#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
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
