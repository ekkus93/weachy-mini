#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.Tests
{
    public sealed class ReachyCameraReprojectionCalibrationTests
    {
        [Test]
        public void UnityAdapterPreservesCoreMatrixAndQuaternionDirections()
        {
            ReachyQuaternionD coreRotation = ReachyQuaternionD.FromAxisAngle(
                new ReachyVector3D(0.0, 1.0, 0.0),
                Math.PI / 6.0);
            ReachyMatrix3x3 coreMatrix = coreRotation.ToRotationMatrix();
            ReachyVector3D coreDirection = coreMatrix.Transform(
                new ReachyVector3D(0.0, 0.0, 1.0));

            Matrix4x4 unityMatrix =
                ReachyCameraCalibrationUnityAdapter.ToUnityMatrix(coreMatrix);
            Vector3 unityMatrixDirection = unityMatrix.MultiplyVector(Vector3.forward);
            Quaternion unityQuaternion =
                ReachyCameraCalibrationUnityAdapter.ToUnityQuaternion(coreRotation);
            Vector3 unityQuaternionDirection = unityQuaternion * Vector3.forward;

            Assert.That(
                unityMatrixDirection.x,
                Is.EqualTo((float)coreDirection.X).Within(1.0e-6f));
            Assert.That(
                unityMatrixDirection.y,
                Is.EqualTo((float)coreDirection.Y).Within(1.0e-6f));
            Assert.That(
                unityMatrixDirection.z,
                Is.EqualTo((float)coreDirection.Z).Within(1.0e-6f));
            Assert.That(
                unityQuaternionDirection.x,
                Is.EqualTo(unityMatrixDirection.x).Within(1.0e-6f));
            Assert.That(
                unityQuaternionDirection.y,
                Is.EqualTo(unityMatrixDirection.y).Within(1.0e-6f));
            Assert.That(
                unityQuaternionDirection.z,
                Is.EqualTo(unityMatrixDirection.z).Within(1.0e-6f));
            Assert.That(
                ReachyCameraCalibrationUnityAdapter.ToCoreQuaternion(
                    unityQuaternion).ToRotationMatrix().ApproximatelyEquals(
                        coreMatrix,
                        1.0e-6),
                Is.True);
        }

        [Test]
        public void PersistenceRoundTripRetainsVersionedCalibration()
        {
            string directory = CreateTemporaryDirectory();
            string path = Path.Combine(
                directory,
                ReachyCameraCalibrationPersistenceStore.CalibrationFileName);
            try
            {
                var writer = new ReachyCameraCalibrationPersistenceStore(path);
                writer.Initialize();
                writer.State.Upsert(CreateProfile(
                    "rear-calibrated",
                    ReachyDeviceCameraFacing.Rear));
                writer.Dispose();

                var reader = new ReachyCameraCalibrationPersistenceStore(path);
                reader.Initialize();
                ReachyCameraCalibrationSelectionResult selection =
                    reader.State.SelectExact(
                        "rear-0",
                        ReachyDeviceCameraFacing.Rear,
                        640,
                        480,
                        640,
                        480,
                        "reachy-mini-official-v1");

                Assert.That(reader.IsDegraded, Is.False);
                Assert.That(
                    selection.Status,
                    Is.EqualTo(
                        ReachyCameraCalibrationSelectionStatus.ExactCalibrated));
                Assert.That(
                    selection.Profile!.ImageNormalization.MirrorHorizontally,
                    Is.False);
                Assert.That(
                    selection.Profile.ReprojectionMode,
                    Is.EqualTo(ReachyCameraReprojectionMode.RotationOnly));
                reader.Dispose();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void UnsupportedSchemaIsQuarantinedWithoutSilentCalibration()
        {
            string directory = CreateTemporaryDirectory();
            string path = Path.Combine(
                directory,
                ReachyCameraCalibrationPersistenceStore.CalibrationFileName);
            try
            {
                File.WriteAllText(
                    path,
                    "{\"schemaVersion\":999,\"profiles\":[]}");
                var store = new ReachyCameraCalibrationPersistenceStore(path);

                store.Initialize();

                Assert.That(store.IsDegraded, Is.True);
                Assert.That(store.State.Current.Profiles, Is.Empty);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(
                    Directory.GetFiles(
                        directory,
                        ReachyCameraCalibrationPersistenceStore.CalibrationFileName +
                            ".corrupt-*"),
                    Has.Length.EqualTo(1));
                store.Dispose();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void SettingsServiceOwnsCalibrationPersistenceBoundary()
        {
            string directory = CreateTemporaryDirectory();
            string settingsPath = Path.Combine(
                directory,
                ReachySettingsPersistenceApplicationService.SettingsFileName);
            string calibrationPath = Path.Combine(
                directory,
                ReachyCameraCalibrationPersistenceStore.CalibrationFileName);
            try
            {
                var service = new ReachySettingsPersistenceApplicationService(
                    settingsPath,
                    calibrationPath);
                service.Initialize();
                service.CameraCalibrations.State.Upsert(CreateProfile(
                    "front-calibrated",
                    ReachyDeviceCameraFacing.Front));

                Assert.That(service.Health.State, Is.EqualTo(ReachyServiceState.Ready));
                Assert.That(File.Exists(settingsPath), Is.True);
                Assert.That(File.Exists(calibrationPath), Is.True);
                Assert.That(
                    service.CameraCalibrations.State.Current.Profiles,
                    Has.Count.EqualTo(1));
                service.Dispose();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static ReachyCameraCalibrationProfile CreateProfile(
            string profileId,
            ReachyDeviceCameraFacing facing)
        {
            bool mirror = facing == ReachyDeviceCameraFacing.Front;
            var normalization = new ReachyCameraImageNormalization(
                640,
                480,
                0,
                0,
                640,
                480,
                0,
                mirror);
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    500.0,
                    320.0,
                    240.0);
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                profileId,
                facing == ReachyDeviceCameraFacing.Front
                    ? "front-1"
                    : "rear-0",
                facing,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "checkerboard fit with retained source evidence",
                "sha256:test-calibration-dataset",
                "reachy-mini-official-v1",
                new DateTimeOffset(
                    2026,
                    8,
                    4,
                    22,
                    0,
                    0,
                    TimeSpan.Zero),
                normalization,
                intrinsics,
                intrinsics,
                ReachyQuaternionD.Identity);
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma100-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
