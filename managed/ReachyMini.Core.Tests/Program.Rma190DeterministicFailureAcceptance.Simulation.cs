#nullable enable

using System;
using System.Text;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static void TestRma190SolverFaultAndControlledResetScenario()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes("rma190-solver-fault-model");
            ReachySimCreateResult createResult = ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 solver-fault session creation failed: {createResult.Error.Code}: {createResult.Error.Message}");

            using (var worker = new ReachySimulationWorker(
                session,
                commandQueueCapacity: 2,
                maximumCommandBytes: 64))
            {
                AssertControlSuccess(
                    worker.Start(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker start");
                AssertControlSuccess(
                    worker.Pause(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(CreateCommandBatch(sequence: 1UL)),
                    "RMA-190 valid command enqueue");
                AssertControlSuccess(
                    worker.Resume(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 first resume");
                _ = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= 1UL,
                    TimeSpan.FromSeconds(5.0),
                    "RMA-190 first command application");

                AssertControlSuccess(
                    worker.Pause(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 second pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(CreateCommandBatch(sequence: 1UL)),
                    "RMA-190 stale command enqueue");
                AssertControlSuccess(
                    worker.Resume(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 second resume");
                ReachySimulationFault fault = WaitForWorkerFault(
                    worker,
                    TimeSpan.FromSeconds(5.0),
                    "RMA-190 intentional solver-boundary fault");
                AssertEqual(
                    ReachySimErrorCode.CommandFormatError,
                    fault.Error.Code,
                    "RMA-190 typed solver-boundary fault");
                AssertEqual(
                    ReachySimulationRunState.Faulted,
                    worker.State,
                    "RMA-190 worker remains faulted until explicit recovery");
                AssertControlSuccess(
                    worker.Shutdown(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker shutdown");
            }

            ReachySimCreateResult recoveryCreate = ReachySimSession.Create(modelBytes);
            ReachySimSession recoverySession = recoveryCreate.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 recovery session creation failed: {recoveryCreate.Error.Code}: {recoveryCreate.Error.Message}");
            using var recoveryWorker = new ReachySimulationWorker(
                recoverySession,
                commandQueueCapacity: 2,
                maximumCommandBytes: 64);
            AssertControlSuccess(
                recoveryWorker.Start(TimeSpan.FromSeconds(5.0)),
                "RMA-190 recovery worker start");
            ReachySimulationControlResult reset = recoveryWorker.Reset(
                ReachySimResetPose.NeutralAwake,
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(reset, "RMA-190 controlled neutral reset");
            ReachyPublishedSimulationSnapshot resetSnapshot = WaitForSnapshot(
                recoveryWorker,
                snapshot => snapshot.State.Sequence == 0UL &&
                    snapshot.State.SimulationTime == 0.0,
                TimeSpan.FromSeconds(5.0),
                "RMA-190 controlled-reset snapshot");
            AssertEqual(
                0U,
                resetSnapshot.State.HealthFlags,
                "RMA-190 neutral reset clears sleeping/fault health flags");
            AssertEqual<ReachySimulationFault?>(
                null,
                recoveryWorker.Fault,
                "RMA-190 recovered worker has no retained fault");
            AssertControlSuccess(
                recoveryWorker.Shutdown(TimeSpan.FromSeconds(5.0)),
                "RMA-190 recovery worker shutdown");
        }
    }
}
