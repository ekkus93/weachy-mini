from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_exact_count(path: Path, old: str, new: str, expected: int) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"{path}: expected {expected} replacement targets, found {count}")
    path.write_text(text.replace(old, new), encoding="utf-8")


v1 = json.loads((ROOT / "benchmarks/rma133/candidates.json").read_text(encoding="utf-8"))
qwen3 = next(candidate for candidate in v1["candidates"] if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m")

v3 = {
    "schema_version": 1,
    "benchmark_id": "rma133-initial-sub1b-v1",
    "experiment_id": "rma133-candidate-set-v3",
    "license_policy": {"allowed_spdx_ids": ["Apache-2.0"]},
    "runtime_profile": dict(v1["runtime_profile"]),
    "selection_policy": dict(v1["selection_policy"]),
    "candidates": [
        qwen3,
        {
            "candidate_id": "qwen2.5-coder-0.5b-instruct-q4-k-m",
            "model_id": "qwen2.5-coder-0.5b-instruct",
            "display_name": "Qwen2.5 Coder 0.5B Instruct Q4_K_M",
            "model_class": "alternative-sub1b",
            "source_uri": "https://huggingface.co/Qwen/Qwen2.5-Coder-0.5B-Instruct-GGUF",
            "source_revision": "bf1da6ca8f02b444067db175f02a14e74f49c5c0",
            "license_id": "Apache-2.0",
            "artifact": {
                "filename": "qwen2.5-coder-0.5b-instruct-q4_k_m.gguf",
                "url": "https://huggingface.co/Qwen/Qwen2.5-Coder-0.5B-Instruct-GGUF/resolve/bf1da6ca8f02b444067db175f02a14e74f49c5c0/qwen2.5-coder-0.5b-instruct-q4_k_m.gguf",
                "file_size_bytes": 491400064,
                "sha256": "1d9614638d18024d0fbb36575a15f1302a3adf044df10345688ec4f6e1c4ff32",
                "quantization": "Q4_K_M",
            },
            "user_prompt_suffix": "",
            "prompt_suffix_reason": "No model-specific suffix is used; the embedded GGUF chat template and frozen benchmark system prompt define the structured-output request.",
        },
    ],
}
(ROOT / "benchmarks/rma133/candidates-v3.json").write_text(json.dumps(v3, indent=2) + "\n", encoding="utf-8")

v3_test = '''from __future__ import annotations

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
'''
(ROOT / "scripts/tests/test_rma133_candidate_set_v3.py").write_text(v3_test, encoding="utf-8")

v2_doc = '''# RMA-133 candidate-set v2 validation

**RMA:** 133  
**Experiment:** `rma133-candidate-set-v2`  
**Status:** `no_candidate_passed` (2026-08-07)  
**Physical runner:** `kawa` / LG-H872 / arm64-v8a / Android API 26

## Frozen contract

Candidate-set v2 retained the original `rma133-initial-sub1b-v1` benchmark contract without changing the behavior corpus, system prompt, runtime profile, quantization class, thresholds, or ranking policy.

The candidates were:

- `qwen3-0.6b-q4-k-m`, retained byte-for-byte as the Qwen3 control from v1.
- `qwen2.5-0.5b-instruct-q4-k-m`, official Qwen Q4_K_M artifact at revision `9217f5db79a29953eb74d5343926648285ec7e67`, size `491400032`, SHA-256 `74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db`, Apache-2.0.

The official Qwen2.5 artifact tuple was corrected after the first v2 acquisition attempt exposed an incorrect recorded SHA-256. The runner rejected that download before inference. No threshold, prompt, model revision, quantization, or scoring rule was changed.

## Evidence hardening

A later complete Qwen2.5 inference attempt exposed a benchmark-evidence defect when a 128-token generation ended in the middle of a UTF-8 code point. The old JSONL representation could therefore become undecodable before the scorer recorded the model failure.

The permanent benchmark now stores exact generated response bytes as lowercase hexadecimal in `response_bytes_hex`. The scorer decodes those bytes with strict UTF-8:

- invalid UTF-8 is an explicit schema failure with semantic score zero;
- bytes are not replacement-decoded, trimmed to a Unicode boundary, or otherwise repaired;
- malformed response bytes cannot crash the scorer; and
- malformed bytes cannot become an eligible response through normalization.

The invalid-UTF-8 behavior is covered by the permanent RMA-133 contract tests and first-party C warnings-as-errors syntax gate.

## Final physical result

The robust physical run was:

- source SHA: `aa18fe5b96848b96326c9d45375cb10ed520d52c`
- workflow run: `31223995663`
- contract job: `93014427659` — success
- physical benchmark job: `93014469563` — expected selector failure because no candidate passed
- evidence artifact: `9011652528`
- artifact digest: `sha256:8d9ef523017277327e5b016831ab469fc64d4520392c3619b9ce9e25bdf28683`

The evidence upload succeeded after the selector rejected the candidate set.

### Qwen3 0.6B Q4_K_M control

- completed cases: 12/12
- schema reliability: 0/12 (`0.000`)
- mean semantic quality: `0.00/100`
- mean decode rate: `5.9489` tokens/s
- mean time to first text: `15888.19` ms
- load time: `1037.72` ms
- peak RSS: `675414016` bytes
- battery temperature: `33.3 C` start, `38.1 C` peak, `38.0 C` final
- temperature rise: `4.8 C`
- eligible: no

The resource envelope passed. Structured behavior output did not.

### Qwen2.5 0.5B Instruct Q4_K_M

- completed cases: 12/12
- schema reliability: 2/12 (`0.1667`)
- mean semantic quality: `10.4167/100`
- mean decode rate: `9.3367` tokens/s
- mean time to first text: `14305.09` ms
- load time: `1817.13` ms
- peak RSS: `561192960` bytes
- battery temperature: `37.1 C` start, `39.0 C` peak/final
- temperature rise: `1.9 C`
- eligible: no

Two cases produced schema-valid objects, but neither satisfied the complete required intent. One 128-token response ended with invalid UTF-8 and was correctly scored as a visible schema failure. Other failures included missing mandatory fields, malformed JSON, prose instead of JSON, missing required gaze targets, and direct repetition of an unsafe motor request.

## Disposition

The selector emitted `status = no_candidate_passed` and `selected_candidate_id = null`.

No v2 candidate is recommended or treated as a default. The thresholds remain unchanged.

Candidate-set v3 evaluates a new predeclared Apache-2.0 sub-1B alternative, Qwen2.5-Coder-0.5B-Instruct Q4_K_M, under the same frozen corpus, runtime profile, thresholds, ranking, and Qwen3 control.
'''
(ROOT / "docs/validation/RMA_133_CANDIDATE_SET_V2_VALIDATION_2026-08-07.md").write_text(v2_doc, encoding="utf-8")

workflow = ROOT / ".github/workflows/rma133-local-llm-benchmark.yml"
replace_exact_count(
    workflow,
    "      - 'scripts/tests/test_rma133_candidate_set_v2.py'\n",
    "      - 'scripts/tests/test_rma133_candidate_set_v2.py'\n      - 'scripts/tests/test_rma133_candidate_set_v3.py'\n",
    2,
)
replace_exact_count(
    workflow,
    "      - 'docs/validation/RMA_133_CANDIDATE_SET_V1_VALIDATION_2026-08-07.md'\n",
    "      - 'docs/validation/RMA_133_CANDIDATE_SET_V1_VALIDATION_2026-08-07.md'\n      - 'docs/validation/RMA_133_CANDIDATE_SET_V2_VALIDATION_2026-08-07.md'\n",
    2,
)
replace_once(
    workflow,
    "            scripts.tests.test_rma133_candidate_set_v2 \\\n            scripts.tests.test_rma133_device_runner_loop\n",
    "            scripts.tests.test_rma133_candidate_set_v2 \\\n            scripts.tests.test_rma133_candidate_set_v3 \\\n            scripts.tests.test_rma133_device_runner_loop\n",
)
replace_once(
    workflow,
    "            --config benchmarks/rma133/candidates-v2.json\n",
    "            --config benchmarks/rma133/candidates-v3.json\n",
)
replace_once(
    workflow,
    "      - name: Benchmark candidate-set v2 and select only if gates pass\n",
    "      - name: Benchmark candidate-set v3 and select only if gates pass\n",
)
replace_once(
    workflow,
    "          RMA133_CONFIG: ${{ github.workspace }}/benchmarks/rma133/candidates-v2.json\n",
    "          RMA133_CONFIG: ${{ github.workspace }}/benchmarks/rma133/candidates-v3.json\n",
)
replace_once(
    workflow,
    "          name: rma133-local-llm-benchmark-v2-${{ github.sha }}\n",
    "          name: rma133-local-llm-benchmark-v3-${{ github.sha }}\n",
)
replace_once(
    workflow,
    "            benchmarks/rma133/candidates-v2.json\n",
    "            benchmarks/rma133/candidates-v2.json\n            benchmarks/rma133/candidates-v3.json\n",
)

print("RMA-133 candidate-set v3 promotion prepared")
