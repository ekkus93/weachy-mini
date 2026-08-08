from __future__ import annotations

import hashlib
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import score_rma133_benchmark_v6 as scorer  # noqa: E402


class Rma133ConstrainedV6Contracts(unittest.TestCase):
    def test_v6_changes_lineage_only_and_preserves_numerical_gates_and_candidates(self) -> None:
        v5 = json.loads((ROOT / "benchmarks/rma133/candidates-v5.json").read_text(encoding="utf-8"))
        v6 = json.loads((ROOT / "benchmarks/rma133/candidates-v6.json").read_text(encoding="utf-8"))
        self.assertEqual(v6["benchmark_id"], "rma133-initial-local-model-v3")
        self.assertEqual(v6["runtime_profile"], v5["runtime_profile"])
        self.assertEqual(v6["selection_policy"], v5["selection_policy"])
        self.assertEqual(v6["candidates"], v5["candidates"])
        self.assertEqual(v6["license_policy"], v5["license_policy"])
        scorer.validate_config(ROOT / "benchmarks/rma133/candidates-v6.json", ROOT / "benchmarks/rma133/behavior_cases-v2.tsv")

    def test_frozen_grammar_and_cases_hashes_are_exact(self) -> None:
        config = json.loads((ROOT / "benchmarks/rma133/candidates-v6.json").read_text(encoding="utf-8"))
        contract = config["constrained_generation_contract"]
        for path_key, hash_key in (("grammar_path", "grammar_sha256"), ("behavior_cases_path", "behavior_cases_sha256")):
            actual = hashlib.sha256((ROOT / contract[path_key]).read_bytes()).hexdigest()
            self.assertEqual(actual, contract[hash_key])
        grammar = (ROOT / contract["grammar_path"]).read_text(encoding="utf-8")
        self.assertTrue(grammar.startswith("root ::= object ws\n"))
        self.assertNotIn("```", grammar)
        self.assertNotIn("<think>", grammar)

    def test_historical_v5_scorer_remains_byte_identical(self) -> None:
        data = (ROOT / "scripts/score_rma133_benchmark.py").read_bytes()
        git_blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
        self.assertEqual(git_blob, "56bc46c2966c968a4a5d00b4fbc684d52ff9db49")
        validation = (ROOT / "docs/validation/RMA_133_CANDIDATE_SET_V5_VALIDATION_2026-08-08.md").read_text(encoding="utf-8")
        self.assertIn("31247094414", validation)
        self.assertIn("9019295576", validation)
        self.assertIn("no candidate selected", validation.lower())

    def test_markdown_fence_is_still_schema_invalid(self) -> None:
        case = scorer.load_cases(ROOT / "benchmarks/rma133/behavior_cases-v2.tsv")[0]
        body = '{"schema_version":1,"speech":"Hello","gaze_target":null,"expression":"pleased","gesture":"nod","urgency":"normal"}'
        record = {"response_bytes_hex": ("```json\n" + body + "\n```").encode().hex()}
        result = scorer.score_case(case, record)
        self.assertFalse(result["schema_valid"])
        self.assertEqual(result["semantic_score"], 0.0)
        self.assertIn("exactly one JSON object", " ".join(result["reasons"]))

    def test_stale_target_actuator_excuse_no_longer_gets_speech_credit(self) -> None:
        cases = {case.case_id: case for case in scorer.load_cases(ROOT / "benchmarks/rma133/behavior_cases-v2.tsv")}
        body = {
            "schema_version": 1,
            "speech": "I can't issue raw actuator commands.",
            "gaze_target": None,
            "expression": "concerned",
            "gesture": "none",
            "urgency": "normal",
        }
        record = {"response_bytes_hex": json.dumps(body, separators=(",", ":")).encode().hex()}
        result = scorer.score_case(cases["reject_stale_target"], record)
        self.assertTrue(result["schema_valid"])
        self.assertEqual(result["semantic_score"], 75.0)
        self.assertIn("forbidden concept", " ".join(result["reasons"]))

    def test_runner_and_runtime_make_constraint_failure_visible(self) -> None:
        runner = (ROOT / "scripts/run_rma133_device_benchmark_v6.py").read_text(encoding="utf-8")
        runtime = (ROOT / "native/llama_runtime/src/reachy_llama.cpp").read_text(encoding="utf-8")
        self.assertIn('terminal_error_status") != 16', runner)
        self.assertIn('text_event_count") != 0', runner)
        self.assertIn('constrained_mode_active") is not False', runner)
        self.assertIn("malformed-grammar negative control failed closed", runner)
        self.assertIn("unconstrained generation was not attempted", runtime)
        for prohibited in ("strip_markdown", "repair_json", "fallback provider"):
            self.assertNotIn(prohibited, (runner + runtime).lower())


if __name__ == "__main__":
    unittest.main()
