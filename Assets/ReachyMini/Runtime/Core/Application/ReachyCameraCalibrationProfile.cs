#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed class ReachyCameraCalibrationProfile
    {
        public const int CurrentProfileSchemaVersion = 1;
        public const int MaximumTextCharacters = 512;

        public ReachyCameraCalibrationProfile(
            int profileSchemaVersion,
            string profileId,
            string cameraId,
            ReachyDeviceCameraFacing facing,
            ReachyCameraCalibrationProvenance provenance,
            string provenanceDetail,
            string sourceReference,
            string modelCompatibility,
            DateTimeOffset createdUtc,
            ReachyCameraImageNormalization imageNormalization,
            ReachyCameraIntrinsicMatrix phoneIntrinsics,
            ReachyCameraIntrinsicMatrix reachyIntrinsics,
            ReachyQuaternionD neutralReachyFromPhoneRotation)
        {
            if (profileSchemaVersion != CurrentProfileSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileSchemaVersion),
                    profileSchemaVersion,
                    $"Camera calibration profile schema must be {CurrentProfileSchemaVersion}.");
            }
            RequireText(profileId, nameof(profileId));
            RequireText(cameraId, nameof(cameraId));
            RequireText(provenanceDetail, nameof(provenanceDetail));
            RequireText(sourceReference, nameof(sourceReference));
            RequireText(modelCompatibility, nameof(modelCompatibility));
            if (facing != ReachyDeviceCameraFacing.Front &&
                facing != ReachyDeviceCameraFacing.Rear &&
                facing != ReachyDeviceCameraFacing.External)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(facing),
                    facing,
                    "Calibration requires a front, rear, or external camera.");
            }
            if (!Enum.IsDefined(typeof(ReachyCameraCalibrationProvenance), provenance) ||
                provenance == ReachyCameraCalibrationProvenance.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(provenance),
                    provenance,
                    "Calibration provenance must be explicit.");
            }
            if (createdUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Calibration creation time must use UTC.",
                    nameof(createdUtc));
            }

            ImageNormalization = imageNormalization ??
                throw new ArgumentNullException(nameof(imageNormalization));
            PhoneIntrinsics = phoneIntrinsics ??
                throw new ArgumentNullException(nameof(phoneIntrinsics));
            ReachyIntrinsics = reachyIntrinsics ??
                throw new ArgumentNullException(nameof(reachyIntrinsics));
            if (PhoneIntrinsics.ImageWidth != ImageNormalization.OutputWidth ||
                PhoneIntrinsics.ImageHeight != ImageNormalization.OutputHeight)
            {
                throw new ArgumentException(
                    "Phone intrinsics must describe the normalized RMA-092 RGB texture dimensions.",
                    nameof(phoneIntrinsics));
            }
            bool expectedMirror = facing == ReachyDeviceCameraFacing.Front;
            if (ImageNormalization.MirrorHorizontally != expectedMirror)
            {
                throw new ArgumentException(
                    "Front calibration must record the RMA-092 horizontal preview mirror; rear and external calibration must not.",
                    nameof(imageNormalization));
            }
            ReachyMatrix3x3 neutralRotation =
                neutralReachyFromPhoneRotation.ToRotationMatrix();
            if (!neutralRotation.IsProperRotation())
            {
                throw new ArgumentException(
                    "Neutral phone-to-Reachy orientation must be a proper rotation.",
                    nameof(neutralReachyFromPhoneRotation));
            }

            ProfileSchemaVersion = profileSchemaVersion;
            ProfileId = profileId;
            CameraId = cameraId;
            Facing = facing;
            Provenance = provenance;
            ProvenanceDetail = provenanceDetail;
            SourceReference = sourceReference;
            ModelCompatibility = modelCompatibility;
            CreatedUtc = createdUtc;
            NeutralReachyFromPhoneRotation = neutralReachyFromPhoneRotation;
            ReprojectionMode = ReachyCameraReprojectionMode.RotationOnly;
        }

        public int ProfileSchemaVersion { get; }

        public string ProfileId { get; }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing Facing { get; }

        public ReachyCameraCalibrationProvenance Provenance { get; }

        public string ProvenanceDetail { get; }

        public string SourceReference { get; }

        public string ModelCompatibility { get; }

        public DateTimeOffset CreatedUtc { get; }

        public ReachyCameraImageNormalization ImageNormalization { get; }

        public ReachyCameraIntrinsicMatrix PhoneIntrinsics { get; }

        public ReachyCameraIntrinsicMatrix ReachyIntrinsics { get; }

        public ReachyQuaternionD NeutralReachyFromPhoneRotation { get; }

        public ReachyCameraReprojectionMode ReprojectionMode { get; }

        public bool IsCalibrated =>
            Provenance != ReachyCameraCalibrationProvenance.UncalibratedEstimate;

        public ReachyMatrix3x3 BuildNeutralHomography()
        {
            return ReachyIntrinsics.PixelFromOpticalRay *
                NeutralReachyFromPhoneRotation.ToRotationMatrix() *
                PhoneIntrinsics.OpticalRayFromPixel;
        }

        public string Summary =>
            $"profile={ProfileId}; camera={CameraId}; facing={Facing}; " +
            $"phone={PhoneIntrinsics.ImageWidth}x{PhoneIntrinsics.ImageHeight}; " +
            $"reachy={ReachyIntrinsics.ImageWidth}x{ReachyIntrinsics.ImageHeight}; " +
            $"mode={ReprojectionMode}; calibrated={IsCalibrated}; " +
            $"provenance={Provenance}; model={ModelCompatibility}";

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextCharacters)
            {
                throw new ArgumentException(
                    $"Calibration text fields must contain 1-{MaximumTextCharacters} characters.",
                    name);
            }
        }
    }
}
