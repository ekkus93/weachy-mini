"""RMA-070 calibration dataset contract tests."""

from __future__ import annotations

import copy
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/calibration_data.py"
spec = importlib.util.spec_from_file_location("calibration_data", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load calibration_data")
calibration_data = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = calibration_data
spec.loader.exec_module(calibration_data)


class CalibrationDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.fixture_path = ROOT / "calibration/fixtures/minimal-calibration-dataset.json"
        cls.fixture = json.loads(cls.fixture_path.read_text(encoding="utf-8"))

    def test_complete_fixture_validates_and_covers_all_sample_types(self) -> None:
        summary = calibration_data.validate_dataset(self.fixture)
        self.assertEqual(summary["status"], "ok")
        self.assertEqual(summary["sample_count"], 8)
        self.assertEqual(set(summary["sample_type_counts"]), calibration_data.SAMPLE_TYPES)
        self.assertEqual(summary["synchronization_state"], "synchronized")

    def test_canonical_hash_detects_tampering(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["streams"][0]["samples"][0]["target_position_rad"] = 0.2
        with self.assertRaisesRegex(calibration_data.CalibrationValidationError, "does not match"):
            calibration_data.validate_dataset(changed)

    def test_non_monotonic_timestamp_is_rejected(self) -> None:
        changed = copy.deepcopy(self.fixture)
        stream = changed["streams"][0]
        second = copy.deepcopy(stream["samples"][0])
        second["sequence"] = 2
        second["timestamp_ns"] = stream["samples"][0]["timestamp_ns"] - 1
        stream["samples"].append(second)
        changed = calibration_data.finalize_dataset(changed)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "must be monotonic"
        ):
            calibration_data.validate_dataset(changed)

    def test_missing_non_primary_clock_alignment_is_rejected(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["clock_alignments"] = changed["clock_alignments"][:1]
        changed = calibration_data.finalize_dataset(changed)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "missing alignment"
        ):
            calibration_data.validate_dataset(changed)

    def test_unsynchronized_alignment_cannot_claim_synchronized_capture(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["clock_alignments"][0].update(
            {"method": "unsynchronized", "synchronized": False, "uncertainty_ns": 1_000_000}
        )
        changed = calibration_data.finalize_dataset(changed)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "alignments derive"
        ):
            calibration_data.validate_dataset(changed)

    def test_unknown_fields_fail_closed(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["unexpected"] = True
        changed = calibration_data.finalize_dataset(changed)
        with self.assertRaisesRegex(calibration_data.CalibrationValidationError, "unexpected keys"):
            calibration_data.validate_dataset(changed)

    def test_resource_limits_are_enforced(self) -> None:
        changed = copy.deepcopy(self.fixture)
        limits = calibration_data.ImportLimits(maximum_total_samples=7)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "too many total samples"
        ):
            calibration_data.validate_dataset(changed, limits=limits)

    def test_nonfinite_json_constants_are_rejected_before_validation(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            path = Path(temp_text) / "bad.json"
            path.write_text('{"value": NaN}\n', encoding="utf-8")
            with self.assertRaisesRegex(calibration_data.CalibrationValidationError, "non-finite"):
                calibration_data.load_json_file(path)

    def test_schema_descriptor_matches_committed_files(self) -> None:
        descriptor = calibration_data.schema_descriptor(ROOT / "calibration/schemas")
        self.assertEqual(descriptor, self.fixture["schema"])

    def test_schema_hashes_are_pinned_during_validation(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["schema"]["schema_sha256"] = "0" * 64
        changed = calibration_data.finalize_dataset(changed)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "pinned v1 schema"
        ):
            calibration_data.validate_dataset(changed)

    def test_duplicate_json_object_keys_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            path = Path(temp_text) / "duplicate.json"
            path.write_text('{"value": 1, "value": 2}\n', encoding="utf-8")
            with self.assertRaisesRegex(
                calibration_data.CalibrationValidationError, "duplicate key"
            ):
                calibration_data.load_json_file(path)

    def test_environment_notes_obey_general_string_limit(self) -> None:
        changed = copy.deepcopy(self.fixture)
        changed["environment"]["notes"] = "12345"
        changed = calibration_data.finalize_dataset(changed)
        limits = calibration_data.ImportLimits(maximum_string_length=4)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "maximum string length"
        ):
            calibration_data.validate_dataset(changed, limits=limits)

    def test_clock_collection_limit_is_enforced(self) -> None:
        limits = calibration_data.ImportLimits(maximum_clocks=2)
        with self.assertRaisesRegex(
            calibration_data.CalibrationValidationError, "too many clocks"
        ):
            calibration_data.validate_dataset(self.fixture, limits=limits)

    def test_schema_descriptor_rejects_unversioned_file_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            root = Path(temp_text)
            source = ROOT / "calibration/schemas"
            (root / "calibration-dataset-v1.schema.json").write_bytes(
                (source / "calibration-dataset-v1.schema.json").read_bytes() + b"\n"
            )
            (root / "calibration-stream-columns-v1.json").write_bytes(
                (source / "calibration-stream-columns-v1.json").read_bytes()
            )
            with self.assertRaisesRegex(
                calibration_data.CalibrationValidationError, "pinned v1 hash"
            ):
                calibration_data.schema_descriptor(root)


if __name__ == "__main__":
    unittest.main()
