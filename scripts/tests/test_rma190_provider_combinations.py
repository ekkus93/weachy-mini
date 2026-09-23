#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = (
    ROOT
    / "managed"
    / "ReachyMini.Core.Tests"
    / "Rma190IndependentProviderCombinationTests.cs"
)
PROGRAM = ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ProviderCombinationContracts(unittest.TestCase):
    def test_managed_acceptance_exhausts_all_local_cloud_combinations(self) -> None:
        source = read(TEST)
        self.assertIn("const int combinationCount = 1 << 4;", source)
        self.assertIn("for (int mask = 0; mask < combinationCount; ++mask)", source)
        for kind in ("Asr", "Tts", "Llm", "Vlm"):
            self.assertIn(f"ReachyProviderKind.{kind}", source)
            self.assertIn(f"ReachyProviderWorkloadKind.{kind}", source)
        self.assertIn("ReachyProviderExecution.Cloud", source)
        self.assertIn("ReachyProviderExecution.OnDevice", source)
        self.assertIn("ReachyConnectivityRequirement.NetworkRequired", source)
        self.assertIn("ReachyConnectivityRequirement.OfflineCapable", source)
        self.assertIn("actual.SendsDataOffDevice", source)
        self.assertIn("PrivacyCloudSummary", source)

    def test_workload_scoped_durable_references_and_fallbacks_are_covered(self) -> None:
        source = read(TEST)
        for reference in (
            "AsrProviderProfileId",
            "TtsProviderProfileId",
            "LlmProviderProfileId",
            "VlmProviderProfileId",
        ):
            self.assertIn(reference, source)
        self.assertIn("ReachyProviderFallbackPolicyEngine", source)
        self.assertIn("AllowCrossProviderSwitch", source)
        self.assertIn("AllowNetworkProviderSwitch", source)
        self.assertIn("IsTargetAuthorized", source)
        self.assertIn("CyclingOneProviderNeverMutatesAnotherWorkload", source)

    def test_core_test_entrypoint_runs_acceptance(self) -> None:
        program = read(PROGRAM)
        self.assertIn("Rma190IndependentProviderCombinationTests.RunAll();", program)


if __name__ == "__main__":
    unittest.main()
