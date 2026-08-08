from __future__ import annotations

import hashlib
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LANGUAGE_DIR = ROOT / "Assets/ReachyMini/Runtime/Core/Language"
INTEROP_DIR = ROOT / "Assets/ReachyMini/Runtime/Interop"
PROMPT_RESOURCE = ROOT / "Assets/ReachyMini/Runtime/Resources/LocalLlm/Rma133SystemPromptV4.txt"
GRAMMAR_RESOURCE = (
    ROOT / "Assets/ReachyMini/Runtime/Resources/LocalLlm/Rma133BehaviorOutputV1.gbnf.txt"
)
V6_CONFIG = ROOT / "benchmarks/rma133/candidates-v6.json"
SELECTED_MANIFEST = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Rma134LocalLlmProviderContracts(unittest.TestCase):
    def test_product_resources_are_frozen_v6_bytes(self) -> None:
        config = json.loads(V6_CONFIG.read_text(encoding="utf-8"))
        prompt = config["system_prompt_contract"]
        constraint = config["constrained_generation_contract"]

        self.assertEqual(sha256(PROMPT_RESOURCE), prompt["sha256"])
        self.assertEqual(sha256(GRAMMAR_RESOURCE), constraint["grammar_sha256"])
        self.assertEqual(PROMPT_RESOURCE.read_bytes(), (ROOT / prompt["path"]).read_bytes())
        self.assertEqual(
            GRAMMAR_RESOURCE.read_bytes(),
            (ROOT / constraint["grammar_path"]).read_bytes(),
        )

    def test_v6_profile_is_preserved_and_product_profile_is_conservative(self) -> None:
        source = (LANGUAGE_DIR / "ReachyLocalLlmRuntimeContracts.cs").read_text(
            encoding="utf-8"
        )
        selected = (LANGUAGE_DIR / "Rma133SelectedLocalLlmProfile.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("CreateRma133SelectedProfile", source)
        self.assertIn("threads: 4", source)
        self.assertIn("batchThreads: 4", source)
        self.assertIn("CreateInitialProductCoexistenceProfile", source)
        self.assertIn("threads: 2", source)
        self.assertIn("batchThreads: 2", source)
        self.assertIn(
            "LocalLlmExecutionProfile.CreateInitialProductCoexistenceProfile()",
            selected,
        )
        self.assertIn("RecommendedThreads = 4", selected)

    def test_product_runtime_exposes_only_constrained_generation(self) -> None:
        native = (INTEROP_DIR / "NativeReachyLlama.cs").read_text(encoding="utf-8")
        runtime = (INTEROP_DIR / "ReachyLlamaNativeRuntime.cs").read_text(encoding="utf-8")
        contracts = (LANGUAGE_DIR / "ReachyLocalLlmRuntimeContracts.cs").read_text(
            encoding="utf-8"
        )
        provider = (LANGUAGE_DIR / "ReachyLocalLlmProvider.cs").read_text(
            encoding="utf-8"
        )
        combined = "\n".join((native, runtime, contracts, provider))

        self.assertIn("reachy_llama_generation_start_constrained", combined)
        self.assertIn("StartConstrained", combined)
        self.assertNotIn('EntryPoint = "reachy_llama_generation_start"', combined)
        self.assertNotIn("StartUnconstrained", combined)

    def test_provider_has_no_network_or_alternate_provider_path(self) -> None:
        product = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(LANGUAGE_DIR.glob("ReachyLocalLlm*.cs"))
        )
        forbidden = (
            "HttpClient",
            "HttpRequestMessage",
            "WebRequest",
            "OpenAI",
            "Anthropic",
            "fallbackProvider",
            "alternateModel",
            "AutomaticRetry",
            "Task.Delay",
        )
        for token in forbidden:
            self.assertNotIn(token, product)
        self.assertIn("RequiresNetwork = false", product)
        self.assertIn("RequiresNetworkAccess", product)

    def test_selected_manifest_is_local_abi2_qwen3(self) -> None:
        manifest = json.loads(SELECTED_MANIFEST.read_text(encoding="utf-8"))
        self.assertEqual(manifest["identity"]["model_id"], "qwen3-0.6b")
        self.assertEqual(manifest["runtime"]["runtime_id"], "reachy_llama")
        self.assertEqual(manifest["runtime"]["abi_version"], 2)
        self.assertFalse(manifest["runtime"]["requires_network_access"])
        self.assertEqual(
            manifest["artifact"]["sha256"],
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e",
        )

    def test_transaction_and_failure_contracts_are_present(self) -> None:
        provider = (LANGUAGE_DIR / "ReachyLocalLlmProvider.cs").read_text(
            encoding="utf-8"
        )
        required = (
            "LocalLlmBehaviorIntentParser.Parse",
            "CommitValidatedTurn",
            "ContextLimit",
            "TimedOut",
            "ProviderCancellation",
            "ReloadAsync",
            "The bounded conversation history is full; reset is required",
            "requests are not queued",
            "StartConstrained",
            "IsTrackedEntityAllowed",
            "current allowlist",
        )
        for token in required:
            self.assertIn(token, provider)

    def test_android_acceptance_uses_statfs_without_driveinfo_fallback(self) -> None:
        acceptance = (
            ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma134LocalLlmAcceptance.cs"
        ).read_text(encoding="utf-8")
        storage = (INTEROP_DIR / "AndroidStatFsLocalModelStorageProbe.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("new AndroidStatFsLocalModelStorageProbe()", acceptance)
        self.assertNotIn("new DriveInfoLocalModelStorageProbe()", acceptance)
        self.assertIn("android.os.StatFs", storage)
        self.assertIn('Call<long>("getAvailableBytes")', storage)
        self.assertIn("PlatformNotSupportedException", storage)
        self.assertNotIn("DriveInfo", storage)
        self.assertNotIn("catch", storage)


if __name__ == "__main__":
    unittest.main()
