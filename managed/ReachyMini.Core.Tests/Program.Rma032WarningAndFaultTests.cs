using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static void TestSimulationWorkerWarningAccounting()
        {
            const uint sleepingHealthFlag = 1U << 0;
            const uint warningHealthFlag = 1U << 1;

            ulong warningCount =
                ReachySimulationWorker.CountNewSolverWarningEpisodes(
                    currentCount: 0UL,
                    previousHealthFlags: 0U,
                    currentHealthFlags: sleepingHealthFlag);
            AssertEqual(
                0UL,
                warningCount,
                "sleeping health does not increment solver warnings");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                sleepingHealthFlag,
                sleepingHealthFlag | warningHealthFlag);
            AssertEqual(1UL, warningCount, "warning rising edge");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                sleepingHealthFlag | warningHealthFlag,
                warningHealthFlag);
            AssertEqual(1UL, warningCount, "persistent warning is not recounted");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                warningHealthFlag,
                currentHealthFlags: 0U);
            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                previousHealthFlags: 0U,
                currentHealthFlags: warningHealthFlag);
            AssertEqual(2UL, warningCount, "second warning episode");
        }

        private static void TestAuthoritativeSimulationWorkerDeadlineMetrics()
        {
            ReachySimulationWorker? worker = null;
            ReachySimSession? unownedSession = null;
            Rma032NativeTestControls.ResetControls();
            try
            {
                byte[] modelBytes = Encoding.UTF8.GetBytes(
                    "simulation-worker-deadline-model");
                ReachySimCreateResult createResult =
                    ReachySimSession.Create(modelBytes);
                unownedSession = createResult.Session ??
                    throw new InvalidOperationException(
                        $"Deadline worker native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

                worker = new ReachySimulationWorker(unownedSession);
                unownedSession = null;
                Rma032NativeTestControls.SetStepBlocked(blocked: true);

                ReachySimulationControlResult start = worker.Start(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(start, "deadline worker start");
                WaitForNativeStepEntry(TimeSpan.FromSeconds(5.0));
                Thread.Sleep(40);
                Rma032NativeTestControls.SetStepBlocked(blocked: false);

                ReachyPublishedSimulationSnapshot timingSnapshot = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.Timing.DeadlineMissCount >= 1UL &&
                        snapshot.Timing.MaximumStepDurationSeconds >= 0.02,
                    TimeSpan.FromSeconds(5.0),
                    "over-budget step timing publication");
                AssertTrajectoryInvariant(
                    timingSnapshot,
                    "deadline-miss authoritative trajectory");
                if (timingSnapshot.Timing.LastStepDurationSeconds < 0.0 ||
                    timingSnapshot.Timing.MaximumStepDurationSeconds <
                        timingSnapshot.Timing.LastStepDurationSeconds)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: step-duration diagnostics are inconsistent.");
                }

                ReachySimulationControlResult shutdown = worker.Shutdown(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(shutdown, "deadline worker shutdown");
            }
            finally
            {
                Rma032NativeTestControls.SetStepBlocked(blocked: false);
                Rma032NativeTestControls.ResetControls();
                if (worker != null)
                {
                    worker.Dispose();
                }
                else
                {
                    unownedSession?.Dispose();
                }
            }
        }

        private static void TestAuthoritativeSimulationWorkerFaultRetention()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes(
                "simulation-worker-fault-model");
            ReachySimCreateResult createResult =
                ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Fault worker native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

            using (ReachySimulationWorker worker =
                new ReachySimulationWorker(
                    session,
                    commandQueueCapacity: 2,
                    maximumCommandBytes: 64))
            {
                ReachySimulationControlResult start = worker.Start(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(start, "fault worker start");

                ReachySimulationControlResult pause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(pause, "fault worker initial pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(CreateCommandBatch(sequence: 1UL)),
                    "command queued while paused");
                Thread.Sleep(50);
                AssertEqual(
                    ReachySimulationRunState.Paused,
                    worker.State,
                    "queued command does not advance paused worker");
                AssertEqual<ReachySimulationFault?>(
                    null,
                    worker.Fault,
                    "queued command is not applied outside a step boundary");

                ReachySimulationControlResult resume = worker.Resume(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(resume, "fault worker first resume");
                ReachyPublishedSimulationSnapshot applied = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= 1UL,
                    TimeSpan.FromSeconds(5.0),
                    "first command boundary application");
                AssertTrajectoryInvariant(
                    applied,
                    "post-command authoritative trajectory");

                ReachySimulationControlResult secondPause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(secondPause, "fault worker second pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(CreateCommandBatch(sequence: 1UL)),
                    "stale command queued for boundary failure");

                ReachySimulationControlResult secondResume = worker.Resume(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(secondResume, "fault worker second resume");
                ReachySimulationFault retainedFault = WaitForWorkerFault(
                    worker,
                    TimeSpan.FromSeconds(5.0),
                    "stale command fault retention");
                AssertEqual(
                    "submit commands",
                    retainedFault.Operation,
                    "retained command fault operation");
                AssertEqual(
                    ReachySimErrorCode.CommandFormatError,
                    retainedFault.Error.Code,
                    "retained command fault code");
                AssertEqual(
                    ReachySimulationRunState.Faulted,
                    worker.State,
                    "worker remains faulted after native rejection");

                ReachySimulationControlResult shutdown = worker.Shutdown(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(shutdown, "fault worker shutdown");
            }
        }

        private static ReachySimulationFault WaitForWorkerFault(
            ReachySimulationWorker worker,
            TimeSpan timeout,
            string description)
        {
            long deadline = Stopwatch.GetTimestamp() + checked(
                (long)Math.Ceiling(
                    timeout.TotalSeconds * Stopwatch.Frequency));
            while (Stopwatch.GetTimestamp() < deadline)
            {
                ReachySimulationFault? fault = worker.Fault;
                if (fault != null)
                {
                    return fault;
                }
                Thread.Sleep(1);
            }

            throw new InvalidOperationException(
                $"Managed test timed out waiting for {description}.");
        }

        private static void WaitForNativeStepEntry(TimeSpan timeout)
        {
            long deadline = Stopwatch.GetTimestamp() + checked(
                (long)Math.Ceiling(
                    timeout.TotalSeconds * Stopwatch.Frequency));
            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (Rma032NativeTestControls.StepEntered())
                {
                    return;
                }
                Thread.Sleep(1);
            }

            throw new InvalidOperationException(
                "Managed test timed out waiting for the controlled native step.");
        }
    }
}
