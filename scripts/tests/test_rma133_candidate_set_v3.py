from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
V1_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates.json"
V3_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v3.json"
SCORER = ROOT / "scripts" / "score_rma133_benchmark.py"

spec = importlib.util.spec_from_file_location("rma133_scorer_v3_contract", SCORER)
if spec is None or spec.loader is None:
    raise RuntimeError("could not load RMA-133 scorer")
scorer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scorer
spec.loader.exec_module(scorer)


class Rma133CandidateSetV3Tests(unittest.TestCase):
    def setUp(self) -> None:
        self.v1 = json.loads(V1_CONFIG.read_text(encoding="utf-8"))
        self.v3 = json.loads(V3_CONFIG.read_text(encoding="utf-8"))

    def test_v3_uses_the_unchanged_v1_benchmark_contract(self) -> None:
        scorer._validate_config(self.v1)
        scorer._validate_config(self.v3)
        self.assertEqual(self.v3["benchmark_id"], self.v1["benchmark_id"])
        self.assertEqual(self.v3["license_policy"], self.v1["license_policy"])
        self.assertEqual(self.v3["runtime_profile"], self.v1["runtime_profile"])
        self.assertEqual(self.v3["selection_policy"], self.v1["selection_policy"])
        self.assertEqual(self.v3["experiment_id"], "rma133-candidate-set-v3")

    def test_v3_keeps_qwen3_control_and_adds_qwen25_coder(self) -> None:
        v1_qwen3 = next(
            candidate
            for candidate in self.v1["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        v3_qwen3 = next(
            candidate
            for candidate in self.v3["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        self.assertEqual(v3_qwen3, v1_qwen3)

        candidate_ids = {candidate["candidate_id"] for candidate in self.v3["candidates"]}
        self.assertEqual(
            candidate_ids,
            {"qwen3-0.6b-q4-k-m", "qwen2.5-coder-0.5b-instruct-q4-k-m"},
        )
        coder = next(
            candidate
            for candidate in self.v3["candidates"]
            if candidate["candidate_id"] == "qwen2.5-coder-0.5b-instruct-q4-k-m"
        )
        self.assertEqual(coder["source_revision"], "bf1da6ca8f02b444067db175f02a14e74f49c5c0")
        self.assertEqual(coder["artifact"]["file_size_bytes"], 491400064)
        self.assertEqual(
            coder["artifact"]["sha256"],
            "1d9614638d18024d0fbb36575a15f1302a3adf044df10345688ec4f6e1c4ff32",
        )
        self.assertEqual(coder["artifact"]["quantization"], "Q4_K_M")
        self.assertEqual(coder["license_id"], "Apache-2.0")
        self.assertEqual(coder["user_prompt_suffix"], "")


if __name__ == "__main__":
    unittest.main()
