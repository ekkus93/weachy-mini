using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma111LightweightTrackingContracts
    {
        internal static async Task RunAsync()
        {
            await ProviderCapabilitiesRemainTruthfulAsync().ConfigureAwait(false);
            TrackingPixelsRequireExactColorAndValidityLengths();
            Rma111AndroidBridgeSourceContracts.Run();
            StableIdsSurviveMotionAndProviderIdDrift();
            ExpiryAndOrderingAreDeterministic();
            CameraContinuityResetDoesNotReuseIds();
            await InvalidCenterDetectionsAreRejectedAsync().ConfigureAwait(false);
            await CancellationDoesNotRetryOrInvokeVlmAsync().ConfigureAwait(false);
            await BackendFailureRemainsVisibleAsync().ConfigureAwait(false);
            await ConcurrentRequestsFailBusyWithoutQueueingAsync().ConfigureAwait(false);
            await TrackerBorrowsFrameWithoutDisposingItAsync().ConfigureAwait(false);
            Console.WriteLine("RMA-111 lightweight tracking contracts passed.");
        }

        private static async Task ProviderCapabilitiesRemainTruthfulAsync()
        {
            FakeBackend backend = FakeBackend.Immediate(
                Array.Empty<ReachyTrackingDetection>());
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-capability-test",
                backend);
            Equal(
                VisionProviderLocation.OnDevice,
                tracker.Descriptor.Location,
                "RMA-111 tracker locality");
            True(tracker.Capabilities.SupportsFaces, "face capability");
            True(tracker.Capabilities.SupportsPeople, "person capability");
            False(tracker.Capabilities.SupportsObjects, "object capability");
            False(tracker.Capabilities.SupportsMotion, "motion capability");
            Equal(1, tracker.Capabilities.MaximumConcurrentOperations, "concurrency");
            True(tracker.Capabilities.SupportsCancellation, "cancellation capability");
        }

        private static void TrackingPixelsRequireExactColorAndValidityLengths()
        {
            ReachyVisionFrameIdentity identity = Identity(1UL, 1_000_000_000L);
            Throws<ArgumentException>(
                () =>
                {
                    var pixels = new ReachyTrackingFramePixels(
                        identity,
                        2,
                        2,
                        new byte[15],
                        new byte[4]);
                    GC.KeepAlive(pixels);
                },
                "short RGBA buffer");
            Throws<ArgumentException>(
                () =>
                {
                    var pixels = new ReachyTrackingFramePixels(
                        identity,
                        2,
                        2,
                        new byte[16],
                        new byte[3]);
                    GC.KeepAlive(pixels);
                },
                "short validity buffer");
        }

        private static void StableIdsSurviveMotionAndProviderIdDrift()
        {
            var store = new ReachyStableTrackStore(
                new ReachyStableTrackingPolicy(
                    minimumIntersectionOverUnion: 0.15,
                    maximumCenterDistance: 0.20,
                    maximumProviderIdCenterDistance: 0.45,
                    maximumMissedFrames: 2,
                    maximumUnseenDuration: TimeSpan.FromSeconds(2.0)));
            IReadOnlyList<TrackedObject> first = store.Update(
                Identity(1UL, 1_000_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Face,
                        "native-7",
                        0.9,
                        0.20,
                        0.20,
                        0.25,
                        0.25),
                });
            IReadOnlyList<TrackedObject> second = store.Update(
                Identity(2UL, 1_050_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Face,
                        null,
                        0.8,
                        0.23,
                        0.21,
                        0.25,
                        0.25),
                });
            IReadOnlyList<TrackedObject> third = store.Update(
                Identity(3UL, 1_100_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Face,
                        "native-reassigned",
                        0.85,
                        0.25,
                        0.22,
                        0.25,
                        0.25),
                });

            Equal(first[0].LocalId, second[0].LocalId, "stable ID after native ID loss");
            Equal(first[0].LocalId, third[0].LocalId, "stable ID after native ID change");
            Equal("face-000001", first[0].LocalId, "deterministic face ID");
        }

        private static void ExpiryAndOrderingAreDeterministic()
        {
            var policy = new ReachyStableTrackingPolicy(
                minimumIntersectionOverUnion: 0.20,
                maximumCenterDistance: 0.18,
                maximumProviderIdCenterDistance: 0.45,
                maximumMissedFrames: 1,
                maximumUnseenDuration: TimeSpan.FromMilliseconds(100));
            var store = new ReachyStableTrackStore(policy);
            IReadOnlyList<TrackedObject> first = store.Update(
                Identity(1UL, 1_000_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Person,
                        null,
                        0.75,
                        0.1,
                        0.1,
                        0.5,
                        0.8),
                });
            store.Update(
                Identity(2UL, 1_050_000_000L),
                Array.Empty<ReachyTrackingDetection>());
            store.Update(
                Identity(3UL, 1_200_000_000L),
                Array.Empty<ReachyTrackingDetection>());
            IReadOnlyList<TrackedObject> replacement = store.Update(
                Identity(4UL, 1_250_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Person,
                        null,
                        0.80,
                        0.1,
                        0.1,
                        0.5,
                        0.8),
                });
            False(
                string.Equals(
                    first[0].LocalId,
                    replacement[0].LocalId,
                    StringComparison.Ordinal),
                "expired ID must not be reused");
            Throws<InvalidOperationException>(
                () => store.Update(
                    Identity(4UL, 1_260_000_000L),
                    Array.Empty<ReachyTrackingDetection>()),
                "duplicate source sequence");
        }


        private static void CameraContinuityResetDoesNotReuseIds()
        {
            var store = new ReachyStableTrackStore();
            IReadOnlyList<TrackedObject> first = store.Update(
                Identity(1UL, 1_000_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Face,
                        null,
                        1.0,
                        0.2,
                        0.2,
                        0.2,
                        0.2),
                });
            var newContinuity = new ReachyVisionFrameIdentity(
                "managed-rma111-camera",
                sourceSessionId: 2UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: 2_000_000_000L,
                authoritativeSequence: 1UL,
                continuityId: 2U);
            IReadOnlyList<TrackedObject> second = store.Update(
                newContinuity,
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Face,
                        null,
                        1.0,
                        0.2,
                        0.2,
                        0.2,
                        0.2),
                });
            False(
                string.Equals(
                    first[0].LocalId,
                    second[0].LocalId,
                    StringComparison.Ordinal),
                "continuity reset must not reuse local IDs");
        }

        private static async Task InvalidCenterDetectionsAreRejectedAsync()
        {
            ReachyTrackingDetection face = Detection(
                ReachyTrackingDetectionClass.Face,
                "face-1",
                1.0,
                0.25,
                0.25,
                0.50,
                0.50);
            FakeBackend backend = FakeBackend.Immediate(new[] { face });
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-invalid-center",
                backend);
            byte[] validity = AllValid(8, 8);
            validity[4 * 8 + 4] = 0;
            await using TestResources resources = TestResources.Create(
                Identity(1UL, 1_000_000_000L),
                8,
                8,
                validity);
            await using ReachyVisionFrame frame = Frame(resources);
            TrackingResult result = await TrackAsync(
                tracker,
                frame,
                "invalid-center").ConfigureAwait(false);
            True(result.Succeeded, result.Diagnostic);
            Equal(0, result.Objects.Count, "invalid-center filtered count");
            Equal(1, backend.InvocationCount, "single backend invocation");
        }

        private static async Task CancellationDoesNotRetryOrInvokeVlmAsync()
        {
            var backend = FakeBackend.Blocking();
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-cancel",
                backend);
            await using TestResources resources = TestResources.Create(
                Identity(1UL, 1_000_000_000L),
                8,
                8,
                AllValid(8, 8));
            await using ReachyVisionFrame frame = Frame(resources);
            var selection = new VisionProviderSelection(tracker.Descriptor);
            var context = new VisionRequestContext(
                "cancel-request",
                selection.Current,
                TimeSpan.FromSeconds(2.0));
            var request = new TrackingRequest(frame, context);
            using var cancellation = new CancellationTokenSource();
            Task<TrackingResult> pending = VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                cancellation.Token).AsTask();
            await backend.Started.Task.ConfigureAwait(false);
            cancellation.Cancel();
            TrackingResult result = await pending.ConfigureAwait(false);
            Equal(VisionOperationStatus.Cancelled, result.Status, "cancelled status");
            Equal(1, backend.InvocationCount, "no retry after cancellation");
            Equal(0, backend.VlmInvocationCount, "no VLM invocation");
        }

        private static async Task BackendFailureRemainsVisibleAsync()
        {
            var backend = FakeBackend.Failing(
                "synthetic backend fault");
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-failure",
                backend);
            await using TestResources resources = TestResources.Create(
                Identity(1UL, 1_000_000_000L),
                8,
                8,
                AllValid(8, 8));
            await using ReachyVisionFrame frame = Frame(resources);
            TrackingResult result = await TrackAsync(
                tracker,
                frame,
                "backend-failure").ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "backend failure status");
            True(result.RequiresProviderReset, "backend failure reset requirement");
            True(
                result.Diagnostic.Contains(
                    "synthetic backend fault",
                    StringComparison.Ordinal),
                "backend diagnostic visibility");
        }

        private static async Task ConcurrentRequestsFailBusyWithoutQueueingAsync()
        {
            var backend = FakeBackend.Blocking();
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-busy",
                backend);
            await using TestResources resources1 = TestResources.Create(
                Identity(1UL, 1_000_000_000L),
                8,
                8,
                AllValid(8, 8));
            await using ReachyVisionFrame frame1 = Frame(resources1);
            Task<TrackingResult> first = TrackAsync(
                tracker,
                frame1,
                "busy-first");
            await backend.Started.Task.ConfigureAwait(false);

            await using TestResources resources2 = TestResources.Create(
                Identity(2UL, 1_050_000_000L),
                8,
                8,
                AllValid(8, 8));
            await using ReachyVisionFrame frame2 = Frame(resources2);
            TrackingResult second = await TrackAsync(
                tracker,
                frame2,
                "busy-second").ConfigureAwait(false);
            Equal(VisionOperationStatus.Unavailable, second.Status, "busy status");
            True(
                second.Diagnostic.Contains("not queued", StringComparison.Ordinal),
                "busy queue diagnostic");
            Equal(1, backend.InvocationCount, "busy request not invoked");
            backend.Complete(Array.Empty<ReachyTrackingDetection>());
            TrackingResult firstResult = await first.ConfigureAwait(false);
            True(firstResult.Succeeded, firstResult.Diagnostic);
        }

        private static async Task TrackerBorrowsFrameWithoutDisposingItAsync()
        {
            FakeBackend backend = FakeBackend.Immediate(
                Array.Empty<ReachyTrackingDetection>());
            await using var tracker = new ReachyOnDeviceLightweightTracker(
                "managed-borrowed-frame",
                backend);
            await using TestResources resources = TestResources.Create(
                Identity(1UL, 1_000_000_000L),
                8,
                8,
                AllValid(8, 8));
            await using ReachyVisionFrame frame = Frame(resources);
            TrackingResult result = await TrackAsync(
                tracker,
                frame,
                "borrowed-frame").ConfigureAwait(false);
            True(result.Succeeded, result.Diagnostic);
            False(resources.IsDisposed, "tracker must not dispose borrowed frame resources");
        }

        private static Task<TrackingResult> TrackAsync(
            ReachyOnDeviceLightweightTracker tracker,
            ReachyVisionFrame frame,
            string requestId)
        {
            var selection = new VisionProviderSelection(tracker.Descriptor);
            var context = new VisionRequestContext(
                requestId,
                selection.Current,
                TimeSpan.FromSeconds(2.0));
            var request = new TrackingRequest(frame, context);
            return VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None).AsTask();
        }

        private static ReachyVisionFrame Frame(TestResources resources)
        {
            int total = checked(resources.Width * resources.Height);
            return new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                resources.Identity,
                new ReachyVisionCoverage(
                    VisionCoverageState.Normal,
                    total,
                    total,
                    hasValidityMask: true,
                    shouldStopVisionDrivenTurning: false,
                    "Synthetic RMA-111 coverage."),
                resources);
        }

        private static ReachyVisionFrameIdentity Identity(
            ulong sequence,
            long timestampNanoseconds)
        {
            return new ReachyVisionFrameIdentity(
                "managed-rma111-camera",
                sourceSessionId: 1UL,
                sourceSequence: sequence,
                sourceTimestampNanoseconds: timestampNanoseconds,
                authoritativeSequence: sequence,
                continuityId: 1U);
        }

        private static ReachyTrackingDetection Detection(
            ReachyTrackingDetectionClass detectionClass,
            string? providerTrackingId,
            double confidence,
            double left,
            double top,
            double width,
            double height)
        {
            return new ReachyTrackingDetection(
                detectionClass,
                providerTrackingId,
                confidence,
                new NormalizedVisionBounds(left, top, width, height));
        }

        private static byte[] AllValid(int width, int height)
        {
            var values = new byte[checked(width * height)];
            Array.Fill(values, (byte)255);
            return values;
        }

        private static void True(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    "Managed RMA-111 test failed: " + message);
            }
        }

        private static void False(bool value, string message)
        {
            True(!value, message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 test failed: {message}; " +
                    $"expected {expected}, actual {actual}.");
            }
        }

        private static void Throws<TException>(Action action, string message)
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
                $"Managed RMA-111 test failed: {message}; expected {typeof(TException).Name}.");
        }

        private sealed class TestResources :
            IReachyVisionFrameResources,
            IReachyTrackingPixelSource
        {
            private readonly ReachyTrackingFramePixels pixels;

            private TestResources(
                ReachyVisionFrameIdentity identity,
                int width,
                int height,
                byte[] validity)
            {
                Identity = identity;
                Width = width;
                Height = height;
                pixels = new ReachyTrackingFramePixels(
                    identity,
                    width,
                    height,
                    new byte[checked(width * height * 4)],
                    validity);
            }

            public ReachyVisionFrameIdentity Identity { get; }

            public string OwnerId => "managed-rma111";

            public ulong Generation => Identity.SourceSequence;

            public int Width { get; }

            public int Height { get; }

            public bool IsDisposed { get; private set; }

            public static TestResources Create(
                ReachyVisionFrameIdentity identity,
                int width,
                int height,
                byte[] validity)
            {
                return new TestResources(identity, width, height, validity);
            }

            public bool HasResource(VisionResourceKind kind)
            {
                return !IsDisposed &&
                    (kind == VisionResourceKind.Color ||
                     kind == VisionResourceKind.ValidityMask);
            }

            public VisionPixelEncoding GetEncoding(VisionResourceKind kind)
            {
                return kind == VisionResourceKind.Color
                    ? VisionPixelEncoding.Rgba8
                    : kind == VisionResourceKind.ValidityMask
                        ? VisionPixelEncoding.ValidityMask8
                        : throw new ArgumentOutOfRangeException(nameof(kind));
            }

            public bool TryGetResource<TResource>(
                VisionResourceKind kind,
                out TResource? resource)
                where TResource : class
            {
                if (!IsDisposed &&
                    kind == VisionResourceKind.Color &&
                    this is TResource source)
                {
                    resource = source;
                    return true;
                }
                resource = null;
                return false;
            }

            public ValueTask<ReachyTrackingFramePixels> StageAsync(
                ReachyVisionFrame frame,
                int maximumDimension,
                CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ReachyTrackingFramePixels>(pixels);
            }

            public ValueTask DisposeAsync()
            {
                IsDisposed = true;
                return default;
            }
        }

        private sealed class FakeBackend : IReachyTrackingDetectionBackend
        {
            private readonly IReadOnlyList<ReachyTrackingDetection>? immediate;
            private readonly string? failure;
            private readonly TaskCompletionSource<IReadOnlyList<ReachyTrackingDetection>>? gate;
            private bool disposed;

            private FakeBackend(
                IReadOnlyList<ReachyTrackingDetection>? immediate,
                string? failure,
                bool blocking)
            {
                this.immediate = immediate;
                this.failure = failure;
                if (blocking)
                {
                    gate = new TaskCompletionSource<IReadOnlyList<ReachyTrackingDetection>>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public string BackendId => "managed-fake-rma111";

            public string BackendVersion => "1";

            public bool SupportsFaces => true;

            public bool SupportsPeople => true;

            public int InvocationCount { get; private set; }

            public int VlmInvocationCount { get; }

            public TaskCompletionSource<bool> Started { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            public static FakeBackend Immediate(
                IReadOnlyList<ReachyTrackingDetection> detections)
            {
                return new FakeBackend(detections, null, blocking: false);
            }

            public static FakeBackend Blocking()
            {
                return new FakeBackend(null, null, blocking: true);
            }

            public static FakeBackend Failing(string message)
            {
                return new FakeBackend(null, message, blocking: false);
            }

            public async ValueTask<IReadOnlyList<ReachyTrackingDetection>> DetectAsync(
                ReachyTrackingFramePixels pixels,
                CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                InvocationCount++;
                Started.TrySetResult(true);
                if (failure != null)
                {
                    throw new InvalidOperationException(failure);
                }
                if (gate != null)
                {
                    using CancellationTokenRegistration registration =
                        cancellationToken.Register(
                            () => gate.TrySetCanceled(cancellationToken));
                    return await gate.Task.ConfigureAwait(false);
                }
                return immediate ?? Array.Empty<ReachyTrackingDetection>();
            }

            public void Complete(
                IReadOnlyList<ReachyTrackingDetection> detections)
            {
                gate?.TrySetResult(detections);
            }

            public ValueTask DisposeAsync()
            {
                disposed = true;
                gate?.TrySetCanceled();
                return default;
            }
        }
    }
}
