#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets" / "ReachyMini" / "Runtime" / "Rendering" / "ReachyProductionAuthoritativeRuntime.cs"

def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")

class Rma190SolverRecoveryContracts(unittest.TestCase):
    def test_fault_recovery_is_explicit_and_fail_closed(self) -> None:
        source = read(RUNTIME)
        start = source.index("public ReachySimulationControlResult RecoverFromFault()")
        end = source.index("public bool TryGetLatestAuthoritativePair(", start)
        method = source[start:end]
        self.assertIn("Status != ReachyProductionRuntimeStatus.Faulted", method)
        self.assertIn("nextCommandSequence = 1UL;", method)
        self.assertIn("Fault = string.Empty;", method)
        self.assertIn("StartProductionRuntime();", method)
        self.assertIn("Status == ReachyProductionRuntimeStatus.Running", method)
        self.assertIn("ReachySimRecoverability.RecreateHandle", method)

    def test_fault_path_tears_down_before_recovery(self) -> None:
        source = read(RUNTIME)
        start = source.index("private void EnterFault(string message)")
        end = source.index("private void ShutdownRuntime(", start)
        fault_path = source[start:end]
        self.assertIn("ShutdownRuntime(logFailures: true, preserveFault: true);", fault_path)
        self.assertIn("renderer.enabled = false;", fault_path)

    def test_startup_rebinds_renderer_after_recovery(self) -> None:
        source = read(RUNTIME)
        start = source.index("private void StartProductionRuntime()")
        end = source.index("private void EnterFault(", start)
        startup = source[start:end]
        self.assertIn("renderer.BindPoseSource(boundPoseSource);", startup)
        self.assertIn("Status = ReachyProductionRuntimeStatus.Running;", startup)

if __name__ == "__main__":
    unittest.main()
