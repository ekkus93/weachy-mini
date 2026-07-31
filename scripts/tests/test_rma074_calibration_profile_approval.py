"""RMA-074 calibrated profile approval and UI label contract tests."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/calibration_profile_approval.py"
spec = importlib.util.spec_from_file_location("calibration_profile_approval", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("cannot load calibration_profile_approval")
approval = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = approval
spec.loader.exec_module(approval)


class CalibrationProfileApprovalTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.private_key = self.root / "approval-private.pem"
        self.public_key = self.root / "approval-public.pem"
        subprocess.run(
            ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(self.private_key)],
            check=True,
            capture_output=True,
        )
        subprocess.run(
            [
                "openssl",
                "pkey",
                "-in",
                str(self.private_key),
                "-pubout",
                "-out",
                str(self.public_key),
            ],
            check=True,
            capture_output=True,
        )
        self.hardware = "1" * 64
        self.compatibility = {
            "reachy_source_commit": "a" * 40,
            "model_sha256": "2" * 64,
            "mujoco_version": "3.9.0",
            "simulator_abi_version": 2,
            "servo_contracts": {
                "servo": "rma061_servo_model_v1",
                "electrical": "rma062_electrical_controller_v1",
                "mechanical": "rma063_mechanical_effects_v1",
                "power_thermal": "rma064_power_thermal_v1",
            },
        }
        self.candidate = {
            "contract_id": "rma073_calibration_profile_manifest_v1",
            "profile_id": "reachy-unit-fit-v1",
            "calibrated": False,
            "approval_state": "unapproved_fit_candidate",
            "compatibility": copy.deepcopy(self.compatibility),
            "datasets": [
                {
                    "dataset_id": "reachy-unit-fitting-v1",
                    "dataset_sha256": "3" * 64,
                    "role": "fitting",
                },
                {
                    "dataset_id": "reachy-unit-heldout-v1",
                    "dataset_sha256": "4" * 64,
                    "role": "heldout",
                },
            ],
            "parameter_results": {
                "friction": {
                    "status": "fitted",
                    "values": {"coulomb_friction_nm": 0.04},
                }
            },
            "integrity": {"profile_sha256": "5" * 64},
            "signature": {"public_key_sha256": "6" * 64},
        }
        self.preflight = {
            "contract_id": "rma074_physical_preflight_v1",
            "result": "physical_unit_ready",
            "physical_unit": {
                "hardware_id_sha256": self.hardware,
                "motion_commands_issued": 0,
                "torque_commands_issued": 0,
            },
        }
        self.dataset_evidence = [
            {
                "dataset_id": "reachy-unit-fitting-v1",
                "dataset_sha256": "3" * 64,
                "role": "fitting",
                "source_kind": "physical_reachy_mini",
                "hardware_id_sha256": self.hardware,
                "capture_run_id": "physical-fitting-run-001",
                "physical_motion": True,
                "artifact_sha256": "7" * 64,
            },
            {
                "dataset_id": "reachy-unit-heldout-v1",
                "dataset_sha256": "4" * 64,
                "role": "heldout",
                "source_kind": "physical_reachy_mini",
                "hardware_id_sha256": self.hardware,
                "capture_run_id": "physical-heldout-run-001",
                "physical_motion": True,
                "artifact_sha256": "8" * 64,
            },
        ]
        self.heldout = self.make_heldout()

    def tearDown(self) -> None:
        self.temp.cleanup()

    @staticmethod
    def candidate_verifier(profile, public_key_path, expected_compatibility):
        del public_key_path
        if profile["compatibility"] != expected_compatibility:
            raise ValueError("compatibility mismatch")
        return {"status": "ok"}

    def make_metric(self, name: str, *, status: str = "passed") -> dict:
        if status == "unsupported":
            return {
                "status": "unsupported",
                "metric": f"{name}_error",
                "unit": "n/a",
                "value": None,
                "threshold": None,
                "sample_count": 0,
                "source_streams": [],
                "claim_scope": f"{name} accuracy",
                "reason": "required instrument was not present",
            }
        return {
            "status": status,
            "metric": f"{name}_error",
            "unit": "rad",
            "value": 0.01 if status == "passed" else 0.2,
            "threshold": 0.05,
            "sample_count": 25,
            "source_streams": ["joint"],
            "claim_scope": f"{name} accuracy",
        }

    def make_heldout(self) -> dict:
        metrics = {name: self.make_metric(name) for name in approval.REQUIRED_METRICS}
        metrics["current"] = self.make_metric("current", status="unsupported")
        metrics["contact"] = self.make_metric("contact", status="unsupported")
        report = {
            "contract_id": "rma074_physical_heldout_report_v1",
            "report_id": "physical-heldout-report-001",
            "created_utc": "2026-07-31T23:00:00Z",
            "hardware_id_sha256": self.hardware,
            "fitting_dataset_sha256": "3" * 64,
            "heldout_dataset_sha256": "4" * 64,
            "metrics": metrics,
            "notes": "Unit-test evidence only; not a physical calibration record.",
        }
        report["report_sha256"] = hashlib.sha256(
            approval.canonical_json_bytes(report)
        ).hexdigest()
        return report

    def create(self, **overrides):
        kwargs = {
            "approval_id": "unit-calibration-approval-v1",
            "created_utc": "2026-07-31T23:01:00Z",
            "candidate_profile": self.candidate,
            "candidate_public_key_path": self.public_key,
            "expected_compatibility": self.compatibility,
            "preflight_report": self.preflight,
            "dataset_evidence": self.dataset_evidence,
            "heldout_report": self.heldout,
            "approval_private_key_path": self.private_key,
            "approval_public_key_path": self.public_key,
            "approval_public_key_id": "production-approval-test-key",
            "approver_statement": (
                "I reviewed the physical split and held-out thresholds for this unit."
            ),
            "candidate_verifier": self.candidate_verifier,
        }
        kwargs.update(overrides)
        return approval.create_approval(**kwargs)

    def test_valid_scoped_approval_signs_verifies_and_labels(self) -> None:
        document = self.create()
        result = approval.verify_approval(
            document,
            public_key_path=self.public_key,
            expected_compatibility=self.compatibility,
            expected_hardware_id_sha256=self.hardware,
        )
        self.assertTrue(result["calibrated"])
        self.assertEqual(result["limited_metrics"], ["contact", "current"])
        self.assertNotIn("contact accuracy", document["claims"]["mature_accuracy_claims"])
        label = approval.resolve_calibration_label(
            document,
            public_key_path=self.public_key,
            expected_compatibility=self.compatibility,
            connected_hardware_id_sha256=self.hardware,
        )
        self.assertTrue(label.calibrated)
        self.assertEqual(label.label, "Calibrated for this unit")

    def test_missing_or_wrong_unit_resolves_uncalibrated(self) -> None:
        missing = approval.resolve_calibration_label(
            None,
            public_key_path=self.public_key,
            expected_compatibility=self.compatibility,
            connected_hardware_id_sha256=self.hardware,
        )
        self.assertFalse(missing.calibrated)
        document = self.create()
        wrong = approval.resolve_calibration_label(
            document,
            public_key_path=self.public_key,
            expected_compatibility=self.compatibility,
            connected_hardware_id_sha256="9" * 64,
        )
        self.assertFalse(wrong.calibrated)
        self.assertIn("does not match connected unit", wrong.reason)

    def test_synthetic_dataset_cannot_be_approved(self) -> None:
        candidate = copy.deepcopy(self.candidate)
        evidence = copy.deepcopy(self.dataset_evidence)
        candidate["datasets"][0]["dataset_id"] = "synthetic-fitting"
        evidence[0]["dataset_id"] = "synthetic-fitting"
        with self.assertRaisesRegex(approval.ApprovalValidationError, "synthetic"):
            self.create(candidate_profile=candidate, dataset_evidence=evidence)

    def test_fitting_and_heldout_must_be_separate_runs(self) -> None:
        evidence = copy.deepcopy(self.dataset_evidence)
        evidence[1]["capture_run_id"] = evidence[0]["capture_run_id"]
        with self.assertRaisesRegex(
            approval.ApprovalValidationError, "separate physical runs"
        ):
            self.create(dataset_evidence=evidence)

    def test_core_metric_must_pass(self) -> None:
        heldout = copy.deepcopy(self.heldout)
        heldout["metrics"]["head_orientation"] = self.make_metric(
            "head_orientation", status="failed"
        )
        heldout.pop("report_sha256")
        heldout["report_sha256"] = hashlib.sha256(
            approval.canonical_json_bytes(heldout)
        ).hexdigest()
        with self.assertRaisesRegex(
            approval.ApprovalValidationError, "core calibration metric"
        ):
            self.create(heldout_report=heldout)

    def test_fixture_approval_key_is_blocked(self) -> None:
        fixture_private = ROOT / "calibration/fixtures/keys/rma073-test-ed25519-private.pem"
        fixture_public = ROOT / "calibration/fixtures/keys/rma073-test-ed25519-public.pem"
        with self.assertRaisesRegex(approval.ApprovalValidationError, "fixture key"):
            self.create(
                approval_private_key_path=fixture_private,
                approval_public_key_path=fixture_public,
            )

    def test_content_tampering_fails_closed(self) -> None:
        document = self.create()
        tampered = copy.deepcopy(document)
        tampered["parameter_results"]["friction"]["values"][
            "coulomb_friction_nm"
        ] = 0.5
        with self.assertRaisesRegex(
            approval.ApprovalValidationError, "does not match content"
        ):
            approval.verify_approval(
                tampered,
                public_key_path=self.public_key,
                expected_compatibility=self.compatibility,
                expected_hardware_id_sha256=self.hardware,
            )

    def test_compatibility_mismatch_resolves_uncalibrated(self) -> None:
        document = self.create()
        incompatible = copy.deepcopy(self.compatibility)
        incompatible["mujoco_version"] = "4.0.0"
        label = approval.resolve_calibration_label(
            document,
            public_key_path=self.public_key,
            expected_compatibility=incompatible,
            connected_hardware_id_sha256=self.hardware,
        )
        self.assertFalse(label.calibrated)
        self.assertIn("compatibility", label.reason)


if __name__ == "__main__":
    unittest.main()
