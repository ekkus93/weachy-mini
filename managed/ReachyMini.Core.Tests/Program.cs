using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static int Main()
        {
            TestProjectMetadata();
            TestNativeLayouts();
            TestSimulationWorkerWarningAccounting();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                TestNativeSessionLifecycle();
                TestAuthoritativeSimulationWorker();
                TestAuthoritativeSimulationWorkerPublishedState();
                TestAuthoritativeSimulationWorkerSnapshots();
                TestAuthoritativeSimulationWorkerDeadlineMetrics();
                TestAuthoritativeSimulationWorkerFaultRetention();
            }

            return 0;
        }

        private static void TestNativeSessionLifecycle()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes("managed-contract-model");

            ReachySimCreateResult createResult =
                ReachySimSession.Create(modelBytes);
            AssertEqual(true, createResult.IsSuccess, "native create result");
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

            ReachySimOperationResult stepResult = session.Step(10U);
            AssertEqual(true, stepResult.IsSuccess, "native step result");

            ReachySimSnapshotCaptureResult captureResult =
                session.CaptureSnapshot();
            AssertEqual(true, captureResult.IsSuccess, "snapshot capture result");
            ReachySimSnapshot snapshot = captureResult.Snapshot ??
                throw new InvalidOperationException(
                    $"Snapshot capture failed: {captureResult.Error.Code}: {captureResult.Error.Message}");
            AssertEqual(
                ProjectMetadata.NativeSnapshotFormatVersion,
                snapshot.SnapshotVersion,
                "snapshot version");
            AssertEqual(
                ProjectMetadata.UncalibratedCalibrationProfileId,
                snapshot.CalibrationProfileId,
                "snapshot calibration profile");
            AssertEqual(10UL, snapshot.Sequence, "snapshot sequence");
            AssertEqual(0.02, snapshot.SimulationTime, "snapshot time");
            if (snapshot.ByteCount <=
                Marshal.SizeOf<NativeReachySimSnapshotHeader>())
            {
                throw new InvalidOperationException(
                    "Managed test failed: snapshot does not contain a backend payload.");
            }

            byte[] exportedSnapshot = snapshot.ToArray();
            exportedSnapshot[0] ^= 0xff;
            AssertEqual(
                ProjectMetadata.NativeAbiVersion,
                (uint)snapshot.ToArray()[0],
                "snapshot export is defensive");

            ReachySimOperationResult advanceResult = session.Step(5U);
            AssertEqual(true, advanceResult.IsSuccess, "advance after snapshot");
            ReachySimOperationResult restoreResult =
                session.RestoreSnapshot(snapshot);
            AssertEqual(true, restoreResult.IsSuccess, "snapshot restore result");

            ReachySimSnapshotCaptureResult recaptureResult =
                session.CaptureSnapshot();
            ReachySimSnapshot recaptured = recaptureResult.Snapshot ??
                throw new InvalidOperationException(
                    $"Snapshot recapture failed: {recaptureResult.Error.Code}: {recaptureResult.Error.Message}");
            AssertEqual(snapshot.Sequence, recaptured.Sequence, "restored sequence");
            AssertEqual(
                snapshot.SimulationTime,
                recaptured.SimulationTime,
                "restored simulation time");
            AssertBytesEqual(
                snapshot.ToArray(),
                recaptured.ToArray(),
                "restored snapshot bytes");

            ReachySimOperationResult zeroStepResult = session.Step(0U);
            AssertEqual(false, zeroStepResult.IsSuccess, "zero-step failure");
            AssertEqual(
                ReachySimErrorCode.InvalidArgument,
                zeroStepResult.Error.Code,
                "zero-step error code");

            ReachySimOperationResult sleepReset =
                session.Reset(ReachySimResetPose.SleepRest);
            AssertEqual(true, sleepReset.IsSuccess, "sleep/rest reset result");
            ReachySimOperationResult neutralReset =
                session.Reset(ReachySimResetPose.NeutralAwake);
            AssertEqual(true, neutralReset.IsSuccess, "neutral-awake reset result");
            ReachySimOperationResult unknownReset = session.Reset(99U);
            AssertEqual(false, unknownReset.IsSuccess, "unknown reset failure");
            AssertEqual(
                ReachySimErrorCode.InvalidArgument,
                unknownReset.Error.Code,
                "unknown reset error code");

            ReachySimOperationResult closeResult = session.Close();
            AssertEqual(true, closeResult.IsSuccess, "native close result");
            AssertThrows<ObjectDisposedException>(
                () => session.Step(1U),
                "operation after close");
            session.Dispose();

            for (int iteration = 0; iteration < 1000; ++iteration)
            {
                ReachySimCreateResult stressCreate =
                    ReachySimSession.Create(modelBytes);
                ReachySimSession? stressSessionCandidate =
                    stressCreate.Session;
                if (!stressCreate.IsSuccess ||
                    stressSessionCandidate == null)
                {
                    throw new InvalidOperationException(
                        $"Lifecycle create {iteration} failed: {stressCreate.Error.Code}: {stressCreate.Error.Message}");
                }

                using (ReachySimSession stressSession =
                    stressSessionCandidate)
                {
                    ReachySimOperationResult stressStep =
                        stressSession.Step(1U);
                    AssertEqual(
                        true,
                        stressStep.IsSuccess,
                        $"lifecycle step {iteration}");
                }
            }
        }

        private static void TestAuthoritativeSimulationWorker()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes("simulation-worker-model");
            ReachySimCreateResult createResult =
                ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Worker native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

            using (ReachySimulationWorker worker =
                new ReachySimulationWorker(
                    session,
                    commandQueueCapacity: 1,
                    maximumCommandBytes: 64))
            {
                ReachySimulationControlResult start = worker.Start(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(start, "worker start");

                ReachyPublishedSimulationSnapshot running = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= 5UL,
                    TimeSpan.FromSeconds(5.0),
                    "independent fixed-step progress");
                AssertEqual(
                    ReachySimulationRunState.Running,
                    worker.State,
                    "worker running state");
                AssertTrajectoryInvariant(
                    running,
                    "initial authoritative worker trajectory");

                ObserveWorkerAtCadence(
                    worker,
                    cadenceMilliseconds: 16,
                    observationCount: 5,
                    "60 Hz reader cadence");
                ObserveWorkerAtCadence(
                    worker,
                    cadenceMilliseconds: 33,
                    observationCount: 4,
                    "30 Hz reader cadence");

                AssertEqual(
                    true,
                    worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot beforeRenderStall),
                    "pre-stall snapshot availability");
                Thread.Sleep(100);
                ReachyPublishedSimulationSnapshot afterRenderStall = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >
                        beforeRenderStall.State.Sequence,
                    TimeSpan.FromSeconds(5.0),
                    "progress across a stalled rendering reader");
                AssertTrajectoryInvariant(
                    afterRenderStall,
                    "post-stall authoritative worker trajectory");

                ReachySimulationControlResult pause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(pause, "worker pause");
                ReachyPublishedSimulationSnapshot paused = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= running.State.Sequence,
                    TimeSpan.FromSeconds(2.0),
                    "paused snapshot");

                Thread.Sleep(250);
                AssertEqual(
                    true,
                    worker.TryGetLatestSnapshot(out ReachyPublishedSimulationSnapshot stillPaused),
                    "paused snapshot availability");
                AssertEqual(
                    paused.State.Sequence,
                    stillPaused.State.Sequence,
                    "paused sequence stability");
                AssertEqual(
                    paused.State.SimulationTime,
                    stillPaused.State.SimulationTime,
                    "paused simulation-time stability");

                AssertEqual(
                    ReachySimulationCommandEnqueueResult.InvalidFormat,
                    worker.EnqueueCommandBatch(new byte[24]),
                    "invalid command visibility");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.CommandTooLarge,
                    worker.EnqueueCommandBatch(new byte[65]),
                    "oversized command visibility");

                byte[] firstCommand = CreateCommandBatch(sequence: 1UL);
                byte[] secondCommand = CreateCommandBatch(sequence: 2UL);
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(firstCommand),
                    "first queued command");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.QueueFull,
                    worker.EnqueueCommandBatch(secondCommand),
                    "visible queue overflow");

                ReachySimulationControlResult resume = worker.Resume(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(resume, "worker resume");
                Thread.Sleep(20);
                ReachyPublishedSimulationSnapshot resumed = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence > paused.State.Sequence,
                    TimeSpan.FromSeconds(5.0),
                    "post-resume progress");
                ulong resumeAdvance =
                    resumed.State.Sequence - paused.State.Sequence;
                if (resumeAdvance > 30UL)
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for resume catch-up suppression: advanced {resumeAdvance} steps after a 250 ms pause.");
                }
                if (resumed.Timing.CommandQueueOverflowCount < 1UL)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: command queue overflow was not published in timing diagnostics.");
                }

                ReachySimulationControlResult secondPause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(secondPause, "second worker pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(secondCommand),
                    "command queued before reset");

                ReachySimulationControlResult reset = worker.Reset(
                    resetId: (uint)ReachySimResetPose.SleepRest,
                    timeout: TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(reset, "worker reset");
                AssertEqual(
                    1,
                    reset.DiscardedCommandCount,
                    "reset discarded-command visibility");

                ReachyPublishedSimulationSnapshot resetSnapshot = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence == 0UL &&
                        snapshot.State.SimulationTime == 0.0,
                    TimeSpan.FromSeconds(5.0),
                    "deterministic reset snapshot");
                if (resetSnapshot.Timing.DiscardedCommandCount < 1UL)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: discarded reset commands were not published in timing diagnostics.");
                }
                AssertEqual(
                    1U,
                    resetSnapshot.State.HealthFlags,
                    "sleeping health flag publication");
                AssertEqual(
                    0UL,
                    resetSnapshot.Timing.SolverWarningCount,
                    "sleeping state is not a solver warning");

                ReachySimulationControlResult shutdown = worker.Shutdown(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(shutdown, "worker shutdown");
                AssertEqual(
                    ReachySimulationRunState.Stopped,
                    worker.State,
                    "worker stopped state");
                AssertEqual<ReachySimulationFault?>(
                    null,
                    worker.Fault,
                    "worker fault state");
            }
        }

        private static void TestAuthoritativeSimulationWorkerPublishedState()
        {
            ReachySimCreateResult createResult = ReachySimSession.Create(
                Encoding.UTF8.GetBytes("worker-published-authoritative-state"));
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Published-state worker create failed: {createResult.Error.Code}: {createResult.Error.Message}");
            using (SyntheticAuthoritativeStateReader reader =
                new SyntheticAuthoritativeStateReader())
            {
            using (ReachySimulationWorker worker =
                new ReachySimulationWorker(session, reader))
            {
                ReachySimAuthoritativeStateFrame destination =
                    worker.CreateAuthoritativeStateFrame();
                AssertEqual(
                    false,
                    worker.TryCaptureLatestAuthoritativeState(destination),
                    "authoritative publication unavailable before worker start");

                AssertControlSuccess(
                    worker.Start(TimeSpan.FromSeconds(5.0)),
                    "published-state worker start");
                long deadline = Stopwatch.GetTimestamp() + checked(
                    (long)Math.Ceiling(5.0 * Stopwatch.Frequency));
                while ((!worker.TryCaptureLatestAuthoritativeState(destination) ||
                        destination.Sequence < 5UL) &&
                    Stopwatch.GetTimestamp() < deadline)
                {
                    Thread.Sleep(1);
                }
                if (destination.Sequence < 5UL)
                {
                    throw new InvalidOperationException(
                        "Managed test timed out waiting for worker-owned authoritative state.");
                }

                AssertEqual(
                    reader.Layout.ModelHash,
                    worker.AuthoritativeStateLayout.ModelHash,
                    "worker authoritative model hash");
                AssertEqual(2, destination.BodyPoseCount, "published body count");
                AssertEqual(
                    1U,
                    destination.GetBodyPose(0).BodyId,
                    "published first body identifier");
                AssertEqual(
                    checked((double)destination.Sequence),
                    destination.GetBodyPose(1).PositionX,
                    "published body value");
                ulong firstSequence = destination.Sequence;
                Thread.Sleep(40);
                if (!worker.TryCaptureLatestAuthoritativeState(destination) ||
                    destination.Sequence <= firstSequence)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: worker-owned authoritative publication did not advance independently of its reader.");
                }
            }

            AssertEqual(true, reader.IsDisposed, "worker owns authoritative reader");
            }
        }

        private static void TestAuthoritativeSimulationWorkerSnapshots()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes(
                "simulation-worker-snapshot-model");
            ReachySimCreateResult createResult =
                ReachySimSession.Create(modelBytes);
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Snapshot worker native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

            using (ReachySimulationWorker worker =
                new ReachySimulationWorker(session))
            {
                ReachySimulationControlResult start = worker.Start(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(start, "snapshot worker start");
                WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= 5UL,
                    TimeSpan.FromSeconds(5.0),
                    "snapshot worker initial progress");

                ReachySimulationControlResult runningCapture =
                    worker.CaptureSnapshot(TimeSpan.FromSeconds(5.0));
                AssertEqual(false, runningCapture.IsSuccess, "running capture rejection");
                AssertEqual(
                    ReachySimErrorCode.InvalidArgument,
                    runningCapture.Error.Code,
                    "running capture error code");

                ReachySimulationControlResult pause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(pause, "snapshot worker pause");
                AssertEqual(
                    true,
                    worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot checkpointState),
                    "checkpoint state availability");

                ReachySimulationControlResult capture = worker.CaptureSnapshot(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(capture, "worker checkpoint capture");
                ReachySimSnapshot checkpoint = capture.CapturedSnapshot ??
                    throw new InvalidOperationException(
                        "Managed test failed: successful worker capture returned no snapshot.");
                AssertEqual(
                    ProjectMetadata.NativeSnapshotFormatVersion,
                    checkpoint.SnapshotVersion,
                    "worker snapshot format version");
                AssertEqual(
                    ProjectMetadata.UncalibratedCalibrationProfileId,
                    checkpoint.CalibrationProfileId,
                    "worker snapshot calibration profile");
                AssertEqual(
                    checkpointState.State.Sequence,
                    checkpoint.Sequence,
                    "worker checkpoint sequence");
                AssertEqual(
                    checkpointState.State.SimulationTime,
                    checkpoint.SimulationTime,
                    "worker checkpoint time");
                if (checkpoint.ModelHash == 0UL)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: worker snapshot model hash is zero.");
                }

                ReachySimCreateResult foreignCreate = ReachySimSession.Create(
                    Encoding.UTF8.GetBytes("foreign-snapshot-model"));
                ReachySimSession foreignSession = foreignCreate.Session ??
                    throw new InvalidOperationException(
                        $"Foreign snapshot create failed: {foreignCreate.Error.Code}: {foreignCreate.Error.Message}");
                using (foreignSession)
                {
                    ReachySimSnapshotCaptureResult foreignCapture =
                        foreignSession.CaptureSnapshot();
                    ReachySimSnapshot foreignSnapshot = foreignCapture.Snapshot ??
                        throw new InvalidOperationException(
                            $"Foreign snapshot capture failed: {foreignCapture.Error.Code}: {foreignCapture.Error.Message}");
                    ReachySimulationControlResult incompatible =
                        worker.RestoreSnapshot(
                            foreignSnapshot,
                            TimeSpan.FromSeconds(5.0));
                    AssertEqual(
                        false,
                        incompatible.IsSuccess,
                        "foreign worker snapshot rejection");
                    AssertEqual(
                        ReachySimErrorCode.SnapshotIncompatible,
                        incompatible.Error.Code,
                        "foreign worker snapshot error code");
                    AssertEqual(
                        ReachySimulationRunState.Paused,
                        worker.State,
                        "worker remains paused after incompatible restore");
                    AssertEqual<ReachySimulationFault?>(
                        null,
                        worker.Fault,
                        "incompatible restore is nonfatal");
                }

                AssertEqual(
                    true,
                    worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot afterIncompatible),
                    "post-incompatible state availability");
                AssertEqual(
                    checkpointState.PublicationSequence,
                    afterIncompatible.PublicationSequence,
                    "incompatible restore does not publish state");
                AssertEqual(
                    checkpointState.State.Sequence,
                    afterIncompatible.State.Sequence,
                    "incompatible restore preserves sequence");
                AssertEqual(
                    checkpointState.State.SimulationTime,
                    afterIncompatible.State.SimulationTime,
                    "incompatible restore preserves time");

                ReachySimulationControlResult resume = worker.Resume(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(resume, "snapshot worker advance resume");
                ReachyPublishedSimulationSnapshot advanced = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence >= checkpoint.Sequence + 5UL,
                    TimeSpan.FromSeconds(5.0),
                    "snapshot worker advanced state");
                ReachySimulationControlResult secondPause = worker.Pause(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(secondPause, "snapshot worker second pause");

                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    worker.EnqueueCommandBatch(CreateCommandBatch(sequence: 1UL)),
                    "queued command before snapshot restore");
                ReachySimulationControlResult restore = worker.RestoreSnapshot(
                    checkpoint,
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(restore, "worker checkpoint restore");
                AssertEqual(
                    1,
                    restore.DiscardedCommandCount,
                    "snapshot restore discarded queued command");
                AssertEqual(
                    ReachySimulationRunState.Paused,
                    restore.State,
                    "snapshot restore remains paused");

                ReachyPublishedSimulationSnapshot restored = WaitForSnapshot(
                    worker,
                    snapshot => snapshot.PublicationSequence >
                            advanced.PublicationSequence &&
                        snapshot.State.Sequence == checkpoint.Sequence &&
                        snapshot.State.SimulationTime == checkpoint.SimulationTime,
                    TimeSpan.FromSeconds(5.0),
                    "worker restored state publication");
                AssertEqual(
                    checkpoint.Sequence,
                    restored.State.Sequence,
                    "restored worker sequence");
                AssertEqual(
                    checkpoint.SimulationTime,
                    restored.State.SimulationTime,
                    "restored worker time");

                ReachySimulationControlResult recapture = worker.CaptureSnapshot(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(recapture, "worker recapture after restore");
                ReachySimSnapshot replayed = recapture.CapturedSnapshot ??
                    throw new InvalidOperationException(
                        "Managed test failed: restored worker recapture returned no snapshot.");
                AssertBytesEqual(
                    checkpoint.ToArray(),
                    replayed.ToArray(),
                    "worker restore snapshot bytes");

                Thread.Sleep(50);
                AssertEqual(
                    true,
                    worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot stillRestored),
                    "restored paused state availability");
                AssertEqual(
                    restored.State.Sequence,
                    stillRestored.State.Sequence,
                    "restored worker remains paused");

                ReachySimulationControlResult finalResume = worker.Resume(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(finalResume, "snapshot worker final resume");
                WaitForSnapshot(
                    worker,
                    snapshot => snapshot.State.Sequence > checkpoint.Sequence,
                    TimeSpan.FromSeconds(5.0),
                    "snapshot worker post-restore progress");
                ReachySimulationControlResult shutdown = worker.Shutdown(
                    TimeSpan.FromSeconds(5.0));
                AssertControlSuccess(shutdown, "snapshot worker shutdown");
            }
        }

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

        private static void ObserveWorkerAtCadence(
            ReachySimulationWorker worker,
            int cadenceMilliseconds,
            int observationCount,
            string description)
        {
            ulong firstSequence = 0UL;
            ulong previousSequence = 0UL;
            ulong previousPublication = 0UL;
            for (int observation = 0; observation < observationCount; ++observation)
            {
                Thread.Sleep(cadenceMilliseconds);
                if (!worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot snapshot))
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: no snapshot was available.");
                }
                AssertTrajectoryInvariant(snapshot, description);
                if (observation == 0)
                {
                    firstSequence = snapshot.State.Sequence;
                }
                else if (snapshot.State.Sequence < previousSequence ||
                    snapshot.PublicationSequence < previousPublication)
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: publication regressed.");
                }
                previousSequence = snapshot.State.Sequence;
                previousPublication = snapshot.PublicationSequence;
            }

            if (previousSequence <= firstSequence)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: simulation did not advance independently of the reader cadence.");
            }
        }

        private sealed class SyntheticAuthoritativeStateReader :
            IReachySimAuthoritativeStateReader
        {
            private ulong nextSequence;

            internal SyntheticAuthoritativeStateReader()
            {
                Layout = new ReachySimAuthoritativeStateLayout(
                    byteCount: 512,
                    modelHash: 0x40a040a040a040a0UL,
                    qposCount: 2,
                    qvelCount: 2,
                    actuatorObservationCount: 1,
                    bodyPoseCount: 2);
            }

            public ReachySimAuthoritativeStateLayout Layout { get; }

            internal bool IsDisposed { get; private set; }

            public ReachySimAuthoritativeStateFrame CreateFrame()
            {
                return new ReachySimAuthoritativeStateFrame(Layout);
            }

            public void Capture(ReachySimAuthoritativeStateFrame frame)
            {
                ulong sequence = nextSequence++;
                frame.Sequence = sequence;
                frame.SimulationTime = sequence *
                    ProjectMetadata.InitialPhysicsTimestepSeconds;
                frame.ContinuityId = 1U;
                frame.JointCount = 3U;
                frame.ContactCount = 0U;
                frame.HealthFlags = 0U;
                frame.CalibrationProfileId =
                    ProjectMetadata.UncalibratedCalibrationProfileId;
                frame.WarningCount = 0UL;
                frame.ConstraintCount = 1U;
                frame.EqualityConstraintCount = 1U;
                frame.MaximumConstraintResidual = 0.0;
                frame.MaximumEqualityConstraintResidual = 0.0;
                frame.QposStorage[0] = sequence;
                frame.QposStorage[1] = -checked((double)sequence);
                frame.QvelStorage[0] = 0.0;
                frame.QvelStorage[1] = 0.0;
                frame.SetActuatorObservation(
                    0,
                    new ReachySimActuatorObservationSnapshot(
                        actuatorId: 0U,
                        controlValue: 0.0,
                        actuatorForce: 0.0,
                        length: 0.0,
                        velocity: 0.0));
                frame.SetBodyPose(
                    0,
                    new ReachySimBodyPoseSnapshot(
                        bodyId: 1U,
                        positionX: 0.0,
                        positionY: 0.0,
                        positionZ: 0.0,
                        quaternionW: 1.0,
                        quaternionX: 0.0,
                        quaternionY: 0.0,
                        quaternionZ: 0.0));
                frame.SetBodyPose(
                    1,
                    new ReachySimBodyPoseSnapshot(
                        bodyId: 2U,
                        positionX: sequence,
                        positionY: 0.0,
                        positionZ: 0.0,
                        quaternionW: 1.0,
                        quaternionX: 0.0,
                        quaternionY: 0.0,
                        quaternionZ: 0.0));
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

    }
}
