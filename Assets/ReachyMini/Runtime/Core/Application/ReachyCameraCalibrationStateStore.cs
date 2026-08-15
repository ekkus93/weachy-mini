#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
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
        public const int MaximumProfiles = 64;
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
            if (profiles.Count > MaximumProfiles)
            {
                throw new ArgumentException(
                    $"At most {MaximumProfiles} calibration profiles are supported.",
                    nameof(profiles));
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
                if (next.Count >= MaximumProfiles)
                {
                    throw new InvalidOperationException(
                        $"At most {MaximumProfiles} calibration profiles are supported.");
                }
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
