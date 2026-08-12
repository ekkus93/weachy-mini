from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "benchmarks" / "rma133" / "candidates.json"
CASES = ROOT / "benchmarks" / "rma133" / "behavior_cases.tsv"
SCORER = ROOT / "scripts" / "score_rma133_benchmark.py"
SYSTEM_PROMPT = ROOT / "benchmarks" / "rma133" / "system_prompt.txt"
# rma133_benchmark.c is split (docs/LARGE_FILE_REFACTOR_TODO_3.md, file #5)
# into several files in the same directory. List every target explicitly
# (rather than a glob) so this check doesn't accidentally pull in the
# unrelated rma133_benchmark_v6.c sibling, and concatenate whichever of them
# currently exist so it still covers the full implementation regardless of
# which file each part now lives in.
BENCHMARK_DIR = ROOT / "native" / "llama_runtime" / "benchmark"
BENCHMARK_SOURCE_FILES = [
    "rma133_benchmark.c",
    "rma133_benchmark_args.c",
    "rma133_benchmark_platform.c",
    "rma133_benchmark_output.c",
    "rma133_benchmark_generation.c",
]
DEVICE_RUNNER = ROOT / "scripts" / "run_rma133_device_benchmark.sh"

spec = importlib.util.spec_from_file_location("rma133_scorer", SCORER)
if spec is None or spec.loader is None:
    raise RuntimeError("could not load RMA-133 scorer")
scorer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scorer
spec.loader.exec_module(scorer)


class Rma133BenchmarkContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = json.loads(CONFIG.read_text(encoding="utf-8"))
        self.cases = scorer._load_cases(CASES)

    def _response_for_case(self, case: object) -> str:
        response = {
            "schema_version": 1,
            "speech": case.speech_any_terms[0] if case.speech_any_terms else "Okay.",
            "expression": case.expression,
            "gesture": case.gesture,
            "urgency": case.urgency,
        }
        if case.gaze_kind is not None:
            response["gaze_target"] = {
                "kind": case.gaze_kind,
                "entity_id": case.gaze_entity,
            }
        return json.dumps(response, separators=(",", ":"))

    def _write_raw(
        self,
        directory: Path,
        candidate_id: str,
        *,
        response_override: dict[str, str] | None = None,
        response_bytes_override: dict[str, bytes] | None = None,
        battery_before: float | None = 30.0,
        battery_after: float | None = 32.0,
    ) -> Path:
        candidate = next(
            item for item in self.config["candidates"] if item["candidate_id"] == candidate_id
        )
        records: list[dict[str, object]] = [
            {
                "record": "model",
                "candidate_id": candidate_id,
                "load_time_ms": 100.0,
                "tensor_bytes": candidate["artifact"]["file_size_bytes"],
                "parameter_count": 600_000_000,
                "training_context_tokens": 32768,
                "peak_rss_bytes": 500_000_000,
                "battery_temp_before_c": battery_before,
                "thermal_zone_max_before_c": None,
            }
        ]
        for index, case in enumerate(self.cases):
            response = self._response_for_case(case)
            if response_override and case.case_id in response_override:
                response = response_override[case.case_id]
            response_bytes = response.encode("utf-8")
            if response_bytes_override and case.case_id in response_bytes_override:
                response_bytes = response_bytes_override[case.case_id]
            records.append(
                {
                    "record": "case",
                    "candidate_id": candidate_id,
                    "case_id": case.case_id,
                    "completed": True,
                    "prompt_tokens": 100,
                    "generated_tokens": 20,
                    "time_to_first_text_ms": 250.0,
                    "total_time_ms": 1500.0,
                    "decode_tokens_per_second": 15.2,
                    "peak_rss_bytes": 600_000_000,
                    "battery_temp_before_c": 30.0 + index * 0.1,
                    "battery_temp_c": 30.1 + index * 0.1,
                    "response_bytes_hex": response_bytes.hex(),
                }
            )
        records.append(
            {
                "record": "summary",
                "candidate_id": candidate_id,
                "case_count": len(self.cases),
                "all_completed": True,
                "peak_rss_bytes": 600_000_000,
                "battery_temp_after_c": battery_after,
                "thermal_zone_max_after_c": None,
            }
        )
        raw = directory / f"{candidate_id}.jsonl"
        raw.write_text(
            "".join(json.dumps(record, separators=(",", ":")) + "\n" for record in records),
            encoding="utf-8",
        )
        return raw

    def test_frozen_candidate_contract_has_qwen_and_alternative(self) -> None:
        scorer._validate_config(self.config)
        self.assertEqual(len(self.cases), 12)
        candidates = self.config["candidates"]
        self.assertEqual({item["license_id"] for item in candidates}, {"Apache-2.0"})
        self.assertEqual({item["artifact"]["quantization"] for item in candidates}, {"Q4_K_M"})
        self.assertIn("qwen3-0.6b-class", {item["model_class"] for item in candidates})
        self.assertIn("alternative-sub1b", {item["model_class"] for item in candidates})
        for candidate in candidates:
            revision = candidate["source_revision"]
            artifact = candidate["artifact"]
            self.assertIn(revision, artifact["url"])
            self.assertEqual(len(artifact["sha256"]), 64)
            self.assertGreater(artifact["file_size_bytes"], 0)

    def test_frozen_threshold_mutation_is_rejected(self) -> None:
        mutated = json.loads(json.dumps(self.config))
        mutated["selection_policy"]["minimum_semantic_quality_score"] = 80.0
        with self.assertRaisesRegex(ValueError, "selection threshold changed"):
            scorer._validate_config(mutated)

    def test_perfect_evidence_passes_and_records_ttft_and_peak_temperature(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            raw = self._write_raw(directory, candidate_id)
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        self.assertTrue(report["eligible"])
        self.assertEqual(report["measurements"]["schema_reliability"], 1.0)
        self.assertEqual(report["measurements"]["semantic_quality_score"], 100.0)
        self.assertEqual(report["measurements"]["mean_time_to_first_text_ms"], 250.0)
        self.assertAlmostEqual(report["measurements"]["battery_peak_temp_c"], 32.0)

    def test_malformed_json_fails_schema_gate_without_repair(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            case_id = self.cases[0].case_id
            raw = self._write_raw(
                directory, candidate_id, response_override={case_id: "```json {} ```"}
            )
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        self.assertFalse(report["eligible"])
        self.assertLess(report["measurements"]["schema_reliability"], 1.0)
        self.assertTrue(
            any("schema reliability" in reason for reason in report["rejection_reasons"])
        )

    def test_invalid_utf8_response_fails_schema_gate_without_crashing_scorer(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            case_id = self.cases[0].case_id
            raw = self._write_raw(
                directory,
                candidate_id,
                response_bytes_override={case_id: b'{"speech":"\xf0\x9f'},
            )
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        score = next(item for item in report["case_scores"] if item["case_id"] == case_id)
        self.assertFalse(score["schema_valid"])
        self.assertEqual(score["semantic_score"], 0.0)
        self.assertIn("response is not valid UTF-8", score["reasons"])
        self.assertFalse(report["eligible"])

    def test_invented_gaze_entity_reduces_quality(self) -> None:
        target_case = next(case for case in self.cases if case.gaze_entity is not None)
        bad = json.loads(self._response_for_case(target_case))
        bad["gaze_target"]["entity_id"] = "entity-999"
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            raw = self._write_raw(
                directory,
                candidate_id,
                response_override={target_case.case_id: json.dumps(bad)},
            )
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        score = next(
            item for item in report["case_scores"] if item["case_id"] == target_case.case_id
        )
        self.assertTrue(score["schema_valid"])
        self.assertLess(score["semantic_score"], 100.0)

    def test_raw_actuation_key_is_rejected(self) -> None:
        target_case = self.cases[0]
        bad = json.loads(self._response_for_case(target_case))
        bad["motor_command"] = 90
        value, reasons = scorer._validate_behavior_object(json.dumps(bad))
        self.assertIsNotNone(value)
        self.assertIn("unsafe raw-actuation key present", reasons)

    def test_missing_thermal_telemetry_is_ineligible(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            raw = self._write_raw(
                directory,
                candidate_id,
                battery_before=None,
                battery_after=None,
            )
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        self.assertFalse(report["eligible"])
        self.assertIn(
            "battery temperature was not measurable on the device", report["rejection_reasons"]
        )

    def test_selection_requires_every_candidate_report(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            raw = self._write_raw(directory, candidate_id)
            report_path = directory / "report.json"
            scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=report_path,
            )
            with self.assertRaisesRegex(
                ValueError, "exactly one report for every frozen candidate"
            ):
                scorer.select_candidate(
                    config_path=CONFIG,
                    report_paths=[report_path],
                    output_path=directory / "selection.json",
                )

    def test_benchmark_has_no_network_or_model_fallback_path(self) -> None:
        source = "".join(
            (BENCHMARK_DIR / name).read_text(encoding="utf-8")
            for name in BENCHMARK_SOURCE_FILES
            if (BENCHMARK_DIR / name).exists()
        )
        runner = DEVICE_RUNNER.read_text(encoding="utf-8")
        prompt = SYSTEM_PROMPT.read_text(encoding="utf-8")
        self.assertNotIn("http://", source)
        self.assertNotIn("https://", source)
        self.assertNotIn("fallback", source.casefold())
        self.assertIn("artifact integrity failure", runner)
        self.assertIn("thermal safety", source.casefold())
        self.assertIn("response_bytes_hex", source)
        self.assertIn("json_hex_bytes(stdout, response, response_length);", source)
        self.assertIn("Never emit joint angles", prompt)
        self.assertNotIn("cloud", source.casefold())


if __name__ == "__main__":
    unittest.main()
