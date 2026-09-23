#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = ROOT / "managed" / "ReachyMini.LocalLlm.Tests" / "Program.Rma190ModelFailureEndToEndTests.cs"
PROGRAM = ROOT / "managed" / "ReachyMini.LocalLlm.Tests" / "Program.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ModelFailureContracts(unittest.TestCase):
    def test_corruption_crosses_package_manager_and_never_exposes_approved_artifact(self) -> None:
        source = read(TEST)
        self.assertIn("LocalModelPackageManager", source)
        self.assertIn("manager.ImportAsync", source)
        self.assertIn("manager.ResolveInstalledAsync", source)
        self.assertIn("LocalModelPackageFailure.InstalledArtifactCorrupt", source)
        self.assertIn("resolved.Artifact == null", source)

    def test_oom_crosses_provider_and_requires_explicit_reload(self) -> None:
        source = read(TEST)
        self.assertIn("ThrowOutOfMemoryOnNextPoll = true", source)
        self.assertIn("LocalLlmGenerationStatus.ResourceExhausted", source)
        self.assertIn("LocalLlmProviderState.Faulted", source)
        self.assertIn("provider.ReloadAsync", source)
        self.assertIn("LocalLlmReloadStatus.Reloaded", source)
        self.assertIn("runtime.StartCount == 1", source)
        self.assertIn("runtime.StartCount == 2", source)

    def test_existing_local_llm_managed_entrypoint_runs_rma190_harness(self) -> None:
        program = read(PROGRAM)
        self.assertIn("TestRma190ModelFailureEndToEndAsync()", program)


if __name__ == "__main__":
    unittest.main()
