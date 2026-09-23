#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Text;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        [ModuleInitializer]
        internal static void RunRma190SolverFaultControlledResetModule()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            TestRma190SolverFaultControlledReset();
        }

        private static void TestRma190SolverFaultControlledReset()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes(
                "rma190-solver-fault-controlled-reset-model");
            Rma032NativeTestControls.ResetControls();

            try
            {
                RunRma190FaultedWorker(modelBytes);
                Rma032NativeTestControls.ResetControls();
                RunRma190RecoveredWorker(modelBytes);
            }
            finally
            {
                Rma032NativeTestControls.ResetControls();
            }
        }

        private static void RunRma190FaultedWorker(byte[] modelBytes)
        {
            ReachySimCreateResult createResult = ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 solver-fault native create failed: " +
                    $"{createResult.Error.Code}: {createResult.Error.Message}");

            using var worker = new ReachySimulationWorker(
                session,
                commandQueueCapacity: 2,
                maximumCommandBytes: 64);
            ReachySimulationControlResult start = worker.Start(
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(start, "RMA-190 solver-fault worker start");
            _ = WaitForSnapshot(
                worker,
                snapshot => snapshot.State.Sequence >= 1UL,
                TimeSpan.FromSeconds(5.0),
                "RMA-190 pre-fault authoritative progress");

            Rma032NativeTestControls.FailNextStepNumeric();
            ReachySimulationFault fault = WaitForWorkerFault(
                worker,
                TimeSpan.FromSeconds(5.0),
                "RMA-190 controlled numeric solver fault");
            AssertEqual("step", fault.Operation, "RMA-190 solver-fault operation");
            AssertEqual(
                ReachySimErrorCode.NumericFailure,
                fault.Error.Code,
                "RMA-190 solver-fault error code");
            AssertEqual(
                ReachySimRecoverability.RecreateHandle,
                fault.Error.Recoverability,
                "RMA-190 solver-fault recoverability");
            AssertEqual(
                ReachySimulationRunState.Faulted,
                worker.State,
                "RMA-190 worker remains faulted");

            ReachySimulationControlResult hiddenReset = worker.Reset(
                ReachySimResetPose.NeutralAwake,
                TimeSpan.FromSeconds(2.0));
            if (hiddenReset.IsSuccess)
            {
                throw new InvalidOperationException(
                    "RMA-190 faulted worker silently accepted an in-place reset instead of requiring controlled handle recreation.");
            }
            AssertEqual(
                ReachySimulationRunState.Faulted,
                hiddenReset.State,
                "RMA-190 reset remains fail-closed while faulted");

            ReachySimulationControlResult shutdown = worker.Shutdown(
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(shutdown, "RMA-190 faulted-worker shutdown");
        }

        private static void RunRma190RecoveredWorker(byte[] modelBytes)
        {
            ReachySimCreateResult createResult = ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 recovery native create failed: " +
                    $"{createResult.Error.Code}: {createResult.Error.Message}");

            using var worker = new ReachySimulationWorker(
                session,
                commandQueueCapacity: 2,
                maximumCommandBytes: 64);
            ReachySimulationControlResult start = worker.Start(
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(start, "RMA-190 recreated worker start");

            ReachySimulationControlResult reset = worker.Reset(
                ReachySimResetPose.NeutralAwake,
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(reset, "RMA-190 explicit neutral reset after recreation");

            ReachyPublishedSimulationSnapshot recovered = WaitForSnapshot(
                worker,
                snapshot => snapshot.State.Sequence >= 2UL,
                TimeSpan.FromSeconds(5.0),
                "RMA-190 post-reset authoritative progress");
            if (double.IsNaN(recovered.State.SimulationTime) ||
                double.IsInfinity(recovered.State.SimulationTime) ||
                recovered.State.SimulationTime <= 0.0)
            {
                throw new InvalidOperationException(
                    "RMA-190 recreated worker did not resume finite authoritative simulation after neutral reset.");
            }
            AssertEqual<ReachySimulationFault?>(
                null,
                worker.Fault,
                "RMA-190 recreated worker has no retained solver fault");
            AssertEqual(
                ReachySimulationRunState.Running,
                worker.State,
                "RMA-190 recreated worker is running after controlled reset");

            ReachySimulationControlResult shutdown = worker.Shutdown(
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(shutdown, "RMA-190 recovered-worker shutdown");
        }
    }
}
