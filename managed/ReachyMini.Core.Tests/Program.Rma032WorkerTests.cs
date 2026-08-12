using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
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
