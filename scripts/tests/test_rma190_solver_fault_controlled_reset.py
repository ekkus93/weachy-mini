#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NATIVE = ROOT / "native" / "reachy_sim" / "tests" / "reachy_sim_fake_backend.c"
HEADER = ROOT / "native" / "reachy_sim" / "tests" / "reachy_sim_blocking_backend.h"
CONTROL = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma032NativeTestControls.cs"
TEST = (
    ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.Rma190SolverFaultControlledResetTests.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190SolverFaultControlledResetContracts(unittest.TestCase):
    def test_native_fault_injection_is_one_shot_numeric_failure(self) -> None:
        native = read(NATIVE)
        header = read(HEADER)
        self.assertIn("reachy_sim_blocking_backend_fail_next_step_numeric", header)
        self.assertIn("g_test_fail_next_step_numeric", native)
        self.assertIn("atomic_exchange_explicit", native)
        self.assertIn("REACHY_SIM_STATUS_NUMERIC_FAILURE", native)
        self.assertIn("controlled RMA-190 numeric solver failure", native)

    def test_managed_acceptance_requires_recreate_then_explicit_neutral_reset(self) -> None:
        control = read(CONTROL)
        test = read(TEST)
        self.assertIn("FailNextStepNumericNative", control)
        self.assertIn('"REACHY_MANAGED_NATIVE_TESTS"', test)
        self.assertIn("ReachySimErrorCode.NumericFailure", test)
        self.assertIn("ReachySimRecoverability.RecreateHandle", test)
        self.assertIn("ReachySimulationRunState.Faulted", test)
        self.assertIn("hiddenReset.IsSuccess", test)
        self.assertGreaterEqual(test.count("ReachySimSession.Create(modelBytes)"), 2)
        self.assertIn("ReachySimResetPose.NeutralAwake", test)
        self.assertIn("post-reset authoritative progress", test)
        self.assertIn("ReachySimulationRunState.Running", test)


if __name__ == "__main__":
    unittest.main()
