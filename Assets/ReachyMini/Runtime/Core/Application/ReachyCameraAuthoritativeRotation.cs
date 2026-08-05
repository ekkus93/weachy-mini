#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraRotationCaptureStatus
    {
        Success = 0,
        StateUnavailable = 1,
        CalibrationModelMismatch = 2,
        AuthoritativeLayoutMismatch = 3,
        CameraBodyMissing = 4,
        CameraBodyDuplicated = 5,
        InvalidAuthoritativePose = 6,
        StaleAuthoritativeState = 7,
    }

    public sealed class ReachyCameraMujocoOpticalBinding
    {
        public const string OfficialModelCompatibility =
            "reachy-mini-official-v1";
        public const string PinnedSourceCommit =
            "a739a6e461eb6d722901f1cfc225265ffc85c28d";
        public const string PinnedSourceModelSha256 =
            "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46";
        public const string PinnedOpticalSiteName = "camera_optical";
        public const string PinnedCameraName = "eye_camera";
        public const string PinnedCanonicalCameraBodyName = "__body_15";
        public const uint CanonicalCameraBodyId = 15U;
        public const int CanonicalBodyPoseCount = 18;

        public ReachyCameraMujocoOpticalBinding(
            string modelCompatibility,
            string sourceCommit,
            string sourceModelSha256,
            string opticalSiteName,
            string cameraName,
            string canonicalCameraBodyName,
            uint cameraBodyId,
            int expectedBodyPoseCount,
            ReachyMatrix3x3 cameraBodyFromOptical,
            ReachyMatrix3x3 neutralMujocoWorldFromOptical)
        {
            RequireText(modelCompatibility, nameof(modelCompatibility));
            RequireText(sourceCommit, nameof(sourceCommit));
            RequireText(sourceModelSha256, nameof(sourceModelSha256));
            RequireText(opticalSiteName, nameof(opticalSiteName));
            RequireText(cameraName, nameof(cameraName));
            RequireText(
                canonicalCameraBodyName,
                nameof(canonicalCameraBodyName));
            if (cameraBodyId == 0U)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cameraBodyId),
                    cameraBodyId,
                    "The world body cannot be used as the Reachy eye.");
            }
            if (expectedBodyPoseCount <= 0 ||
                cameraBodyId > (uint)expectedBodyPoseCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedBodyPoseCount),
                    expectedBodyPoseCount,
                    "The authoritative body layout must contain the camera body.");
            }
            if (!cameraBodyFromOptical.IsProperRotation())
            {
                throw new ArgumentException(
                    "The fixed camera-body/optical relationship must be a proper rotation.",
                    nameof(cameraBodyFromOptical));
            }
            if (!neutralMujocoWorldFromOptical.IsProperRotation())
            {
                throw new ArgumentException(
                    "The neutral MuJoCo-world/optical relationship must be a proper rotation.",
                    nameof(neutralMujocoWorldFromOptical));
            }

            ModelCompatibility = modelCompatibility;
            SourceCommit = sourceCommit;
            SourceModelSha256 = sourceModelSha256;
            OpticalSiteName = opticalSiteName;
            CameraName = cameraName;
            CanonicalCameraBodyName = canonicalCameraBodyName;
            CameraBodyId = cameraBodyId;
            ExpectedBodyPoseCount = expectedBodyPoseCount;
            CameraBodyFromOptical = cameraBodyFromOptical;
            NeutralMujocoWorldFromOptical =
                neutralMujocoWorldFromOptical;
        }

        public static ReachyCameraMujocoOpticalBinding PinnedReachyMini { get; } =
            new ReachyCameraMujocoOpticalBinding(
                OfficialModelCompatibility,
                PinnedSourceCommit,
                PinnedSourceModelSha256,
                PinnedOpticalSiteName,
                PinnedCameraName,
                PinnedCanonicalCameraBodyName,
                CanonicalCameraBodyId,
                CanonicalBodyPoseCount,
                new ReachyMatrix3x3(
                    1.0, 0.0, 0.0,
                    0.0, -1.0, 0.0,
                    0.0, 0.0, -1.0),
                new ReachyMatrix3x3(
                    0.0, 0.0, 1.0,
                    -1.0, 0.0, 0.0,
                    0.0, -1.0, 0.0));

        public string ModelCompatibility { get; }

        public string SourceCommit { get; }

        public string SourceModelSha256 { get; }

        public string OpticalSiteName { get; }

        public string CameraName { get; }

        public string CanonicalCameraBodyName { get; }

        public uint CameraBodyId { get; }

        public int ExpectedBodyPoseCount { get; }

        public ReachyMatrix3x3 CameraBodyFromOptical { get; }

        public ReachyMatrix3x3 NeutralMujocoWorldFromOptical { get; }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "MuJoCo camera binding text cannot be empty.",
                    name);
            }
        }
    }

    public sealed class ReachyPhoneOpticalOrientationSample
    {
        public ReachyPhoneOpticalOrientationSample(
            long timestampNanoseconds,
            ReachyQuaternionD neutralPhoneFromCurrentPhone)
        {
            if (timestampNanoseconds < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampNanoseconds),
                    timestampNanoseconds,
                    "A phone orientation timestamp cannot be negative.");
            }
            ReachyMatrix3x3 rotation =
                neutralPhoneFromCurrentPhone.ToRotationMatrix();
            if (!rotation.IsProperRotation())
            {
                throw new ArgumentException(
                    "Phone orientation must be a proper rotation.",
                    nameof(neutralPhoneFromCurrentPhone));
            }

            TimestampNanoseconds = timestampNanoseconds;
            NeutralPhoneFromCurrentPhone =
                neutralPhoneFromCurrentPhone;
        }

        public static ReachyPhoneOpticalOrientationSample Identity(
            long timestampNanoseconds)
        {
            return new ReachyPhoneOpticalOrientationSample(
                timestampNanoseconds,
                ReachyQuaternionD.Identity);
        }

        public long TimestampNanoseconds { get; }

        public ReachyQuaternionD NeutralPhoneFromCurrentPhone { get; }
    }

    public sealed class ReachyCameraRelativeRotationSample
    {
        public ReachyCameraRelativeRotationSample(
            ulong modelHash,
            ulong authoritativeSequence,
            double simulationTimeSeconds,
            uint continuityId,
            long phoneTimestampNanoseconds,
            uint cameraBodyId,
            ReachyMatrix3x3 currentMujocoWorldFromReachyOptical,
            ReachyMatrix3x3 currentReachyFromNeutralReachy,
            ReachyMatrix3x3 currentReachyFromCurrentPhone)
        {
            if (modelHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(modelHash),
                    modelHash,
                    "An authoritative camera sample requires a nonzero model hash.");
            }
            if (double.IsNaN(simulationTimeSeconds) ||
                double.IsInfinity(simulationTimeSeconds) ||
                simulationTimeSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(simulationTimeSeconds),
                    simulationTimeSeconds,
                    "Simulation time must be finite and nonnegative.");
            }
            if (phoneTimestampNanoseconds < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phoneTimestampNanoseconds),
                    phoneTimestampNanoseconds,
                    "A phone timestamp cannot be negative.");
            }
            if (cameraBodyId == 0U)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cameraBodyId),
                    cameraBodyId,
                    "A camera sample cannot use the world body.");
            }
            RequireRotation(
                currentMujocoWorldFromReachyOptical,
                nameof(currentMujocoWorldFromReachyOptical));
            RequireRotation(
                currentReachyFromNeutralReachy,
                nameof(currentReachyFromNeutralReachy));
            RequireRotation(
                currentReachyFromCurrentPhone,
                nameof(currentReachyFromCurrentPhone));

            ModelHash = modelHash;
            AuthoritativeSequence = authoritativeSequence;
            SimulationTimeSeconds = simulationTimeSeconds;
            ContinuityId = continuityId;
            PhoneTimestampNanoseconds = phoneTimestampNanoseconds;
            CameraBodyId = cameraBodyId;
            CurrentMujocoWorldFromReachyOptical =
                currentMujocoWorldFromReachyOptical;
            CurrentReachyFromNeutralReachy =
                currentReachyFromNeutralReachy;
            CurrentReachyFromCurrentPhone =
                currentReachyFromCurrentPhone;
        }

        public ulong ModelHash { get; }

        public ulong AuthoritativeSequence { get; }

        public double SimulationTimeSeconds { get; }

        public uint ContinuityId { get; }

        public long PhoneTimestampNanoseconds { get; }

        public uint CameraBodyId { get; }

        public ReachyMatrix3x3 CurrentMujocoWorldFromReachyOptical { get; }

        public ReachyMatrix3x3 CurrentReachyFromNeutralReachy { get; }

        public ReachyMatrix3x3 CurrentReachyFromCurrentPhone { get; }

        private static void RequireRotation(
            ReachyMatrix3x3 rotation,
            string name)
        {
            if (!rotation.IsProperRotation())
            {
                throw new ArgumentException(
                    "Authoritative camera transforms must be proper rotations.",
                    name);
            }
        }
    }

    public sealed class ReachyCameraRotationCaptureResult
    {
        public ReachyCameraRotationCaptureResult(
            ReachyCameraRotationCaptureStatus status,
            ReachyCameraRelativeRotationSample? sample,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Camera rotation capture requires diagnostics.",
                    nameof(message));
            }
            bool succeeded =
                status == ReachyCameraRotationCaptureStatus.Success;
            if (succeeded != (sample != null))
            {
                throw new ArgumentException(
                    "Camera rotation capture status and sample disagree.",
                    nameof(sample));
            }

            Status = status;
            Sample = sample;
            Message = message;
        }

        public ReachyCameraRotationCaptureStatus Status { get; }

        public ReachyCameraRelativeRotationSample? Sample { get; }

        public string Message { get; }

        public bool Succeeded =>
            Status == ReachyCameraRotationCaptureStatus.Success;
    }

    public static class ReachyCameraRelativeRotationCalculator
    {
        public static ReachyCameraRelativeRotationSample Calculate(
            ReachyCameraMujocoOpticalBinding binding,
            ReachyCameraCalibrationProfile calibration,
            ulong modelHash,
            ulong authoritativeSequence,
            double simulationTimeSeconds,
            uint continuityId,
            ReachyQuaternionD mujocoWorldFromCameraBody,
            ReachyPhoneOpticalOrientationSample phoneOrientation)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (calibration == null)
            {
                throw new ArgumentNullException(nameof(calibration));
            }
            if (phoneOrientation == null)
            {
                throw new ArgumentNullException(nameof(phoneOrientation));
            }
            if (!string.Equals(
                    calibration.ModelCompatibility,
                    binding.ModelCompatibility,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The calibration profile does not match the authoritative Reachy model binding.",
                    nameof(calibration));
            }

            ReachyMatrix3x3 worldFromCameraBody =
                mujocoWorldFromCameraBody.ToRotationMatrix();
            ReachyMatrix3x3 currentWorldFromOptical =
                worldFromCameraBody * binding.CameraBodyFromOptical;
            if (!currentWorldFromOptical.IsProperRotation())
            {
                throw new InvalidOperationException(
                    "The authoritative camera-body pose did not produce a proper optical rotation.");
            }

            // Maps a ray expressed in the neutral Reachy optical frame into
            // the current Reachy optical frame. Translation is intentionally
            // absent from the Level 1 contract.
            ReachyMatrix3x3 currentReachyFromNeutralReachy =
                currentWorldFromOptical.Transposed() *
                binding.NeutralMujocoWorldFromOptical;

            // Phone orientation is explicit and remains separate from the
            // simulated head rotation:
            // currentReachy <- neutralReachy <- neutralPhone <- currentPhone.
            ReachyMatrix3x3 currentReachyFromCurrentPhone =
                currentReachyFromNeutralReachy *
                calibration.NeutralReachyFromPhoneRotation.ToRotationMatrix() *
                phoneOrientation.NeutralPhoneFromCurrentPhone
                    .ToRotationMatrix();

            return new ReachyCameraRelativeRotationSample(
                modelHash,
                authoritativeSequence,
                simulationTimeSeconds,
                continuityId,
                phoneOrientation.TimestampNanoseconds,
                binding.CameraBodyId,
                currentWorldFromOptical,
                currentReachyFromNeutralReachy,
                currentReachyFromCurrentPhone);
        }
    }
}
