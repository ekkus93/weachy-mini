#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using ReachyMini.Perception;

namespace ReachyMini.WorldModel.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            StableTrackerIdsUpdateOneEntity();
            ContinuityResetCreatesANewGeneration();
            ExpiryOccursAtTheExactDeadline();
            CoverageRejectionIsVisibleAndDoesNotRefreshEntities();
            DegradedCoverageRemainsAttachedToObservations();
            DescriptionsAreDeduplicatedAndAged();
            DescriptionHistoryIsBoundedAndReportsDrops();
            SnapshotsAreDeeplyImmutable();
            EntityCapacityRejectsTheWholeBatch();
            LongRunningStreamsRemainBounded();
            SessionOrderingMemoryRemainsBounded();
            TwoDimensionalTrackingNeverFabricatesPosition();
            VisibilityDistinguishesCurrentRecentAndExpired();
            StaleAndConflictingInputsFailClosed();
            StaleFutureFramesAreNonMutating();
            RetainedScopesProtectOrderingCursors();
            SemanticResultsCannotCrossEntityGenerations();
            SourceContractRemainsExplicit();
            Console.WriteLine("RMA-112 bounded world-model contracts passed.");
            return 0;
        }

        private static void StableTrackerIdsUpdateOneEntity()
        {
            var model = CreateModel();
            WorldModelUpdateResult first = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 10UL,
                    objects: new[] { Tracked("face-1", "face", 0.7, 0.1) }));
            Equal(WorldModelUpdateStatus.Accepted, first.Status, "first status");

            WorldModelUpdateResult second = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 2UL,
                    authoritativeSequence: 11UL,
                    objects: new[] { Tracked("face-1", "face", 0.9, 0.2) }));
            Equal(WorldModelUpdateStatus.Accepted, second.Status, "second status");
            Equal(1, second.Snapshot.Entities.Count, "stable entity count");
            WorldEntitySnapshot entity = second.Snapshot.Entities[0];
            Equal(100L, entity.FirstSeenTimestampNanoseconds, "first seen");
            Equal(200L, entity.LastSeenTimestampNanoseconds, "last seen");
            Equal(2, entity.Observations.Count, "observation history");
            Equal(0.9, entity.Confidence, "updated confidence");
            True(entity.IsCurrentlyVisible, "stable entity visible");
        }

        private static void ContinuityResetCreatesANewGeneration()
        {
            var model = CreateModel();
            WorldModelSnapshot first = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot;
            string firstId = first.Entities[0].EntityId;

            WorldModelSnapshot reset = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    sourceSessionId: 2UL,
                    continuityId: 2U,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot;

            Equal(2, reset.Entities.Count, "generation count");
            Equal(1, reset.CurrentlyVisibleEntities.Count, "new generation visible");
            Equal(1, reset.RecentlySeenEntities.Count, "old generation recent");
            False(
                string.Equals(
                    firstId,
                    reset.CurrentlyVisibleEntities[0].EntityId,
                    StringComparison.Ordinal),
                "generation entity ID changes");
        }

        private static void ExpiryOccursAtTheExactDeadline()
        {
            var model = CreateModel(expiryNanoseconds: 100L);
            model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }));
            Equal(1, model.GetSnapshot(199L).Entities.Count, "before expiry");
            WorldModelSnapshot expired = model.GetSnapshot(200L);
            Equal(0, expired.Entities.Count, "at expiry");
            Equal(1L, expired.Diagnostics.ExpiredEntityCount, "expired counter");
        }

        private static void CoverageRejectionIsVisibleAndDoesNotRefreshEntities()
        {
            var model = CreateModel(expiryNanoseconds: 500L);
            model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }));

            WorldModelUpdateResult rejected = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 2UL,
                    authoritativeSequence: 2UL,
                    coverage: UnusableCoverage(),
                    objects: new[] { Tracked("face-1", "face", 0.9, 0.3) }));

            Equal(
                WorldModelUpdateStatus.InvalidCoverageRejected,
                rejected.Status,
                "coverage status");
            Equal(0, rejected.Snapshot.CurrentlyVisibleEntities.Count, "visible cleared");
            Equal(1, rejected.Snapshot.RecentlySeenEntities.Count, "entity retained recent");
            Equal(
                100L,
                rejected.Snapshot.Entities[0].LastSeenTimestampNanoseconds,
                "rejected frame does not refresh last seen");
            Equal(1L, rejected.Snapshot.Diagnostics.InvalidCoverageBatchCount, "coverage counter");
        }

        private static void DegradedCoverageRemainsAttachedToObservations()
        {
            var model = CreateModel();
            WorldEntitySnapshot entity = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    coverage: DegradedCoverage(),
                    objects: new[] { Tracked("person-1", "person", 0.75, 0.25) }))
                .Snapshot.Entities[0];

            Equal(VisionCoverageState.Degraded, entity.Coverage.State, "entity coverage");
            Equal(
                VisionCoverageState.Degraded,
                entity.Observations[0].Coverage.State,
                "observation coverage");
            Equal(0.5, entity.Coverage.ValidFraction, "coverage fraction");
        }

        private static void DescriptionsAreDeduplicatedAndAged()
        {
            var model = CreateModel();
            WorldEntitySnapshot entity = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot.Entities[0];
            ReachyVisionFrameIdentity source = entity.LatestFrameIdentity;

            WorldModelUpdateResult first = model.ApplyDescription(
                new WorldModelDescriptionUpdate(
                    entity.EntityId,
                    "vlm-local",
                    "Person wearing a blue shirt",
                    source,
                    150L));
            Equal(WorldModelUpdateStatus.DescriptionAccepted, first.Status, "description accepted");

            WorldModelUpdateResult duplicate = model.ApplyDescription(
                new WorldModelDescriptionUpdate(
                    entity.EntityId,
                    "vlm-cloud",
                    "  person   wearing a BLUE shirt  ",
                    source,
                    160L));
            Equal(WorldModelUpdateStatus.DescriptionDuplicate, duplicate.Status, "description duplicate");
            WorldEntitySnapshot described = duplicate.Snapshot.Entities[0];
            Equal(1, described.Descriptions.Count, "deduplicated count");
            Equal(2, described.Descriptions[0].ConfirmationCount, "confirmation count");
            Equal(
                "vlm-cloud",
                described.DescriptionProviderInstanceId,
                "latest confirming provider");
            Equal(
                "vlm-cloud",
                described.Descriptions[0].ProviderInstanceId,
                "description provenance matches source confirmation");
            Equal(0L, described.DescriptionAgeNanoseconds, "description age at confirmation");
            Equal(
                40L,
                model.GetSnapshot(200L).Entities[0].DescriptionAgeNanoseconds,
                "description age advances");
        }

        private static void DescriptionHistoryIsBoundedAndReportsDrops()
        {
            var model = CreateModel(maximumDescriptions: 2);
            WorldEntitySnapshot entity = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot.Entities[0];
            ReachyVisionFrameIdentity source = entity.LatestFrameIdentity;

            model.ApplyDescription(new WorldModelDescriptionUpdate(
                entity.EntityId, "vlm", "first", source, 110L));
            model.ApplyDescription(new WorldModelDescriptionUpdate(
                entity.EntityId, "vlm", "second", source, 120L));
            WorldEntitySnapshot bounded = model.ApplyDescription(
                new WorldModelDescriptionUpdate(
                    entity.EntityId, "vlm", "third", source, 130L))
                .Snapshot.Entities[0];

            Equal(2, bounded.Descriptions.Count, "bounded description count");
            Equal("second", bounded.Descriptions[0].Text, "oldest retained description");
            Equal("third", bounded.Description, "latest description");
            Equal(1L, bounded.DroppedDescriptionCount, "description drop count");
        }

        private static void SnapshotsAreDeeplyImmutable()
        {
            var model = CreateModel();
            WorldModelSnapshot oldSnapshot = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }))
                .Snapshot;

            Throws<NotSupportedException>(
                () => ((IList<WorldEntitySnapshot>)oldSnapshot.Entities).Add(
                    oldSnapshot.Entities[0]),
                "entity collection immutable");
            Throws<NotSupportedException>(
                () => ((IList<WorldObservationSnapshot>)oldSnapshot.Entities[0].Observations).Clear(),
                "observation collection immutable");

            model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 2UL,
                    authoritativeSequence: 2UL,
                    objects: new[] { Tracked("face-1", "face", 0.9, 0.3) }));
            Equal(1, oldSnapshot.Entities[0].Observations.Count, "old snapshot unchanged");
            Equal(100L, oldSnapshot.Entities[0].LastSeenTimestampNanoseconds, "old timestamp unchanged");
        }

        private static void EntityCapacityRejectsTheWholeBatch()
        {
            var model = CreateModel(maximumEntities: 1);
            WorldModelUpdateResult result = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[]
                    {
                        Tracked("face-1", "face", 0.8, 0.1),
                        Tracked("face-2", "face", 0.8, 0.6),
                    }));

            Equal(WorldModelUpdateStatus.CapacityExceeded, result.Status, "capacity status");
            Equal(0, result.Snapshot.Entities.Count, "capacity rejection atomic");
            Equal(1L, result.Snapshot.Diagnostics.CapacityRejectedBatchCount, "capacity counter");
        }

        private static void LongRunningStreamsRemainBounded()
        {
            var model = CreateModel(maximumObservations: 4, expiryNanoseconds: 10_000L);
            for (int index = 1; index <= 100; ++index)
            {
                model.ApplyTracking(
                    Batch(
                        timestamp: index,
                        sourceSequence: (ulong)index,
                        authoritativeSequence: (ulong)index,
                        objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }));
            }

            WorldEntitySnapshot entity = model.GetSnapshot(100L).Entities[0];
            Equal(1, model.GetSnapshot(100L).Entities.Count, "bounded entity count");
            Equal(4, entity.Observations.Count, "bounded observation count");
            Equal(96L, entity.DroppedObservationCount, "observation drop count");
            Equal(97L, entity.Observations[0].TimestampNanoseconds, "oldest retained observation");
        }

        private static void SessionOrderingMemoryRemainsBounded()
        {
            var model = CreateModel(
                maximumEntities: 3,
                expiryNanoseconds: 10_000L,
                maximumScopeCursors: 3);
            for (int index = 1; index <= 5; ++index)
            {
                model.ApplyTracking(
                    Batch(
                        timestamp: index,
                        sourceSequence: 1UL,
                        authoritativeSequence: 1UL,
                        sourceSessionId: (ulong)index,
                        continuityId: (uint)index,
                        objects: Array.Empty<TrackedObject>()));
            }

            WorldModelDiagnosticsSnapshot diagnostics =
                model.GetSnapshot(5L).Diagnostics;
            Equal(3, diagnostics.ActiveScopeCursorCount, "cursor bound");
            Equal(2L, diagnostics.DroppedScopeCursorCount, "cursor drop count");
        }

        private static void TwoDimensionalTrackingNeverFabricatesPosition()
        {
            var model = CreateModel();
            WorldEntitySnapshot entity = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }))
                .Snapshot.Entities[0];

            False(entity.Position.IsKnown, "position unknown");
            Equal(null, entity.Position.XMeters, "x absent");
            Equal("unavailable_from_2d_tracking", entity.Position.Method, "position method");
            Equal(
                "normalized_image_ray_without_metric_intrinsics",
                entity.Direction.Method,
                "direction method");
            Equal(
                1.0,
                Math.Sqrt(
                    (entity.Direction.X * entity.Direction.X) +
                    (entity.Direction.Y * entity.Direction.Y) +
                    (entity.Direction.Z * entity.Direction.Z)),
                "normalized direction",
                tolerance: 1e-12);
        }

        private static void VisibilityDistinguishesCurrentRecentAndExpired()
        {
            var model = CreateModel(expiryNanoseconds: 1_000L);
            model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[]
                    {
                        Tracked("person-1", "person", 0.8, 0.1),
                        Tracked("person-2", "person", 0.8, 0.6),
                    }));
            WorldModelSnapshot second = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 2UL,
                    authoritativeSequence: 2UL,
                    objects: new[] { Tracked("person-1", "person", 0.9, 0.2) }))
                .Snapshot;

            Equal(1, second.CurrentlyVisibleEntities.Count, "current count");
            Equal(1, second.RecentlySeenEntities.Count, "recent count");
            Equal("person-1", second.CurrentlyVisibleEntities[0].TrackingLocalId, "current ID");
            Equal("person-2", second.RecentlySeenEntities[0].TrackingLocalId, "recent ID");

            WorldModelSnapshot partlyExpired = model.GetSnapshot(1_100L);
            Equal(1, partlyExpired.Entities.Count, "older recent entity expired");
            Equal("person-1", partlyExpired.Entities[0].TrackingLocalId, "newer entity retained");
            Equal(0, model.GetSnapshot(1_200L).Entities.Count, "all expired");
        }

        private static void StaleAndConflictingInputsFailClosed()
        {
            var model = CreateModel();
            WorldModelTrackingBatch first = Batch(
                timestamp: 100L,
                sourceSequence: 1UL,
                authoritativeSequence: 1UL,
                objects: new[] { Tracked("face-1", "face", 0.8, 0.2) });
            model.ApplyTracking(first);
            Equal(
                WorldModelUpdateStatus.DuplicateIgnored,
                model.ApplyTracking(first).Status,
                "exact duplicate");

            Equal(
                WorldModelUpdateStatus.StaleRejected,
                model.ApplyTracking(
                    Batch(
                        timestamp: 90L,
                        sourceSequence: 2UL,
                        authoritativeSequence: 2UL,
                        objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }))
                    .Status,
                "stale result");

            WorldModelUpdateResult conflict = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 2UL,
                    authoritativeSequence: 2UL,
                    objects: new[] { Tracked("face-1", "person", 0.8, 0.2) }));
            Equal(WorldModelUpdateStatus.ClassificationConflict, conflict.Status, "classification conflict");
            Equal(0, conflict.Snapshot.CurrentlyVisibleEntities.Count, "conflict clears visibility");
            Equal(1L, conflict.Snapshot.Diagnostics.ClassificationConflictCount, "conflict counter");
        }

        private static void StaleFutureFramesAreNonMutating()
        {
            var model = CreateModel(expiryNanoseconds: 100L);
            model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 5UL,
                    authoritativeSequence: 5UL,
                    objects: new[] { Tracked("face-1", "face", 0.8, 0.2) }));

            WorldModelUpdateResult rejected = model.ApplyTracking(
                Batch(
                    timestamp: 250L,
                    sourceSequence: 4UL,
                    authoritativeSequence: 6UL,
                    objects: new[] { Tracked("face-1", "face", 0.9, 0.3) }));

            Equal(WorldModelUpdateStatus.StaleRejected, rejected.Status, "future stale status");
            Equal(1, rejected.Snapshot.Entities.Count, "future stale retains entity");
            Equal(100L, rejected.Snapshot.Entities[0].LastSeenTimestampNanoseconds, "future stale does not refresh");
            True(rejected.Snapshot.Entities[0].IsCurrentlyVisible, "future stale does not alter visibility");
            Equal(0L, rejected.Snapshot.Diagnostics.ExpiredEntityCount, "future stale does not expire");
            Equal(1, model.GetSnapshot(199L).Entities.Count, "future stale does not advance clock");
        }

        private static void RetainedScopesProtectOrderingCursors()
        {
            var model = CreateModel(
                maximumEntities: 2,
                expiryNanoseconds: 10_000L,
                maximumScopeCursors: 2);
            model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 5UL,
                    authoritativeSequence: 5UL,
                    sourceSessionId: 1UL,
                    continuityId: 1U,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.1) }));
            model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 5UL,
                    authoritativeSequence: 5UL,
                    sourceSessionId: 2UL,
                    continuityId: 2U,
                    objects: new[] { Tracked("person-2", "person", 0.8, 0.6) }));

            WorldModelUpdateResult full = model.ApplyTracking(
                Batch(
                    timestamp: 300L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    sourceSessionId: 3UL,
                    continuityId: 3U,
                    objects: Array.Empty<TrackedObject>()));
            Equal(WorldModelUpdateStatus.CapacityExceeded, full.Status, "retained cursor capacity status");
            Equal(2, full.Snapshot.Diagnostics.ActiveScopeCursorCount, "retained cursor count");
            Equal(2, full.Snapshot.Entities.Count, "retained entities unchanged");
            Equal(1, full.Snapshot.CurrentlyVisibleEntities.Count, "retained visibility unchanged");
            Equal("person-2", full.Snapshot.CurrentlyVisibleEntities[0].TrackingLocalId, "latest generation remains visible");

            WorldModelUpdateResult stale = model.ApplyTracking(
                Batch(
                    timestamp: 400L,
                    sourceSequence: 4UL,
                    authoritativeSequence: 6UL,
                    sourceSessionId: 1UL,
                    continuityId: 1U,
                    objects: new[] { Tracked("person-1", "person", 0.9, 0.2) }));
            Equal(WorldModelUpdateStatus.StaleRejected, stale.Status, "retained cursor rejects regression");
            Equal(100L, stale.Snapshot.Entities[0].LastSeenTimestampNanoseconds, "regression did not refresh entity");
            Equal(2, stale.Snapshot.Diagnostics.ActiveScopeCursorCount, "regression preserves cursors");
        }

        private static void SemanticResultsCannotCrossEntityGenerations()
        {
            var model = CreateModel(expiryNanoseconds: 1_000L);
            WorldEntitySnapshot first = model.ApplyTracking(
                Batch(
                    timestamp: 100L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot.Entities[0];
            WorldEntitySnapshot second = model.ApplyTracking(
                Batch(
                    timestamp: 200L,
                    sourceSequence: 1UL,
                    authoritativeSequence: 1UL,
                    sourceSessionId: 2UL,
                    continuityId: 2U,
                    objects: new[] { Tracked("person-1", "person", 0.8, 0.2) }))
                .Snapshot.CurrentlyVisibleEntities[0];

            WorldModelUpdateResult rejected = model.ApplyDescription(
                new WorldModelDescriptionUpdate(
                    second.EntityId,
                    "vlm",
                    "wrong generation",
                    first.LatestFrameIdentity,
                    250L));
            Equal(WorldModelUpdateStatus.DescriptionRejected, rejected.Status, "cross-generation rejection");
            Equal(0, rejected.Snapshot.CurrentlyVisibleEntities[0].Descriptions.Count, "no semantic contamination");
        }

        private static void SourceContractRemainsExplicit()
        {
            // ReachyBoundedWorldModel.cs was split (docs/LARGE_FILE_REFACTOR_TODO.md) into
            // ReachyBoundedWorldModel*.cs (the partial-class pieces) and ReachyWorldModel*.cs
            // (the extracted DTO/value types). Concatenate every split-out piece so this
            // contract check keeps covering the same source, regardless of which file each
            // token now lives in.
            const string directory = "Assets/ReachyMini/Runtime/Core/Perception";
            var sourceFiles = new List<string>();
            sourceFiles.AddRange(Directory.GetFiles(directory, "ReachyBoundedWorldModel*.cs"));
            sourceFiles.AddRange(Directory.GetFiles(directory, "ReachyWorldModel*.cs"));
            var builder = new System.Text.StringBuilder();
            foreach (string sourceFile in sourceFiles)
            {
                builder.Append(File.ReadAllText(sourceFile));
            }
            string source = builder.ToString();
            Contains(source, "unavailable_from_2d_tracking", "unknown position contract");
            Contains(source, "MaximumEntities", "entity bound contract");
            Contains(source, "MaximumScopeCursors", "cursor bound contract");
            Contains(source, "TrySelectCursorEviction", "retained cursor preflight contract");
            Contains(source, "HasEntityRetainedForScopeAt", "cursor/entity retention contract");
            Contains(source, "CommitCursor", "cursor commit contract");
            Contains(source, "DescriptionDuplicate", "description dedup contract");
            Contains(source, "InvalidCoverageRejected", "coverage rejection contract");
            Contains(source, "ExpireInternal", "expiry contract");
            DoesNotContain(source, "silent fallback", "no silent fallback text");
            DoesNotContain(source, "catch (", "no swallowed exception path");
        }

        private static BoundedWorldModel CreateModel(
            int maximumEntities = 8,
            int maximumObservations = 8,
            int maximumDescriptions = 4,
            long expiryNanoseconds = 1_000L,
            int maximumScopeCursors = 8)
        {
            return new BoundedWorldModel(
                new WorldModelPolicy(
                    maximumEntities,
                    maximumObservations,
                    maximumDescriptions,
                    maximumDescriptionCharacters: 128,
                    entityExpiryNanoseconds: expiryNanoseconds,
                    maximumScopeCursors: maximumScopeCursors));
        }

        private static WorldModelTrackingBatch Batch(
            long timestamp,
            ulong sourceSequence,
            ulong authoritativeSequence,
            IReadOnlyList<TrackedObject> objects,
            ReachyVisionCoverage? coverage = null,
            ulong sourceSessionId = 1UL,
            uint continuityId = 1U)
        {
            return new WorldModelTrackingBatch(
                "tracker-instance",
                new ReachyVisionFrameIdentity(
                    "rear-0",
                    sourceSessionId,
                    sourceSequence,
                    timestamp,
                    authoritativeSequence,
                    continuityId),
                coverage ?? NormalCoverage(),
                objects);
        }

        private static TrackedObject Tracked(
            string localId,
            string classification,
            double confidence,
            double left)
        {
            return new TrackedObject(
                localId,
                classification,
                confidence,
                new NormalizedVisionBounds(left, 0.2, 0.2, 0.4));
        }

        private static ReachyVisionCoverage NormalCoverage()
        {
            return new ReachyVisionCoverage(
                VisionCoverageState.Normal,
                validPixelCount: 90L,
                totalPixelCount: 100L,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning: false,
                diagnostic: "normal coverage");
        }

        private static ReachyVisionCoverage DegradedCoverage()
        {
            return new ReachyVisionCoverage(
                VisionCoverageState.Degraded,
                validPixelCount: 50L,
                totalPixelCount: 100L,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning: false,
                diagnostic: "degraded coverage");
        }

        private static ReachyVisionCoverage UnusableCoverage()
        {
            return new ReachyVisionCoverage(
                VisionCoverageState.Unusable,
                validPixelCount: 20L,
                totalPixelCount: 100L,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning: true,
                diagnostic: "unusable coverage");
        }

        private static void True(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name + " expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                throw new InvalidOperationException(name + " expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected=" + expected + " actual=" + actual + ".");
            }
        }

        private static void Equal(
            double expected,
            double actual,
            string name,
            double tolerance = 0.0)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    name + " expected=" + expected + " actual=" + actual + ".");
            }
        }

        private static void Contains(string value, string expected, string name)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " missing '" + expected + "'.");
            }
        }

        private static void DoesNotContain(string value, string unexpected, string name)
        {
            if (value.Contains(unexpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " unexpectedly contained '" + unexpected + "'.");
            }
        }

        private static void Throws<TException>(Action action, string name)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                name + " expected " + typeof(TException).Name + ".");
        }
    }
}
