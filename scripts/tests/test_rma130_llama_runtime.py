from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PINNED_COMMIT = "dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb"
PINNED_VERSION = "b10313"


class Rma130LlamaRuntimeContracts(unittest.TestCase):
    def test_source_lock_and_inventory_match_exact_release(self) -> None:
        lock = json.loads((ROOT / "third_party/llama-cpp-source.lock.json").read_text(encoding="utf-8"))
        self.assertEqual(lock["schema_version"], 1)
        self.assertEqual(lock["version"], PINNED_VERSION)
        self.assertEqual(lock["commit"], PINNED_COMMIT)
        self.assertEqual(lock["license"], "MIT")
        self.assertEqual(lock["license_file"], "LICENSE")
        self.assertEqual(lock["license_git_blob"], "e7dca554bcb802f98408383a864404e3aa4eacca")
        inventory = json.loads((ROOT / "third_party/inventory.json").read_text(encoding="utf-8"))
        llama = next(entry for entry in inventory["entries"] if entry["id"] == "llama-cpp")
        self.assertEqual(llama["source_revision"], PINNED_COMMIT)
        self.assertEqual(llama["license"], "MIT")
        self.assertNotEqual(llama["status"], "planned")

    def test_public_abi2_adds_explicit_constraints_without_sync_generation(self) -> None:
        header = (ROOT / "native/llama_runtime/include/reachy_llama.h").read_text(encoding="utf-8")
        required = (
            "REACHY_LLAMA_ABI_VERSION 2u",
            "REACHY_LLAMA_STATUS_INVALID_CONSTRAINT = 15",
            "REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED = 16",
            "REACHY_LLAMA_CONSTRAINT_GBNF = 1",
            "reachy_llama_generation_constraint",
            "reachy_llama_generation_start(",
            "reachy_llama_generation_start_constrained(",
            "reachy_llama_generation_poll(",
            "reachy_llama_generation_cancel(",
            "reachy_llama_generation_release(",
        )
        for contract in required:
            self.assertIn(contract, header)
        self.assertNotIn("reachy_llama_generate(", header)
        self.assertNotIn("reachy_llama_generation_wait(", header)

    def test_abi1_implementation_is_preserved_as_historical_source(self) -> None:
        base = (ROOT / "native/llama_runtime/src/reachy_llama_abi1_base.inc").read_text(encoding="utf-8")
        self.assertIn('constexpr const char * kVersion = "rma130-abi1"', base)
        self.assertIn("RMA-130 does not queue requests", base)
        self.assertIn("release never blocks waiting for inference", base)
        self.assertIn("abort_callback = AbortDecode", base)
        source = (ROOT / "native/llama_runtime/src/reachy_llama.cpp").read_text(encoding="utf-8")
        self.assertIn('#include "reachy_llama_abi1_base.inc"', source)
        self.assertIn("rma133-abi2-constrained", source)

    def test_constrained_runtime_fails_closed_without_provider_or_unconstrained_fallback(self) -> None:
        source = (ROOT / "native/llama_runtime/src/reachy_llama.cpp").read_text(encoding="utf-8")
        lowered = source.lower()
        for prohibited in ("http://", "https://", "curl_", "socket(", "fallback provider", "cloud", "download", ".detach()"):
            self.assertNotIn(prohibited, lowered)
        self.assertIn("llama_sampler_init_grammar", source)
        self.assertIn("REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED", source)
        self.assertIn("unconstrained generation was not attempted", source)
        self.assertIn("payload.grammar.assign", source)
        self.assertIn("payload.root.assign", source)
        self.assertNotIn("strip_markdown", lowered)
        self.assertNotIn("repair_json", lowered)

    def test_third_party_build_isolated_from_first_party_warning_policy(self) -> None:
        cmake = (ROOT / "native/llama_runtime/CMakeLists.txt").read_text(encoding="utf-8")
        for setting in (
            "set(BUILD_SHARED_LIBS OFF", "set(LLAMA_ALL_WARNINGS OFF", "set(LLAMA_FATAL_WARNINGS OFF",
            "set(LLAMA_OPENSSL OFF", "set(LLAMA_SUBPROCESS OFF", "set(GGML_NATIVE OFF",
            "set(GGML_OPENMP OFF", "set(GGML_LLAMAFILE OFF", 'set(GGML_CPU_ARM_ARCH "armv8-a"',
        ):
            self.assertIn(setting, cmake)
        self.assertIn("reachy_enable_strict_warnings(reachy_llama)", cmake)
        self.assertIn("reachy_llama_constraint_contract_tests", cmake)
        self.assertIn("EXCLUDE_FROM_ALL\n    SYSTEM", cmake)

    def test_android_build_fails_closed_and_exports_only_wrapper(self) -> None:
        build_script = (ROOT / "scripts/build_llama_android.sh").read_text(encoding="utf-8")
        self.assertIn("ANDROID_STL=c++_static", build_script)
        self.assertIn("native_feasibility_min_sdk", build_script)
        self.assertIn("verify_source_checkout.py", build_script)
        self.assertIn("license blob mismatch", build_script)
        self.assertIn("prohibited dynamic dependency", build_script)
        self.assertIn("leaked symbols outside the first-party ABI", build_script)
        self.assertIn("CPU baseline: armv8-a", build_script)
        self.assertIn("Model bundled: no", build_script)

    def test_unity_staging_includes_runtime_without_model_payload(self) -> None:
        stage = (ROOT / "scripts/stage_reachy_unity_android_runtime.sh").read_text(encoding="utf-8")
        self.assertIn("third_party/llama-cpp-source.lock.json", stage)
        self.assertIn("build_llama_android.sh", stage)
        self.assertIn("libreachy_llama.so", stage)
        self.assertNotIn(".gguf", stage.lower())

    def test_stress_contract_keeps_bounded_queue_and_cancellation(self) -> None:
        tests = (ROOT / "native/llama_runtime/tests/reachy_llama_contract_tests.cpp").read_text(encoding="utf-8")
        self.assertIn("TestBoundedQueueOrderingAndAllocationStress", tests)
        self.assertIn("TestCancellationUnblocksBackpressureWithoutSilentDrain", tests)
        self.assertIn("queue.Cancel()", tests)


if __name__ == "__main__":
    unittest.main()
