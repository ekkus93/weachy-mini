#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
SPEC = ROOT / "docs" / "RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"
PACKAGE_TESTS = ROOT / "managed" / "ReachyMini.LocalModelPackages.Tests" / "Program.cs"
LLM_MAIN = ROOT / "managed" / "ReachyMini.LocalLlm.Tests" / "Program.cs"
LLM_OOM = ROOT / "managed" / "ReachyMini.LocalLlm.Tests" / "Program.OutOfMemoryTests.cs"
RMA132_WORKFLOW = ROOT / ".github" / "workflows" / "rma132-local-model-packages.yml"
RMA134_WORKFLOW = ROOT / ".github" / "workflows" / "rma134-local-llm-provider.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ModelCorruptionOomContracts(unittest.TestCase):
    def test_authoritative_rma190_row_stays_open_until_exact_evidence(self) -> None:
        todo = read(TODO)
        spec = read(SPEC)
        item = "Model corruption/OOM."
        self.assertIn(f"- [ ] {item}", todo)
        self.assertIn("Model corruption/OOM", spec)
        self.assertIn("Local-model package/provider integration", spec)

    def test_package_corruption_paths_fail_closed_without_approved_artifact(self) -> None:
        packages = read(PACKAGE_TESTS)
        for test_name in (
            "TestWrongHashAsync",
            "TestTamperAsync",
            "TestRecoveryAsync",
            "TestExtraBytesAsync",
            "TestWrongOffsetAsync",
        ):
            self.assertIn(test_name, packages)
        for token in (
            "LocalModelPackageFailure.Sha256Mismatch",
            "LocalModelPackageFailure.SizeMismatch",
            "Require(result.Artifact == null",
            "LocalModelPackageFailure.InstalledArtifactCorrupt",
        ):
            self.assertIn(token, packages)
        self.assertIn("tampered", packages.lower())
        self.assertIn("corrupt model exposed approved path", packages)

    def test_oom_provider_paths_fault_and_require_explicit_recovery(self) -> None:
        main = read(LLM_MAIN)
        oom = read(LLM_OOM)
        for call in (
            "TestOutOfMemoryBeforeNativeHandleAsync",
            "TestOutOfMemoryAfterNativeStartAsync",
            "TestOutOfMemoryCleanupFailureFaultsAsync",
            "TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync",
        ):
            self.assertIn(f"await {call}().ConfigureAwait(false);", main)
            self.assertIn(f"private static async Task {call}()", oom)
        for token in (
            "LocalLlmGenerationStatus.ResourceExhausted",
            "LocalLlmProviderState.Faulted",
            "LocalLlmReloadStatus.Reloaded",
            "runtime.StartCount == 2",
            "runtime.CancelCount == 1",
            "runtime.ReleaseCount == 2",
        ):
            self.assertIn(token, oom)

    def test_ci_runs_package_and_provider_projects_for_this_scenario(self) -> None:
        rma132 = read(RMA132_WORKFLOW)
        rma134 = read(RMA134_WORKFLOW)
        self.assertIn("managed/ReachyMini.LocalModelPackages.Tests/ReachyMini.LocalModelPackages.Tests.csproj", rma132)
        self.assertIn("managed/ReachyMini.LocalModelPackages.Tests/Program.cs", rma132)
        self.assertIn("managed/ReachyMini.LocalLlm.Tests/ReachyMini.LocalLlm.Tests.csproj", rma134)
        self.assertIn("managed/ReachyMini.LocalLlm.Tests/Program.OutOfMemoryTests.cs", rma134)
        self.assertIn("managed/ReachyMini.LocalLlm.Tests/Fakes.cs", rma134)


if __name__ == "__main__":
    unittest.main()
