#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
    public sealed class WorldModelTrackingBatch
    {
        private readonly ReadOnlyCollection<TrackedObject> objects;

        public WorldModelTrackingBatch(
            string providerInstanceId,
            ReachyVisionFrameIdentity frameIdentity,
            ReachyVisionCoverage coverage,
            IReadOnlyList<TrackedObject> objects)
        {
            ProviderInstanceId = RequireBoundedText(
                providerInstanceId,
                nameof(providerInstanceId),
                256);
            FrameIdentity = frameIdentity ??
                throw new ArgumentNullException(nameof(frameIdentity));
            Coverage = coverage ??
                throw new ArgumentNullException(nameof(coverage));
            if (frameIdentity.CameraId.Length > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIdentity));
            }
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            if (objects.Count > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(objects));
            }

            var copy = new List<TrackedObject>(objects.Count);
            var localIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < objects.Count; ++index)
            {
                TrackedObject tracked = objects[index] ??
                    throw new ArgumentException(
                        "Tracking batches cannot contain null objects.",
                        nameof(objects));
                if (tracked.LocalId.Length > 256 ||
                    tracked.Classification.Length > 128)
                {
                    throw new ArgumentOutOfRangeException(nameof(objects));
                }
                if (!localIds.Add(tracked.LocalId))
                {
                    throw new ArgumentException(
                        "Tracking batches cannot contain duplicate local IDs.",
                        nameof(objects));
                }
                copy.Add(tracked);
            }
            this.objects = copy.AsReadOnly();
        }

        public string ProviderInstanceId { get; }

        public ReachyVisionFrameIdentity FrameIdentity { get; }

        public ReachyVisionCoverage Coverage { get; }

        public IReadOnlyList<TrackedObject> Objects => objects;

        public static WorldModelTrackingBatch FromTrackingResult(
            TrackingResult result,
            ReachyVisionCoverage coverage)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            if (!result.Succeeded)
            {
                throw new ArgumentException(
                    "Only successful tracking results can enter the world model.",
                    nameof(result));
            }
            return new WorldModelTrackingBatch(
                result.ProviderInstanceId,
                result.FrameIdentity,
                coverage,
                result.Objects);
        }

        private static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "World-model text cannot be empty.",
                    name);
            }
            if (value.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            return value;
        }
    }

    public sealed class WorldModelDescriptionUpdate
    {
        public WorldModelDescriptionUpdate(
            string entityId,
            string providerInstanceId,
            string text,
            ReachyVisionFrameIdentity sourceFrameIdentity,
            long appliedAtTimestampNanoseconds)
        {
            EntityId = RequireBoundedText(entityId, nameof(entityId), 256);
            ProviderInstanceId = RequireBoundedText(
                providerInstanceId,
                nameof(providerInstanceId),
                256);
            Text = RequireBoundedText(text, nameof(text), 8192);
            SourceFrameIdentity = sourceFrameIdentity ??
                throw new ArgumentNullException(nameof(sourceFrameIdentity));
            if (appliedAtTimestampNanoseconds <
                sourceFrameIdentity.SourceTimestampNanoseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(appliedAtTimestampNanoseconds));
            }
            AppliedAtTimestampNanoseconds =
                appliedAtTimestampNanoseconds;
        }

        public string EntityId { get; }

        public string ProviderInstanceId { get; }

        public string Text { get; }

        public ReachyVisionFrameIdentity SourceFrameIdentity { get; }

        public long AppliedAtTimestampNanoseconds { get; }

        private static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "World-model text cannot be empty.",
                    name);
            }
            if (value.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            return value;
        }
    }
}
