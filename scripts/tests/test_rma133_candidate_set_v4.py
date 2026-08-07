from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
V3_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v3.json"
V4_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v4.json"
V4_PROMPT = ROOT / "benchmarks" / "rma133" / "system_prompt-v4.txt"
SCORER = ROOT / "scripts" / "score_rma133_benchmark.py"
EXPECTED_PROMPT_SHA256 = "0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20"

spec = importlib.util.spec_from_file_location("rma133_scorer_v4_contract", SCORER)
if spec is None or spec.loader is None:
    raise RuntimeError("could not load RMA-133 scorer")
scorer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scorer
spec.loader.exec_module(scorer)


class Rma133CandidateSetV4Tests(unittest.TestCase):
    def setUp(self) -> None:
        self.v3 = json.loads(V3_CONFIG.read_text(encoding="utf-8"))
        self.v4 = json.loads(V4_CONFIG.read_text(encoding="utf-8"))

    def test_v4_changes_only_prompt_contract_and_experiment_identity(self) -> None:
        scorer._validate_config(self.v3)
        scorer._validate_config(self.v4)
        self.assertEqual(self.v4["benchmark_id"], self.v3["benchmark_id"])
        self.assertEqual(self.v4["license_policy"], self.v3["license_policy"])
        self.assertEqual(self.v4["runtime_profile"], self.v3["runtime_profile"])
        self.assertEqual(self.v4["selection_policy"], self.v3["selection_policy"])
        self.assertEqual(self.v4["candidates"], self.v3["candidates"])
        self.assertEqual(self.v4["experiment_id"], "rma133-candidate-set-v4")

    def test_v4_prompt_bytes_are_cryptographically_pinned(self) -> None:
        prompt_contract = self.v4["system_prompt_contract"]
        self.assertEqual(prompt_contract["path"], "benchmarks/rma133/system_prompt-v4.txt")
        self.assertEqual(prompt_contract["sha256"], EXPECTED_PROMPT_SHA256)
        actual = hashlib.sha256(V4_PROMPT.read_bytes()).hexdigest()
        self.assertEqual(actual, EXPECTED_PROMPT_SHA256)

    def test_v4_prompt_targets_measured_v3_failure_modes_without_case_ids(self) -> None:
        prompt = V4_PROMPT.read_text(encoding="utf-8")
        self.assertIn('It is WRONG to write "schema_version":"1".', prompt)
        self.assertIn("CURRENT VALID tracked entity ID", prompt)
        self.assertIn("do not copy or paraphrase the scenario instructions", prompt)
        self.assertIn("do not repeat the requested command or value", prompt)
        for case_id in (
            "warm_greeting",
            "look_red_ball",
            "reject_stale_target",
            "stop_motion",
            "ambiguous_cup",
            "camera_unavailable",
            "accept_compliment",
            "surprise_event",
            "look_at_user",
            "new_blue_cube",
            "reject_raw_actuator",
            "rest_request",
        ):
            self.assertNotIn(case_id, prompt)


if __name__ == "__main__":
    unittest.main()
