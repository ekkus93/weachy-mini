from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
V1_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates.json"
V2_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v2.json"
SCORER = ROOT / "scripts" / "score_rma133_benchmark.py"

spec = importlib.util.spec_from_file_location("rma133_scorer_v2_contract", SCORER)
if spec is None or spec.loader is None:
    raise RuntimeError("could not load RMA-133 scorer")
scorer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scorer
spec.loader.exec_module(scorer)


class Rma133CandidateSetV2Tests(unittest.TestCase):
    def setUp(self) -> None:
        self.v1 = json.loads(V1_CONFIG.read_text(encoding="utf-8"))
        self.v2 = json.loads(V2_CONFIG.read_text(encoding="utf-8"))

    def test_v2_uses_the_unchanged_v1_benchmark_contract(self) -> None:
        scorer._validate_config(self.v1)
        scorer._validate_config(self.v2)
        self.assertEqual(self.v2["benchmark_id"], self.v1["benchmark_id"])
        self.assertEqual(self.v2["license_policy"], self.v1["license_policy"])
        self.assertEqual(self.v2["runtime_profile"], self.v1["runtime_profile"])
        self.assertEqual(self.v2["selection_policy"], self.v1["selection_policy"])
        self.assertEqual(self.v2["experiment_id"], "rma133-candidate-set-v2")

    def test_v2_keeps_qwen3_control_and_adds_qwen25(self) -> None:
        v1_qwen3 = next(
            candidate
            for candidate in self.v1["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        v2_qwen3 = next(
            candidate
            for candidate in self.v2["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        self.assertEqual(v2_qwen3, v1_qwen3)

        candidate_ids = {candidate["candidate_id"] for candidate in self.v2["candidates"]}
        self.assertEqual(
            candidate_ids,
            {"qwen3-0.6b-q4-k-m", "qwen2.5-0.5b-instruct-q4-k-m"},
        )
        qwen25 = next(
            candidate
            for candidate in self.v2["candidates"]
            if candidate["candidate_id"] == "qwen2.5-0.5b-instruct-q4-k-m"
        )
        self.assertEqual(qwen25["source_revision"], "9217f5db79a29953eb74d5343926648285ec7e67")
        self.assertEqual(qwen25["artifact"]["file_size_bytes"], 491400032)
        self.assertEqual(
            qwen25["artifact"]["sha256"],
            "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
        )
        self.assertEqual(qwen25["artifact"]["quantization"], "Q4_K_M")
        self.assertEqual(qwen25["license_id"], "Apache-2.0")
        self.assertEqual(qwen25["user_prompt_suffix"], "")


if __name__ == "__main__":
    unittest.main()
