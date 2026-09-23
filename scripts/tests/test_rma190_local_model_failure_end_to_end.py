#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma190LocalModelFailureEndToEndTests.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190LocalModelFailureEndToEndContracts(unittest.TestCase):
    def test_corruption_crosses_real_package_manager_boundary(self) -> None:
        source = read(TEST)
        self.assertIn("LocalModelPackageManager", source)
        self.assertIn("manager.ImportAsync", source)
        self.assertIn("File.WriteAllText(artifact.FullPath, \"tampered\"", source)
        self.assertIn("manager.ResolveInstalledAsync", source)
        self.assertIn("LocalModelPackageFailure.InstalledArtifactCorrupt", source)
        self.assertIn("resolved.Artifact == null", source)

    def test_oom_crosses_real_provider_boundary_and_requires_explicit_reload(self) -> None:
        source = read(TEST)
        self.assertIn("LocalLlmProvider.CreateForTestingAsync", source)
        self.assertIn("throw new OutOfMemoryException", source)
        self.assertIn("LocalLlmGenerationStatus.ResourceExhausted", source)
        self.assertIn("LocalLlmProviderState.Faulted", source)
        self.assertIn("provider.ReloadAsync", source)
        self.assertIn("LocalLlmReloadStatus.Reloaded", source)
        self.assertIn("reload did not replay failed generation", source)


if __name__ == "__main__":
    unittest.main()
