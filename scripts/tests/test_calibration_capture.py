"""RMA-071 calibration capture and import tests."""

from __future__ import annotations

import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
for module_name in (
    "calibration_data",
    "capture_reachy_calibration",
    "estimate_calibration_clock_offset",
):
    path = ROOT / "scripts" / f"{module_name}.py"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {module_name}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)

import calibration_data  # noqa: E402
import capture_reachy_calibration as capture  # noqa: E402
import estimate_calibration_clock_offset as clock_offset  # noqa: E402


class CalibrationCaptureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fixture = ROOT / "calibration/fixtures"

    def test_fixture_capture_builds_complete_valid_dataset(self) -> None:
        telemetry_path = self.fixture / "reachy-telemetry.jsonl"
        with telemetry_path.open("r", encoding="utf-8") as handle:
            records, _, _ = capture.read_telemetry_jsonl(
                handle,
                maximum_records=100,
                maximum_bytes=1_000_000,
            )
        streams = capture.group_telemetry_records(records)
        streams.append(
            capture.import_external_pose_csv(
                self.fixture / "external-pose.csv",
                stream_id="external_pose",
                clock_id="camera_clock",
            )
        )
        streams.append(
            capture.import_force_torque_csv(
                self.fixture / "force-torque.csv",
                stream_id="force_torque",
                clock_id="force_clock",
                coordinate_frame="reachy_body",
            )
        )
        dataset = capture.build_dataset(
            dataset_id="fixture-capture",
            created_utc="2026-07-31T19:00:00Z",
            robot=json.loads((self.fixture / "robot-metadata.json").read_text()),
            environment=json.loads((self.fixture / "environment.json").read_text()),
            clock_document=json.loads((self.fixture / "clock-metadata.json").read_text()),
            streams=streams,
            source_files=[],
            schema_root=ROOT / "calibration/schemas",
        )
        summary = calibration_data.validate_dataset(dataset)
        self.assertEqual(summary["sample_count"], 8)
        self.assertEqual(set(summary["sample_type_counts"]), calibration_data.SAMPLE_TYPES)

    def test_jsonl_metadata_cannot_change_mid_stream(self) -> None:
        text = "\n".join(
            [
                json.dumps(
                    {
                        "stream_id": "joint",
                        "sample_type": "joint",
                        "clock_id": "clock_a",
                        "sample": {},
                    }
                ),
                json.dumps(
                    {
                        "stream_id": "joint",
                        "sample_type": "joint",
                        "clock_id": "clock_b",
                        "sample": {},
                    }
                ),
            ]
        )
        records, _, _ = capture.read_telemetry_jsonl(
            io.StringIO(text),
            maximum_records=10,
            maximum_bytes=10_000,
        )
        with self.assertRaisesRegex(ValueError, "changes metadata"):
            capture.group_telemetry_records(records)

    def test_csv_column_drift_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            path = Path(temp_text) / "pose.csv"
            path.write_text("timestamp_ns,sequence\n1,1\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "columns"):
                capture.import_external_pose_csv(path, stream_id="pose", clock_id="camera")

    def test_clock_offset_uses_median_and_conservative_uncertainty(self) -> None:
        alignment = clock_offset.estimate_alignment(
            [(1005, 1000), (2005, 2000), (3010, 3000), (4000, 4000), (5005, 5000)],
            from_clock_id="camera",
            to_clock_id="reachy",
            maximum_uncertainty_ns=10,
            allow_unsynchronized=False,
        )
        self.assertEqual(alignment["offset_ns"], 5)
        self.assertEqual(alignment["uncertainty_ns"], 5)
        self.assertTrue(alignment["synchronized"])

    def test_clock_offset_fails_closed_when_uncertainty_exceeds_budget(self) -> None:
        with self.assertRaisesRegex(ValueError, "exceeds maximum"):
            clock_offset.estimate_alignment(
                [(1000, 1000), (3000, 2000), (3000, 3000)],
                from_clock_id="camera",
                to_clock_id="reachy",
                maximum_uncertainty_ns=10,
                allow_unsynchronized=False,
            )

    def test_allowed_unsynchronized_result_is_explicit(self) -> None:
        alignment = clock_offset.estimate_alignment(
            [(1000, 1000), (3000, 2000), (3000, 3000)],
            from_clock_id="camera",
            to_clock_id="reachy",
            maximum_uncertainty_ns=10,
            allow_unsynchronized=True,
        )
        self.assertFalse(alignment["synchronized"])
        self.assertEqual(alignment["method"], "unsynchronized")
        self.assertGreater(alignment["uncertainty_ns"], 0)

    def test_jsonl_duplicate_keys_are_rejected(self) -> None:
        text = (
            '{"stream_id":"first","stream_id":"second",'
            '"sample_type":"joint","clock_id":"clock","sample":{}}\n'
        )
        with self.assertRaisesRegex(ValueError, "duplicate key"):
            capture.read_telemetry_jsonl(
                io.StringIO(text),
                maximum_records=10,
                maximum_bytes=10_000,
            )

    def test_clock_pair_csv_header_order_and_duplicates_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            path = Path(temp_text) / "pairs.csv"
            path.write_text(
                "source_timestamp_ns,primary_timestamp_ns\n1,2\n3,4\n5,6\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "columns must be exactly"):
                clock_offset.read_pairs(path, 10)

    def test_clock_alignment_rejects_same_source_and_target(self) -> None:
        with self.assertRaisesRegex(ValueError, "must differ"):
            clock_offset.estimate_alignment(
                [(1, 1), (2, 2), (3, 3)],
                from_clock_id="same",
                to_clock_id="same",
                maximum_uncertainty_ns=10,
                allow_unsynchronized=False,
            )


if __name__ == "__main__":
    unittest.main()
