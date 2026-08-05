#nullable enable

using System;
using ReachyMini.AppState;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed class ReachyAuthoritativeCameraRotationSource
    {
        private readonly object gate = new object();
        private readonly IReachyPublishedAuthoritativeStateSource source;
        private readonly ReachyCameraMujocoOpticalBinding binding;
        private readonly ReachySimAuthoritativeStateFrame frame;
        private bool hasAcceptedState;
        private ulong lastSequence;
        private uint lastContinuityId;

        public ReachyAuthoritativeCameraRotationSource(
            IReachyPublishedAuthoritativeStateSource source,
            ReachyCameraMujocoOpticalBinding? binding = null)
        {
            this.source = source ??
                throw new ArgumentNullException(nameof(source));
            this.binding = binding ??
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            frame = source.CreateAuthoritativeStateFrame();
        }

        public ReachyCameraMujocoOpticalBinding Binding => binding;

        public ReachyCameraRotationCaptureResult Capture(
            ReachyCameraCalibrationProfile calibration,
            ReachyPhoneOpticalOrientationSample phoneOrientation)
        {
            if (calibration == null)
            {
                throw new ArgumentNullException(nameof(calibration));
            }
            if (phoneOrientation == null)
            {
                throw new ArgumentNullException(nameof(phoneOrientation));
            }

            lock (gate)
            {
                if (!string.Equals(
                        calibration.ModelCompatibility,
                        binding.ModelCompatibility,
                        StringComparison.Ordinal))
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus
                            .CalibrationModelMismatch,
                        "The selected calibration profile does not match the pinned Reachy camera binding.");
                }

                ReachySimAuthoritativeStateLayout layout =
                    source.AuthoritativeStateLayout;
                if (layout.ModelHash == 0UL ||
                    layout.BodyPoseCount != binding.ExpectedBodyPoseCount ||
                    !layout.Matches(frame.Layout))
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus
                            .AuthoritativeLayoutMismatch,
                        "The authoritative model layout does not match the pinned camera-body contract.");
                }

                if (!source.TryCaptureLatestAuthoritativeState(frame))
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus.StateUnavailable,
                        "No authoritative MuJoCo state is currently published.");
                }

                int cameraIndex = -1;
                for (int index = 0; index < frame.BodyPoseCount; ++index)
                {
                    if (frame.GetBodyPose(index).BodyId !=
                        binding.CameraBodyId)
                    {
                        continue;
                    }
                    if (cameraIndex >= 0)
                    {
                        return Failure(
                            ReachyCameraRotationCaptureStatus
                                .CameraBodyDuplicated,
                            $"Authoritative body ID {binding.CameraBodyId} is duplicated.");
                    }
                    cameraIndex = index;
                }
                if (cameraIndex < 0)
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus.CameraBodyMissing,
                        $"Authoritative body ID {binding.CameraBodyId} is missing.");
                }

                if (hasAcceptedState &&
                    frame.ContinuityId == lastContinuityId &&
                    frame.Sequence <= lastSequence)
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus
                            .StaleAuthoritativeState,
                        "The authoritative camera state did not advance within the current continuity.");
                }

                ReachySimBodyPoseSnapshot pose =
                    frame.GetBodyPose(cameraIndex);
                try
                {
                    var worldFromCameraBody = new ReachyQuaternionD(
                        pose.QuaternionX,
                        pose.QuaternionY,
                        pose.QuaternionZ,
                        pose.QuaternionW);
                    ReachyCameraRelativeRotationSample sample =
                        ReachyCameraRelativeRotationCalculator.Calculate(
                            binding,
                            calibration,
                            layout.ModelHash,
                            frame.Sequence,
                            frame.SimulationTime,
                            frame.ContinuityId,
                            worldFromCameraBody,
                            phoneOrientation);

                    hasAcceptedState = true;
                    lastSequence = frame.Sequence;
                    lastContinuityId = frame.ContinuityId;
                    return new ReachyCameraRotationCaptureResult(
                        ReachyCameraRotationCaptureStatus.Success,
                        sample,
                        $"Authoritative optical rotation captured from {binding.CanonicalCameraBodyName} " +
                        $"at sequence {frame.Sequence}; body translation was not consumed.");
                }
                catch (ArgumentException exception)
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus
                            .InvalidAuthoritativePose,
                        $"Authoritative camera rotation is invalid: {exception.Message}");
                }
                catch (InvalidOperationException exception)
                {
                    return Failure(
                        ReachyCameraRotationCaptureStatus
                            .InvalidAuthoritativePose,
                        $"Authoritative camera rotation is invalid: {exception.Message}");
                }
            }
        }

        private static ReachyCameraRotationCaptureResult Failure(
            ReachyCameraRotationCaptureStatus status,
            string message)
        {
            return new ReachyCameraRotationCaptureResult(
                status,
                null,
                message);
        }
    }
}
