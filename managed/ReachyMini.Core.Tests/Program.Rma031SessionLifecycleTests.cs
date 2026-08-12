using System;
using System.Runtime.InteropServices;
using System.Text;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
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
    }
}
