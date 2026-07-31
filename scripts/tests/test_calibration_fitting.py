"""RMA-073 fitting, split, signature, and compatibility regression tests."""

from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_fitting = load_module("calibration_fitting", ROOT / "scripts/calibration_fitting.py")
generator = load_module(
    "generate_rma073_synthetic_data", ROOT / "scripts/generate_rma073_synthetic_data.py"
)


class CalibrationFittingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.summary = generator.generate_bundle(self.root)
        self.plan = calibration_fitting.load_fit_plan(self.root / "fit-plan.json")
        self.private_key = ROOT / "calibration/fixtures/keys/rma073-test-ed25519-private.pem"
        self.public_key = ROOT / "calibration/fixtures/keys/rma073-test-ed25519-public.pem"

    def tearDown(self) -> None:
        self.temp.cleanup()

    def fit(self):
        return calibration_fitting.fit_profile(
            self.plan,
            dataset_root=self.root,
            created_utc="2026-07-31T21:03:00Z",
            private_key_path=self.private_key,
            public_key_path=self.public_key,
            public_key_id="rma073-test-key-v1",
        )

    def test_split_is_explicit_and_hash_bound(self) -> None:
        summary = calibration_fitting.validate_fit_plan(self.plan)
        self.assertEqual(summary["fitting_dataset_count"], 1)
        self.assertEqual(summary["heldout_dataset_count"], 1)
        self.assertNotEqual(
            self.plan["datasets"][0]["dataset_sha256"],
            self.plan["datasets"][1]["dataset_sha256"],
        )

    def test_all_parameter_families_fit_and_heldout_passes(self) -> None:
        profile, verification = self.fit()
        self.assertTrue(profile["heldout_validation"]["all_supported_passed"])
        self.assertEqual(profile["heldout_validation"]["supported_family_count"], 7)
        self.assertFalse(verification["calibrated"])
        for family in calibration_fitting.PARAMETER_FAMILIES:
            result = profile["parameter_results"][family]
            self.assertEqual(result["status"], "fitted", family)
            self.assertIn("sensitivity", result)
            self.assertNotEqual(result["confidence"], "not_estimated")

    def test_fitted_values_match_synthetic_truth(self) -> None:
        profile, _ = self.fit()
        values = profile["parameter_results"]
        self.assertAlmostEqual(values["friction"]["values"]["coulomb_friction_nm"], 0.04, places=4)
        self.assertAlmostEqual(values["backlash"]["values"]["backlash_half_width_rad"], 0.003, places=4)
        self.assertAlmostEqual(values["latency"]["values"]["command_latency_s"], 0.03, places=6)
        self.assertAlmostEqual(values["controller"]["values"]["position_gain_nm_per_rad"], 2.4, places=3)
        self.assertAlmostEqual(values["voltage"]["values"]["source_impedance_ohm"], 0.12, places=4)
        self.assertAlmostEqual(values["compliance"]["values"]["compliance_stiffness_nm_per_rad"], 30.0, places=2)
        self.assertAlmostEqual(values["thermal"]["values"]["heating_c_per_a2_s"], 0.08, places=4)

    def test_heldout_data_is_not_used_during_fitting(self) -> None:
        datasets = calibration_fitting.load_datasets(self.plan, self.root)
        first = calibration_fitting.fit_parameters(datasets["fitting"], self.plan)
        changed_heldout = copy.deepcopy(datasets["heldout"])
        changed_heldout[0]["streams"][0]["samples"][0]["target_position_rad"] += 0.5
        second = calibration_fitting.fit_parameters(datasets["fitting"], self.plan)
        self.assertEqual(first, second)

    def test_missing_stream_reports_unsupported_instead_of_fabricating(self) -> None:
        datasets = calibration_fitting.load_datasets(self.plan, self.root)
        changed = copy.deepcopy(datasets["fitting"])
        changed[0]["streams"] = [
            stream for stream in changed[0]["streams"] if stream["sample_type"] != "voltage"
        ]
        results = calibration_fitting.fit_parameters(changed, self.plan)
        self.assertEqual(results["voltage"]["status"], "unsupported")
        self.assertEqual(results["voltage"]["values"], {})

    def test_dataset_hash_mismatch_fails_closed(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["datasets"][0]["dataset_sha256"] = "0" * 64
        changed = calibration_fitting.finalize_fit_plan(changed)
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "does not match loaded dataset"):
            calibration_fitting.load_datasets(changed, self.root)

    def test_path_traversal_is_rejected(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["datasets"][0]["path"] = "../training.json"
        changed = calibration_fitting.finalize_fit_plan(changed)
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "parent traversal"):
            calibration_fitting.validate_fit_plan(changed)

    def test_duplicate_json_keys_fail_closed(self) -> None:
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "duplicate key"):
            calibration_fitting.strict_json_loads('{"value":1,"value":2}')

    def test_schema_descriptor_rejects_unversioned_drift(self) -> None:
        schema_root = self.root / "schemas"
        schema_root.mkdir()
        for name in (
            "calibration-fit-plan-v1.schema.json",
            "calibration-profile-manifest-v1.schema.json",
        ):
            (schema_root / name).write_bytes((ROOT / "calibration/schemas" / name).read_bytes())
        (schema_root / "calibration-fit-plan-v1.schema.json").write_bytes(
            (schema_root / "calibration-fit-plan-v1.schema.json").read_bytes() + b"\n"
        )
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "drifted"):
            calibration_fitting.schema_descriptor(schema_root)

    def test_signature_and_hash_verify(self) -> None:
        profile, verification = self.fit()
        self.assertEqual(verification["status"], "ok")
        self.assertEqual(
            profile["integrity"]["profile_sha256"], calibration_fitting.compute_profile_sha256(profile)
        )

    def test_profile_content_tampering_is_detected(self) -> None:
        profile, _ = self.fit()
        profile["parameter_results"]["friction"]["values"]["coulomb_friction_nm"] += 0.01
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "does not match canonical"):
            calibration_fitting.verify_profile(profile, public_key_path=self.public_key)

    def test_wrong_public_key_is_rejected(self) -> None:
        profile, _ = self.fit()
        other_private = self.root / "other-private.pem"
        other_public = self.root / "other-public.pem"
        subprocess.run(
            ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(other_private)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        subprocess.run(
            ["openssl", "pkey", "-in", str(other_private), "-pubout", "-out", str(other_public)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "public key hash"):
            calibration_fitting.verify_profile(profile, public_key_path=other_public)

    def test_incompatible_model_or_runtime_is_rejected(self) -> None:
        profile, _ = self.fit()
        expected = copy.deepcopy(self.plan["compatibility"])
        expected["model_sha256"] = "0" * 64
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "incompatible"):
            calibration_fitting.verify_profile(
                profile,
                public_key_path=self.public_key,
                expected_compatibility=expected,
            )

    def test_rma073_cannot_sign_a_calibrated_claim(self) -> None:
        datasets = calibration_fitting.load_datasets(self.plan, self.root)
        results = calibration_fitting.fit_parameters(datasets["fitting"], self.plan)
        heldout = calibration_fitting.validate_heldout(results, datasets["heldout"], self.plan)
        manifest = calibration_fitting.build_profile_manifest(
            self.plan, results, heldout, created_utc="2026-07-31T21:03:00Z"
        )
        manifest["calibrated"] = True
        with self.assertRaisesRegex(calibration_fitting.FittingValidationError, "never calibrated"):
            calibration_fitting.sign_profile(
                manifest,
                private_key_path=self.private_key,
                public_key_path=self.public_key,
                public_key_id="rma073-test-key-v1",
            )

    def test_output_is_deterministic_for_identical_inputs(self) -> None:
        first, _ = self.fit()
        second, _ = self.fit()
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main()
