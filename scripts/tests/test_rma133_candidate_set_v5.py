from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
V4_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v4.json"
V5_CONFIG = ROOT / "benchmarks" / "rma133" / "candidates-v5.json"
SCORER = ROOT / "scripts" / "score_rma133_benchmark.py"

spec = importlib.util.spec_from_file_location("rma133_scorer_v5_contract", SCORER)
if spec is None or spec.loader is None:
    raise RuntimeError("could not load RMA-133 scorer")
scorer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scorer
spec.loader.exec_module(scorer)


class Rma133CandidateSetV5Tests(unittest.TestCase):
    def setUp(self) -> None:
        self.v4 = json.loads(V4_CONFIG.read_text(encoding="utf-8"))
        self.v5 = json.loads(V5_CONFIG.read_text(encoding="utf-8"))

    def test_v5_relaxes_only_candidate_size_scope(self) -> None:
        scorer._validate_config(self.v4)
        scorer._validate_config(self.v5)
        self.assertEqual(self.v5["benchmark_id"], "rma133-initial-local-model-v2")
        self.assertEqual(self.v5["experiment_id"], "rma133-candidate-set-v5")
        self.assertEqual(self.v5["system_prompt_contract"], self.v4["system_prompt_contract"])
        self.assertEqual(self.v5["license_policy"], self.v4["license_policy"])
        self.assertEqual(self.v5["runtime_profile"], self.v4["runtime_profile"])
        self.assertEqual(self.v5["selection_policy"], self.v4["selection_policy"])
        self.assertEqual(
            self.v5["model_size_policy"]["constraint"],
            "up-to-2B-class",
        )
        self.assertEqual(self.v5["model_size_policy"]["relaxed_from"], "sub-1B")

    def test_v5_retains_qwen_control_and_pins_larger_coder(self) -> None:
        v4_qwen = next(
            candidate
            for candidate in self.v4["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        v5_qwen = next(
            candidate
            for candidate in self.v5["candidates"]
            if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
        )
        self.assertEqual(v5_qwen, v4_qwen)

        larger = next(
            candidate
            for candidate in self.v5["candidates"]
            if candidate["candidate_id"] == "qwen2.5-coder-1.5b-instruct-q4-k-m"
        )
        self.assertEqual(larger["model_class"], "alternative-local")
        self.assertEqual(larger["license_id"], "Apache-2.0")
        self.assertEqual(larger["source_revision"], "2ab9f8f42af02fc212effaef7c4850c885e965f4")
        self.assertEqual(
            larger["artifact"]["filename"],
            "qwen2.5-coder-1.5b-instruct-q4_k_m.gguf",
        )
        self.assertEqual(larger["artifact"]["file_size_bytes"], 1_117_320_768)
        self.assertEqual(
            larger["artifact"]["sha256"],
            "cc324af070c2ecbfd324a30884d2f951a7ff756aba85cb811a6ec436933bb046",
        )
        self.assertEqual(larger["artifact"]["quantization"], "Q4_K_M")
        self.assertEqual(larger["user_prompt_suffix"], "")
        self.assertGreater(larger["artifact"]["file_size_bytes"], 1_000_000_000)


if __name__ == "__main__":
    unittest.main()
