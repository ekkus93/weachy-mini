#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Interop;
using ReachyMini.Presentation;
using ReachyMini.Simulation;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Tests
{
    public sealed class ReachyAuthoritativeCameraRotationTests
    {
        private const string PrefabPath =
            "Assets/Generated/ReachyMini/UnityPresentation/Resources/" +
            "ReachyMiniPresentation.prefab";

        [Test]
        public void GeneratedModelRetainsPinnedCanonicalCameraBody()
        {
            ReachyCameraMujocoOpticalBinding binding =
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationBody? match = null;
                foreach (ReachyPresentationBody body in
                    contents.GetComponentsInChildren<
                        ReachyPresentationBody>(true))
                {
                    if (body.BodyIndex !=
                        ReachyCameraMujocoOpticalBinding
                            .CanonicalCameraPresentationIndex)
                    {
                        continue;
                    }
                    Assert.That(match, Is.Null);
                    match = body;
                }

                Assert.That(match, Is.Not.Null);
                Assert.That(
                    match!.BodyName,
                    Is.EqualTo(binding.CanonicalCameraBodyName));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void SourceUsesSolvedPoseAndRejectsStaleSequence()
        {
            var fake = new FakeAuthoritativeStateSource();
            fake.Publish(
                sequence: 10UL,
                continuityId: 4U,
                cameraBodyPose: NeutralCameraBodyPose(
                    positionX: 1000.0,
                    positionY: -2000.0,
                    positionZ: 3000.0));
            var source =
                new ReachyAuthoritativeCameraRotationSource(fake);

            ReachyCameraRotationCaptureResult first = source.Capture(
                Profile(),
                ReachyPhoneOpticalOrientationSample.Identity(55L));
            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(
                first.Sample!.CurrentReachyFromNeutralReachy
                    .ApproximatelyEquals(
                        ReachyMatrix3x3.Identity,
                        1.0e-9),
                Is.True);
            Assert.That(first.Sample.AuthoritativeSequence, Is.EqualTo(10UL));

            ReachyCameraRotationCaptureResult stale = source.Capture(
                Profile(),
                ReachyPhoneOpticalOrientationSample.Identity(56L));
            Assert.That(
                stale.Status,
                Is.EqualTo(
                    ReachyCameraRotationCaptureStatus
                        .StaleAuthoritativeState));
        }

        [Test]
        public void SourceIgnoresTranslationAndAcceptsContinuityReset()
        {
            var fake = new FakeAuthoritativeStateSource();
            var source =
                new ReachyAuthoritativeCameraRotationSource(fake);

            fake.Publish(
                5UL,
                1U,
                NeutralCameraBodyPose(0.0, 0.0, 0.0));
            ReachyCameraRotationCaptureResult first = source.Capture(
                Profile(),
                ReachyPhoneOpticalOrientationSample.Identity(1L));

            fake.Publish(
                1UL,
                2U,
                NeutralCameraBodyPose(9.0e6, -8.0e6, 7.0e6));
            ReachyCameraRotationCaptureResult reset = source.Capture(
                Profile(),
                ReachyPhoneOpticalOrientationSample.Identity(2L));

            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(reset.Succeeded, Is.True, reset.Message);
            Assert.That(
                reset.Sample!.AuthoritativeSequence,
                Is.EqualTo(1UL));
            Assert.That(
                reset.Sample.ContinuityId,
                Is.EqualTo(2U));
            Assert.That(
                reset.Sample.CurrentReachyFromCurrentPhone
                    .ApproximatelyEquals(
                        first.Sample!.CurrentReachyFromCurrentPhone,
                        1.0e-9),
                Is.True);
        }

        [Test]
        public void SourceFailsClosedForMissingOrDuplicatedCameraBody()
        {
            var missingFake = new FakeAuthoritativeStateSource(
                includeCameraBody: false);
            missingFake.Publish(
                1UL,
                1U,
                NeutralCameraBodyPose(0.0, 0.0, 0.0));
            ReachyCameraRotationCaptureResult missing =
                new ReachyAuthoritativeCameraRotationSource(missingFake)
                    .Capture(
                        Profile(),
                        ReachyPhoneOpticalOrientationSample.Identity(0L));
            Assert.That(
                missing.Status,
                Is.EqualTo(
                    ReachyCameraRotationCaptureStatus.CameraBodyMissing));

            var duplicateFake = new FakeAuthoritativeStateSource(
                duplicateCameraBody: true);
            duplicateFake.Publish(
                1UL,
                1U,
                NeutralCameraBodyPose(0.0, 0.0, 0.0));
            ReachyCameraRotationCaptureResult duplicate =
                new ReachyAuthoritativeCameraRotationSource(duplicateFake)
                    .Capture(
                        Profile(),
                        ReachyPhoneOpticalOrientationSample.Identity(0L));
            Assert.That(
                duplicate.Status,
                Is.EqualTo(
                    ReachyCameraRotationCaptureStatus
                        .CameraBodyDuplicated));
        }

        private static ReachySimBodyPoseSnapshot NeutralCameraBodyPose(
            double positionX,
            double positionY,
            double positionZ)
        {
            return new ReachySimBodyPoseSnapshot(
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                positionX,
                positionY,
                positionZ,
                quaternionW: 0.5,
                quaternionX: 0.5,
                quaternionY: -0.5,
                quaternionZ: -0.5);
        }

        private static ReachyCameraCalibrationProfile Profile()
        {
            var normalization = new ReachyCameraImageNormalization(
                640,
                480,
                0,
                0,
                640,
                480,
                0,
                false);
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    500.0,
                    319.5,
                    239.5);
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "unity-rma101",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "Unity RMA-101 test profile",
                "sha256:unity-rma101",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                intrinsics,
                intrinsics,
                ReachyQuaternionD.Identity);
        }

        private sealed class FakeAuthoritativeStateSource :
            IReachyPublishedAuthoritativeStateSource
        {
            private readonly bool includeCameraBody;
            private readonly bool duplicateCameraBody;
            private ReachySimAuthoritativeStateFrame? published;

            public FakeAuthoritativeStateSource(
                bool includeCameraBody = true,
                bool duplicateCameraBody = false)
            {
                this.includeCameraBody = includeCameraBody;
                this.duplicateCameraBody = duplicateCameraBody;
                AuthoritativeStateLayout =
                    new ReachySimAuthoritativeStateLayout(
                        4096,
                        0x12345678UL,
                        0,
                        0,
                        0,
                        ReachyCameraMujocoOpticalBinding
                            .CanonicalBodyPoseCount);
            }

            public ReachySimAuthoritativeStateLayout
                AuthoritativeStateLayout { get; }

            public ReachySimAuthoritativeStateFrame
                CreateAuthoritativeStateFrame()
            {
                return new ReachySimAuthoritativeStateFrame(
                    AuthoritativeStateLayout);
            }

            public void Publish(
                ulong sequence,
                uint continuityId,
                ReachySimBodyPoseSnapshot cameraBodyPose)
            {
                ReachySimAuthoritativeStateFrame next =
                    CreateAuthoritativeStateFrame();
                next.Sequence = sequence;
                next.ContinuityId = continuityId;
                next.SimulationTime = 0.5;
                for (int index = 0;
                    index < next.BodyPoseCount;
                    ++index)
                {
                    uint bodyId = (uint)(index + 1);
                    if (!includeCameraBody &&
                        bodyId ==
                        ReachyCameraMujocoOpticalBinding
                            .CanonicalCameraBodyId)
                    {
                        bodyId = 1000U;
                    }
                    if (duplicateCameraBody &&
                        index ==
                        next.BodyPoseCount - 1)
                    {
                        bodyId =
                            ReachyCameraMujocoOpticalBinding
                                .CanonicalCameraBodyId;
                    }

                    next.SetBodyPose(
                        index,
                        bodyId ==
                            ReachyCameraMujocoOpticalBinding
                                .CanonicalCameraBodyId
                            ? cameraBodyPose
                            : new ReachySimBodyPoseSnapshot(
                                bodyId,
                                0.0,
                                0.0,
                                0.0,
                                1.0,
                                0.0,
                                0.0,
                                0.0));
                }
                published = next;
            }

            public bool TryCaptureLatestAuthoritativeState(
                ReachySimAuthoritativeStateFrame destination)
            {
                if (published == null)
                {
                    return false;
                }

                destination.Sequence = published.Sequence;
                destination.ContinuityId = published.ContinuityId;
                destination.SimulationTime = published.SimulationTime;
                for (int index = 0;
                    index < destination.BodyPoseCount;
                    ++index)
                {
                    destination.SetBodyPose(
                        index,
                        published.GetBodyPose(index));
                }
                return true;
            }
        }
    }
}
