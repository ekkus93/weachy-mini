#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public sealed class ReachyCameraIntrinsicMatrix
    {
        public ReachyCameraIntrinsicMatrix(
            int imageWidth,
            int imageHeight,
            ReachyMatrix3x3 pixelFromOpticalRay)
        {
            if (imageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageWidth),
                    imageWidth,
                    "An intrinsic image width must be positive.");
            }
            if (imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageHeight),
                    imageHeight,
                    "An intrinsic image height must be positive.");
            }
            if (Math.Abs(pixelFromOpticalRay.M20) > 1.0e-12 ||
                Math.Abs(pixelFromOpticalRay.M21) > 1.0e-12 ||
                Math.Abs(pixelFromOpticalRay.M22 - 1.0) > 1.0e-12)
            {
                throw new ArgumentException(
                    "A camera intrinsic matrix must retain affine pixel projection in its final row.",
                    nameof(pixelFromOpticalRay));
            }
            if (Math.Abs(pixelFromOpticalRay.Determinant) <= 1.0e-12)
            {
                throw new ArgumentException(
                    "A camera intrinsic matrix must be invertible.",
                    nameof(pixelFromOpticalRay));
            }

            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            PixelFromOpticalRay = pixelFromOpticalRay;
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public ReachyMatrix3x3 PixelFromOpticalRay { get; }

        public ReachyMatrix3x3 OpticalRayFromPixel =>
            PixelFromOpticalRay.Inverse();

        public static ReachyCameraIntrinsicMatrix CreatePinhole(
            int imageWidth,
            int imageHeight,
            double focalLengthX,
            double focalLengthY,
            double principalPointX,
            double principalPointY,
            double skew = 0.0)
        {
            RequireFinitePositive(focalLengthX, nameof(focalLengthX));
            RequireFinitePositive(focalLengthY, nameof(focalLengthY));
            RequireFinite(principalPointX, nameof(principalPointX));
            RequireFinite(principalPointY, nameof(principalPointY));
            RequireFinite(skew, nameof(skew));
            return new ReachyCameraIntrinsicMatrix(
                imageWidth,
                imageHeight,
                new ReachyMatrix3x3(
                    focalLengthX,
                    skew,
                    principalPointX,
                    0.0,
                    focalLengthY,
                    principalPointY,
                    0.0,
                    0.0,
                    1.0));
        }

        public ReachyCameraIntrinsicMatrix TransformPixels(
            ReachyMatrix3x3 normalizedFromCurrentPixels,
            int outputWidth,
            int outputHeight)
        {
            return new ReachyCameraIntrinsicMatrix(
                outputWidth,
                outputHeight,
                normalizedFromCurrentPixels * PixelFromOpticalRay);
        }

        public ReachyVector3D GetOpticalRay(double pixelX, double pixelY)
        {
            return OpticalRayFromPixel.Transform(
                new ReachyVector3D(pixelX, pixelY, 1.0)).Normalized();
        }

        public ReachyVector3D ProjectOpticalRay(ReachyVector3D ray)
        {
            if (ray.Z <= 1.0e-12)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ray),
                    "Only rays in front of the camera can be projected.");
            }
            return PixelFromOpticalRay.TransformPixel(
                ray.X / ray.Z,
                ray.Y / ray.Z);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera intrinsic values must be finite.");
            }
        }

        private static void RequireFinitePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera focal lengths must be positive.");
            }
        }
    }

    public sealed class ReachyCameraImageNormalization
    {
        public ReachyCameraImageNormalization(
            int sourceWidth,
            int sourceHeight,
            int cropLeft,
            int cropTop,
            int cropWidth,
            int cropHeight,
            int clockwiseRotationDegrees,
            bool mirrorHorizontally)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceWidth),
                    "Camera source dimensions must be positive.");
            }
            if (cropLeft < 0 || cropTop < 0 ||
                cropWidth <= 0 || cropHeight <= 0 ||
                cropLeft + cropWidth > sourceWidth ||
                cropTop + cropHeight > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cropLeft),
                    "The camera crop must have positive extent and remain inside the source image.");
            }
            ValidateRightAngle(clockwiseRotationDegrees);

            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            CropLeft = cropLeft;
            CropTop = cropTop;
            CropWidth = cropWidth;
            CropHeight = cropHeight;
            ClockwiseRotationDegrees = clockwiseRotationDegrees;
            MirrorHorizontally = mirrorHorizontally;

            OutputWidth = clockwiseRotationDegrees == 90 ||
                clockwiseRotationDegrees == 270
                    ? cropHeight
                    : cropWidth;
            OutputHeight = clockwiseRotationDegrees == 90 ||
                clockwiseRotationDegrees == 270
                    ? cropWidth
                    : cropHeight;

            ReachyMatrix3x3 crop = new ReachyMatrix3x3(
                1.0,
                0.0,
                -cropLeft,
                0.0,
                1.0,
                -cropTop,
                0.0,
                0.0,
                1.0);
            ReachyMatrix3x3 rotation = CreateRotation(
                cropWidth,
                cropHeight,
                clockwiseRotationDegrees);
            ReachyMatrix3x3 mirror = mirrorHorizontally
                ? new ReachyMatrix3x3(
                    -1.0,
                    0.0,
                    OutputWidth - 1.0,
                    0.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0)
                : ReachyMatrix3x3.Identity;
            NormalizedFromSourcePixels = mirror * rotation * crop;
            SourceFromNormalizedPixels = NormalizedFromSourcePixels.Inverse();
        }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int CropLeft { get; }

        public int CropTop { get; }

        public int CropWidth { get; }

        public int CropHeight { get; }

        public int ClockwiseRotationDegrees { get; }

        public bool MirrorHorizontally { get; }

        public int OutputWidth { get; }

        public int OutputHeight { get; }

        public ReachyMatrix3x3 NormalizedFromSourcePixels { get; }

        public ReachyMatrix3x3 SourceFromNormalizedPixels { get; }

        public ReachyCameraIntrinsicMatrix NormalizeIntrinsics(
            ReachyCameraIntrinsicMatrix sourceIntrinsics)
        {
            if (sourceIntrinsics == null)
            {
                throw new ArgumentNullException(nameof(sourceIntrinsics));
            }
            if (sourceIntrinsics.ImageWidth != SourceWidth ||
                sourceIntrinsics.ImageHeight != SourceHeight)
            {
                throw new ArgumentException(
                    "Source intrinsics must match the pre-crop camera buffer dimensions.",
                    nameof(sourceIntrinsics));
            }
            return sourceIntrinsics.TransformPixels(
                NormalizedFromSourcePixels,
                OutputWidth,
                OutputHeight);
        }

        private static ReachyMatrix3x3 CreateRotation(
            int width,
            int height,
            int clockwiseRotationDegrees)
        {
            return clockwiseRotationDegrees switch
            {
                0 => ReachyMatrix3x3.Identity,
                90 => new ReachyMatrix3x3(
                    0.0,
                    -1.0,
                    height - 1.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0),
                180 => new ReachyMatrix3x3(
                    -1.0,
                    0.0,
                    width - 1.0,
                    0.0,
                    -1.0,
                    height - 1.0,
                    0.0,
                    0.0,
                    1.0),
                270 => new ReachyMatrix3x3(
                    0.0,
                    1.0,
                    0.0,
                    -1.0,
                    0.0,
                    width - 1.0,
                    0.0,
                    0.0,
                    1.0),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(clockwiseRotationDegrees),
                    clockwiseRotationDegrees,
                    "Camera rotation must be a right angle."),
            };
        }

        private static void ValidateRightAngle(int value)
        {
            if (value != 0 && value != 90 && value != 180 && value != 270)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Camera rotation must be 0, 90, 180, or 270 degrees.");
            }
        }
    }

    public sealed class ReachyCameraCalibrationProfile
    {
        public const int CurrentProfileSchemaVersion = 1;

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
            if (provenance == ReachyCameraCalibrationProvenance.Unknown)
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
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Calibration text fields cannot be empty.",
                    name);
            }
        }
    }

    public sealed class ReachyCameraCalibrationSelectionResult
    {
        public ReachyCameraCalibrationSelectionResult(
            ReachyCameraCalibrationSelectionStatus status,
            ReachyCameraCalibrationProfile? profile,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Calibration selection requires diagnostics.",
                    nameof(message));
            }
            bool selected = status ==
                    ReachyCameraCalibrationSelectionStatus.ExactCalibrated ||
                status == ReachyCameraCalibrationSelectionStatus.ExactUncalibratedEstimate;
            if (selected != (profile != null))
            {
                throw new ArgumentException(
                    "Calibration selection status and selected profile disagree.",
                    nameof(profile));
            }

            Status = status;
            Profile = profile;
            Message = message;
        }

        public ReachyCameraCalibrationSelectionStatus Status { get; }

        public ReachyCameraCalibrationProfile? Profile { get; }

        public string Message { get; }

        public bool HasProfile => Profile != null;

        public bool IsCalibrated =>
            Status == ReachyCameraCalibrationSelectionStatus.ExactCalibrated;
    }

    public sealed class ReachyCameraCalibrationSnapshot
    {
        private readonly ReachyCameraCalibrationProfile[] profiles;

        public ReachyCameraCalibrationSnapshot(
            IReadOnlyList<ReachyCameraCalibrationProfile> sourceProfiles,
            ulong revision)
        {
            if (sourceProfiles == null)
            {
                throw new ArgumentNullException(nameof(sourceProfiles));
            }
            profiles = new ReachyCameraCalibrationProfile[sourceProfiles.Count];
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            int calibratedCount = 0;
            for (int index = 0; index < sourceProfiles.Count; ++index)
            {
                ReachyCameraCalibrationProfile profile = sourceProfiles[index] ??
                    throw new ArgumentException(
                        "Calibration profile collections cannot contain null entries.",
                        nameof(sourceProfiles));
                if (!identifiers.Add(profile.ProfileId))
                {
                    throw new ArgumentException(
                        $"Calibration profile identifier '{profile.ProfileId}' is duplicated.",
                        nameof(sourceProfiles));
                }
                profiles[index] = profile;
                if (profile.IsCalibrated)
                {
                    ++calibratedCount;
                }
            }
            Revision = revision;
            CalibratedCount = calibratedCount;
        }

        public IReadOnlyList<ReachyCameraCalibrationProfile> Profiles =>
            Array.AsReadOnly(profiles);

        public ulong Revision { get; }

        public int CalibratedCount { get; }
    }

    public sealed class ReachyCameraCalibrationChangedEventArgs : EventArgs
    {
        public ReachyCameraCalibrationChangedEventArgs(
            ReachyCameraCalibrationSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyCameraCalibrationSnapshot Snapshot { get; }
    }

    public sealed class ReachyCameraCalibrationStateStore
    {
        private readonly object sync = new object();
        private ReachyCameraCalibrationSnapshot current =
            new ReachyCameraCalibrationSnapshot(
                Array.Empty<ReachyCameraCalibrationProfile>(),
                0UL);

        public ReachyCameraCalibrationSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public event EventHandler<ReachyCameraCalibrationChangedEventArgs>? Changed;

        public void ReplaceAll(
            IReadOnlyList<ReachyCameraCalibrationProfile> profiles)
        {
            if (profiles == null)
            {
                throw new ArgumentNullException(nameof(profiles));
            }
            Publish(new ReachyCameraCalibrationSnapshot(
                profiles,
                checked(Current.Revision + 1UL)));
        }

        public void Upsert(ReachyCameraCalibrationProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            ReachyCameraCalibrationSnapshot snapshot = Current;
            var next = new List<ReachyCameraCalibrationProfile>(
                snapshot.Profiles.Count + 1);
            bool replaced = false;
            for (int index = 0; index < snapshot.Profiles.Count; ++index)
            {
                ReachyCameraCalibrationProfile existing = snapshot.Profiles[index];
                if (string.Equals(
                        existing.ProfileId,
                        profile.ProfileId,
                        StringComparison.Ordinal))
                {
                    next.Add(profile);
                    replaced = true;
                }
                else
                {
                    next.Add(existing);
                }
            }
            if (!replaced)
            {
                next.Add(profile);
            }
            Publish(new ReachyCameraCalibrationSnapshot(
                next,
                checked(snapshot.Revision + 1UL)));
        }

        public bool Remove(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new ArgumentException(
                    "A calibration profile identifier is required.",
                    nameof(profileId));
            }
            ReachyCameraCalibrationSnapshot snapshot = Current;
            var next = new List<ReachyCameraCalibrationProfile>(
                snapshot.Profiles.Count);
            bool removed = false;
            for (int index = 0; index < snapshot.Profiles.Count; ++index)
            {
                ReachyCameraCalibrationProfile profile = snapshot.Profiles[index];
                if (string.Equals(
                        profile.ProfileId,
                        profileId,
                        StringComparison.Ordinal))
                {
                    removed = true;
                }
                else
                {
                    next.Add(profile);
                }
            }
            if (!removed)
            {
                return false;
            }
            Publish(new ReachyCameraCalibrationSnapshot(
                next,
                checked(snapshot.Revision + 1UL)));
            return true;
        }

        public ReachyCameraCalibrationSelectionResult SelectExact(
            string cameraId,
            ReachyDeviceCameraFacing facing,
            int phoneImageWidth,
            int phoneImageHeight,
            int reachyImageWidth,
            int reachyImageHeight,
            string modelCompatibility)
        {
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new ArgumentException(
                    "Calibration selection requires a camera identifier.",
                    nameof(cameraId));
            }
            if (phoneImageWidth <= 0 || phoneImageHeight <= 0 ||
                reachyImageWidth <= 0 || reachyImageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phoneImageWidth),
                    "Calibration selection dimensions must be positive.");
            }
            if (string.IsNullOrWhiteSpace(modelCompatibility))
            {
                throw new ArgumentException(
                    "Calibration selection requires a model compatibility key.",
                    nameof(modelCompatibility));
            }

            ReachyCameraCalibrationSnapshot snapshot = Current;
            bool cameraFound = false;
            bool sizeFound = false;
            bool modelFound = false;
            ReachyCameraCalibrationProfile? best = null;
            for (int index = 0; index < snapshot.Profiles.Count; ++index)
            {
                ReachyCameraCalibrationProfile candidate = snapshot.Profiles[index];
                if (!string.Equals(
                        candidate.CameraId,
                        cameraId,
                        StringComparison.Ordinal) ||
                    candidate.Facing != facing)
                {
                    continue;
                }
                cameraFound = true;
                if (candidate.PhoneIntrinsics.ImageWidth != phoneImageWidth ||
                    candidate.PhoneIntrinsics.ImageHeight != phoneImageHeight ||
                    candidate.ReachyIntrinsics.ImageWidth != reachyImageWidth ||
                    candidate.ReachyIntrinsics.ImageHeight != reachyImageHeight)
                {
                    continue;
                }
                sizeFound = true;
                if (!string.Equals(
                        candidate.ModelCompatibility,
                        modelCompatibility,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                modelFound = true;
                if (best == null || IsPreferred(candidate, best))
                {
                    best = candidate;
                }
            }

            if (best != null)
            {
                ReachyCameraCalibrationSelectionStatus status = best.IsCalibrated
                    ? ReachyCameraCalibrationSelectionStatus.ExactCalibrated
                    : ReachyCameraCalibrationSelectionStatus.ExactUncalibratedEstimate;
                return new ReachyCameraCalibrationSelectionResult(
                    status,
                    best,
                    best.IsCalibrated
                        ? $"Selected calibrated camera profile '{best.ProfileId}'."
                        : $"Selected explicitly uncalibrated estimate '{best.ProfileId}'.");
            }
            if (!cameraFound)
            {
                return new ReachyCameraCalibrationSelectionResult(
                    snapshot.Profiles.Count == 0
                        ? ReachyCameraCalibrationSelectionStatus.Missing
                        : ReachyCameraCalibrationSelectionStatus.CameraMismatch,
                    null,
                    snapshot.Profiles.Count == 0
                        ? "No camera calibration profiles are installed."
                        : $"No profile matches camera '{cameraId}' and facing '{facing}'.");
            }
            if (!sizeFound)
            {
                return new ReachyCameraCalibrationSelectionResult(
                    ReachyCameraCalibrationSelectionStatus.ImageSizeMismatch,
                    null,
                    "Camera calibration exists, but not for the requested phone and Reachy image dimensions.");
            }
            if (!modelFound)
            {
                return new ReachyCameraCalibrationSelectionResult(
                    ReachyCameraCalibrationSelectionStatus.ModelMismatch,
                    null,
                    $"Camera calibration exists, but not for model compatibility key '{modelCompatibility}'.");
            }
            throw new InvalidOperationException(
                "Calibration selection reached an inconsistent state.");
        }

        private static bool IsPreferred(
            ReachyCameraCalibrationProfile candidate,
            ReachyCameraCalibrationProfile currentBest)
        {
            if (candidate.IsCalibrated != currentBest.IsCalibrated)
            {
                return candidate.IsCalibrated;
            }
            int timeComparison = candidate.CreatedUtc.CompareTo(currentBest.CreatedUtc);
            if (timeComparison != 0)
            {
                return timeComparison > 0;
            }
            return string.CompareOrdinal(
                candidate.ProfileId,
                currentBest.ProfileId) < 0;
        }

        private void Publish(ReachyCameraCalibrationSnapshot snapshot)
        {
            EventHandler<ReachyCameraCalibrationChangedEventArgs>? handler;
            lock (sync)
            {
                current = snapshot;
                handler = Changed;
            }
            handler?.Invoke(
                this,
                new ReachyCameraCalibrationChangedEventArgs(snapshot));
        }
    }
}
