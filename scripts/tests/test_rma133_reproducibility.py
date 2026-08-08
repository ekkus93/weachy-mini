from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from scripts import prepare_rma133_reproducibility_device as prepare
from scripts import run_rma133_device_reproducibility_v6 as reproduce


class Rma133ReproducibilityTests(unittest.TestCase):
    def test_temperature_normalization_matches_frozen_benchmark(self) -> None:
        self.assertEqual(prepare.normalize_temperature(312.0), 31.2)
        self.assertEqual(prepare.normalize_temperature(31200.0), 31.2)
        self.assertEqual(prepare.normalize_temperature(31.2), 31.2)
        self.assertEqual(prepare.normalize_temperature(0.0), -1.0)

    def test_stable_window_requires_full_cool_sixty_seconds(self) -> None:
        old_window = prepare.STABLE_WINDOW_SECONDS
        old_interval = prepare.SAMPLE_INTERVAL_SECONDS
        old_max = prepare.MAX_START_TEMP_C
        old_span = prepare.MAX_STABLE_SPAN_C
        try:
            prepare.STABLE_WINDOW_SECONDS = 60.0
            prepare.SAMPLE_INTERVAL_SECONDS = 10.0
            prepare.MAX_START_TEMP_C = 32.0
            prepare.MAX_STABLE_SPAN_C = 0.3
            samples = [
                {"elapsed_seconds": float(index * 10), "temperature_c": value}
                for index, value in enumerate((31.9, 31.8, 31.8, 31.7, 31.8, 31.7, 31.7))
            ]
            window = prepare.stable_window(samples)
            self.assertIsNotNone(window)
            self.assertEqual(len(window or []), 7)
        finally:
            prepare.STABLE_WINDOW_SECONDS = old_window
            prepare.SAMPLE_INTERVAL_SECONDS = old_interval
            prepare.MAX_START_TEMP_C = old_max
            prepare.MAX_STABLE_SPAN_C = old_span

    def test_stable_window_rejects_warm_or_unstable_device(self) -> None:
        old_window = prepare.STABLE_WINDOW_SECONDS
        old_interval = prepare.SAMPLE_INTERVAL_SECONDS
        old_max = prepare.MAX_START_TEMP_C
        old_span = prepare.MAX_STABLE_SPAN_C
        try:
            prepare.STABLE_WINDOW_SECONDS = 60.0
            prepare.SAMPLE_INTERVAL_SECONDS = 10.0
            prepare.MAX_START_TEMP_C = 32.0
            prepare.MAX_STABLE_SPAN_C = 0.3
            warm = [
                {"elapsed_seconds": float(index * 10), "temperature_c": 32.1} for index in range(7)
            ]
            unstable = [
                {"elapsed_seconds": float(index * 10), "temperature_c": value}
                for index, value in enumerate((31.5, 31.6, 31.7, 31.8, 31.9, 31.8, 31.7))
            ]
            self.assertIsNone(prepare.stable_window(warm))
            self.assertIsNone(prepare.stable_window(unstable))
        finally:
            prepare.STABLE_WINDOW_SECONDS = old_window
            prepare.SAMPLE_INTERVAL_SECONDS = old_interval
            prepare.MAX_START_TEMP_C = old_max
            prepare.MAX_STABLE_SPAN_C = old_span

    def test_precondition_contract_is_exact_and_fail_closed(self) -> None:
        old_path = reproduce.PRECONDITION
        try:
            with tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "precondition.json"
                reproduce.PRECONDITION = path
                valid = {
                    "schema_version": 1,
                    "protocol": "rma133-v6-cool-start-reproducibility-v1",
                    "status": "passed",
                    "limits": {"maximum_start_temperature_c": 32.0},
                    "device": {"serial": "device-1"},
                }
                path.write_text(json.dumps(valid), encoding="utf-8")
                loaded = reproduce.require_precondition()
                self.assertEqual(loaded["status"], "passed")

                valid["limits"]["maximum_start_temperature_c"] = 33.0
                path.write_text(json.dumps(valid), encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    reproduce.require_precondition()

                valid["limits"]["maximum_start_temperature_c"] = 32.0
                valid["status"] = "invalid_environment"
                path.write_text(json.dumps(valid), encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    reproduce.require_precondition()
        finally:
            reproduce.PRECONDITION = old_path

    def test_stale_process_probe_cannot_match_itself(self) -> None:
        source = Path(prepare.__file__).read_text(encoding="utf-8")
        scan_start = source.index('script = r"""')
        scan_end = source.index('"""', scan_start + len('script = r"""'))
        scan_script = source[scan_start:scan_end]
        self.assertNotIn("*rma133_benchmark_v6*", scan_script)
        self.assertIn("*rma133_benchmark_v[6]*", scan_script)

    def test_reproducibility_targets_only_frozen_selected_candidate(self) -> None:
        config = json.loads(reproduce.base.CONFIG.read_text(encoding="utf-8"))
        candidate = reproduce.selected_candidate(config)
        self.assertEqual(candidate["candidate_id"], "qwen3-0.6b-q4-k-m")
        self.assertEqual(
            candidate["artifact"]["sha256"],
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e",
        )
        self.assertEqual(candidate["artifact"]["file_size_bytes"], 396704416)
        self.assertEqual(
            reproduce.ACCEPTED_V6_SOURCE_SHA,
            "e3007579d0365d31f5d5efc378fc81a13f2d705e",
        )
        self.assertEqual(reproduce.FIRST_CASE_MAX_START_TEMP_C, 32.5)

    def test_reproducibility_runner_does_not_run_selector_or_repair_output(self) -> None:
        source = Path(reproduce.__file__).read_text(encoding="utf-8")
        self.assertNotIn('"select"', source)
        self.assertNotIn("strip_markdown", source)
        self.assertNotIn("json_repair", source)
        self.assertIn("base.benchmark_command", source)
        self.assertIn("base.SCORER", source)
        self.assertIn('"candidate_gate_failure"', source)
        self.assertIn('"invalid_environment"', source)


if __name__ == "__main__":
    unittest.main()
