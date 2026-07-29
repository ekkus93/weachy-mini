using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            TestProjectMetadata();
            TestNativeLayouts();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                TestNativeSessionLifecycle();
                TestAuthoritativeSimulationWorker();
            }

            return 0;
        }

        private static void TestProjectMetadata()
        {
            AssertEqual(
                SimulationFidelity.Unavailable,
                ProjectMetadata.InitialFidelity,
                "initial fidelity");
            AssertEqual(
                1U,
                ProjectMetadata.NativeSnapshotFormatVersion,
                "snapshot format version");
            AssertEqual(
                0UL,
                ProjectMetadata.UncalibratedCalibrationProfileId,
                "uncalibrated profile identifier");
            AssertEqual(
                true,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.002),
                "500 Hz timestep");
            AssertEqual(
                false,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.0),
                "zero timestep");
            AssertEqual(
                false,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.02),
                "oversized timestep");
        }

        private static void TestNativeLayouts()
        {
            AssertEqual(8, IntPtr.Size, "64-bit managed process");
            AssertEqual(24, Marshal.SizeOf<NativeReachySimConfig>(), "config size");
            AssertEqual(
                40,
                Marshal.SizeOf<NativeReachySimCapabilities>(),
                "capabilities size");
            AssertEqual(
                48,
                Marshal.SizeOf<NativeReachySimStateHeader>(),
                "state header size");
            AssertEqual(
                24,
                Marshal.SizeOf<NativeReachySimCommandBatchHeader>(),
                "command header size");
            AssertEqual(
                96,
                Marshal.SizeOf<NativeReachySimWrenchCommand>(),
                "wrench command size");
            AssertEqual(
                48,
                Marshal.SizeOf<NativeReachySimSnapshotHeader>(),
                "snapshot header size");
            AssertEqual(
                272,
                Marshal.SizeOf<NativeReachySimErrorInfo>(),
                "error info size");

            AssertEqual(
                new IntPtr(8),
                Marshal.OffsetOf<NativeReachySimConfig>(
                    nameof(NativeReachySimConfig.TimestepSeconds)),
                "config timestep offset");
            AssertEqual(
                new IntPtr(16),
                Marshal.OffsetOf<NativeReachySimStateHeader>(
                    nameof(NativeReachySimStateHeader.SimulationTime)),
                "state time offset");
            AssertEqual(
                new IntPtr(40),
                Marshal.OffsetOf<NativeReachySimSnapshotHeader>(
                    nameof(NativeReachySimSnapshotHeader.CalibrationProfileId)),
                "snapshot calibration offset");
            AssertEqual(
                new IntPtr(16),
                Marshal.OffsetOf<NativeReachySimErrorInfo>(
                    nameof(NativeReachySimErrorInfo.Message)),
                "error message offset");
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

        private static ReachyPublishedSimulationSnapshot WaitForSnapshot(
            ReachySimulationWorker worker,
            Func<ReachyPublishedSimulationSnapshot, bool> predicate,
            TimeSpan timeout,
            string description)
        {
            long deadline = Stopwatch.GetTimestamp() + checked(
                (long)Math.Ceiling(
                    timeout.TotalSeconds * Stopwatch.Frequency));
            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot snapshot) &&
                    predicate(snapshot))
                {
                    return snapshot;
                }

                ReachySimulationFault? fault = worker.Fault;
                if (fault != null)
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: worker faulted in {fault.Operation}: {fault.Error.Code}: {fault.Error.Message}");
                }

                Thread.Sleep(1);
            }

            throw new InvalidOperationException(
                $"Managed test timed out waiting for {description}.");
        }

        private static byte[] CreateCommandBatch(ulong sequence)
        {
            byte[] bytes = new byte[24];
            WriteUInt32(bytes, 0, ProjectMetadata.NativeAbiVersion);
            WriteUInt32(bytes, 4, 24U);
            WriteUInt64(bytes, 8, sequence);
            WriteUInt32(bytes, 16, 0U);
            WriteUInt32(bytes, 20, checked((uint)bytes.Length));
            return bytes;
        }

        private static void WriteUInt32(
            byte[] destination,
            int offset,
            uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(
            byte[] destination,
            int offset,
            ulong value)
        {
            WriteUInt32(destination, offset, (uint)value);
            WriteUInt32(destination, offset + 4, (uint)(value >> 32));
        }

        private static void AssertBytesEqual(
            byte[] expected,
            byte[] actual,
            string description)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected.Length} bytes, actual {actual.Length}.");
            }

            for (int index = 0; index < expected.Length; ++index)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: byte {index} differs.");
                }
            }
        }

        private static void AssertControlSuccess(
            ReachySimulationControlResult result,
            string description)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: {result.Error.Code}: {result.Error.Message}");
            }
        }

        private static void AssertEqual<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }

        private static void AssertThrows<TException>(
            Action action,
            string description)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Managed test failed for {description}: expected {typeof(TException).Name}.");
        }
    }
}
