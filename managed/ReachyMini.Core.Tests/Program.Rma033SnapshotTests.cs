using System;
using System.Text;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
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
    }
}
