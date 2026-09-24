#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
SPEC = ROOT / "docs" / "RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"
PROGRAM = ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.cs"
WORKER_TESTS = ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.Rma032WorkerTests.cs"
FAULT_TESTS = ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.Rma032WarningAndFaultTests.cs"
REQUEST_HANDLERS = ROOT / "Assets" / "ReachyMini" / "Runtime" / "Simulation" / "ReachySimulationWorker.RequestHandlers.cs"
CONTROL_REQUESTS = ROOT / "Assets" / "ReachyMini" / "Runtime" / "Simulation" / "ReachySimulationWorker.ControlRequests.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190SolverFaultResetContracts(unittest.TestCase):
    def test_authoritative_rma190_row_stays_open_until_exact_evidence(self) -> None:
        todo = read(TODO)
        spec = read(SPEC)
        item = "Solver fault and controlled reset."
        self.assertIn(f"- [ ] {item}", todo)
        self.assertIn("Solver fault and controlled reset", spec)
        self.assertIn("Native-backed worker + application/runtime recovery", spec)

    def test_controlled_reset_discards_commands_and_republishes_authoritative_zero_state(self) -> None:
        worker = read(WORKER_TESTS)
        for token in (
            "worker.Reset(",
            "ReachySimResetPose.SleepRest",
            "reset.DiscardedCommandCount",
            "snapshot.State.Sequence == 0UL",
            "snapshot.State.SimulationTime == 0.0",
            "resetSnapshot.Timing.DiscardedCommandCount",
            "sleeping health flag publication",
        ):
            self.assertIn(token, worker)

    def test_native_fault_is_retained_and_worker_stays_faulted(self) -> None:
        fault_tests = read(FAULT_TESTS)
        for token in (
            "TestAuthoritativeSimulationWorkerFaultRetention",
            "WaitForWorkerFault",
            "stale command fault retention",
            "retainedFault.Operation",
            "ReachySimErrorCode.CommandFormatError",
            "ReachySimulationRunState.Faulted",
            "worker remains faulted after native rejection",
        ):
            self.assertIn(token, fault_tests)
        program = read(PROGRAM)
        self.assertIn("TestAuthoritativeSimulationWorkerFaultRetention();", program)

    def test_runtime_enters_faulted_state_on_failed_native_reset(self) -> None:
        handlers = read(REQUEST_HANDLERS)
        controls = read(CONTROL_REQUESTS)
        for token in (
            "private void ProcessResetRequest",
            "session.ResetRaw(request.ResetId)",
            "EnterNativeFault(\"reset\", error)",
            "ReachySimulationRunState.Faulted",
            "commandQueue.Clear()",
            "PublishCurrentState(stepDurationSeconds: 0.0)",
        ):
            self.assertIn(token, handlers)
        self.assertIn("CompleteNonShutdownRequestAfterFault", controls)
        self.assertIn("ControlRequestKind.Reset", controls)


if __name__ == "__main__":
    unittest.main()
