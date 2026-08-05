#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma110VisionProviderContracts
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            RunAsync().GetAwaiter().GetResult();
        }

        private static async Task RunAsync()
        {
            ProviderKindsAndCapabilitiesRemainExplicit();
            TransformedFramesRequireOwnedColorValidityAndCoverage();
            await FrameSourceRejectsRawFallbackAndStaleSequenceAsync()
                .ConfigureAwait(false);
            await CallerCancellationReturnsTypedFailureAsync()
                .ConfigureAwait(false);
            await TimeoutQuarantinesProviderAsync().ConfigureAwait(false);
            await ProviderFaultRemainsVisibleAsync().ConfigureAwait(false);
            await ProviderSwitchSupersedesLateResultsAsync()
                .ConfigureAwait(false);
            await ResultIdentityMismatchFailsClosedAsync()
                .ConfigureAwait(false);
            await CloudDisclosureIsRequiredBeforeInvocationAsync()
                .ConfigureAwait(false);
            await FrameResourcesDisposeExactlyOnceAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 vision provider contracts passed.");
        }

        private static void ProviderKindsAndCapabilitiesRemainExplicit()
        {
            ProviderDescriptor source = Descriptor(
                VisionProviderKind.FrameSource,
                "source-1",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor tracker = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-1",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor vlm = Descriptor(
                VisionProviderKind.SemanticVisionLanguage,
                "vlm-1",
                VisionProviderLocation.Cloud);

            Equal(VisionProviderKind.FrameSource, source.Kind, "source kind");
            Equal(
                VisionProviderKind.LightweightTracker,
                tracker.Kind,
                "tracker kind");
            Equal(
                VisionProviderKind.SemanticVisionLanguage,
                vlm.Kind,
                "VLM kind");
            True(vlm.RequiresNetworkDisclosure, "cloud disclosure");
            False(source.RequiresNetworkDisclosure, "on-device disclosure");

            _ = new FrameSourceCapabilities(
                supportsGpuColor: true,
                supportsGpuValidityMask: true,
                supportsCancellation: true,
                maximumWidth: 4096,
                maximumHeight: 4096,
                maximumOutstandingFrames: 2);
            _ = TrackerCapability();
            _ = VisionLanguageCapability();

            Throws<ArgumentException>(
                () =>
                {
                    _ = new TrackerCapabilities(
                        supportsFaces: false,
                        supportsPeople: false,
                        supportsObjects: false,
                        supportsMotion: false,
                        consumesGpuFrames: true,
                        supportsCancellation: true,
                        maximumConcurrentOperations: 1);
                },
                "empty tracker capability set");
            Throws<ArgumentException>(
                () =>
                {
                    _ = new FrameSourceCapabilities(
                        supportsGpuColor: true,
                        supportsGpuValidityMask: false,
                        supportsCancellation: true,
                        maximumWidth: 640,
                        maximumHeight: 480,
                        maximumOutstandingFrames: 1);
                },
                "missing validity-mask capability");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new VisionRequestContext(
                        "bad-timeout",
                        new VisionProviderSelection(tracker).Current,
                        TimeSpan.Zero);
                },
                "zero timeout");
        }

        private static void TransformedFramesRequireOwnedColorValidityAndCoverage()
        {
            var resources = new FakeResources(
                width: 10,
                height: 10,
                hasValidity: true);
            ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 1UL);
            True(frame.IsObservationEligible, "transformed frame eligibility");
            Equal(0.8, frame.Coverage.Fraction, "coverage fraction");
            True(
                frame.Resources.HasResource(VisionResourceKind.Color),
                "color resource");
            True(
                frame.Resources.HasResource(VisionResourceKind.ValidityMask),
                "validity resource");

            Throws<ArgumentException>(
                () =>
                {
                    _ = Frame(
                        new FakeResources(10, 10, hasValidity: false),
                        VisionFrameOrigin.TransformedReachyEye,
                        VisionCoverageState.Normal,
                        sourceSequence: 2UL);
                },
                "transformed frame without validity resource");
            Throws<ArgumentException>(
                () =>
                {
                    _ = new ReachyVisionCoverage(
                        VisionCoverageState.Unusable,
                        10L,
                        100L,
                        hasValidityMask: true,
                        shouldStopVisionDrivenTurning: false,
                        diagnostic: "unsafe unusable coverage");
                },
                "unusable coverage without turning stop");
        }

        private static async Task FrameSourceRejectsRawFallbackAndStaleSequenceAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.FrameSource,
                "source-raw-fallback",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            VisionRequestContext context = Context(
                "frame-source-1",
                selection,
                TimeSpan.FromSeconds(1.0));
            var rawResources = new FakeResources(
                width: 10,
                height: 10,
                hasValidity: false);
            ReachyVisionFrame raw = RawFrame(rawResources, 5UL);
            var source = new FakeFrameSource(
                descriptor,
                (_, _) => new ValueTask<FrameSourceResult>(
                    FrameSourceResult.Success(
                        descriptor,
                        context,
                        raw)));
            var normalRequest = new FrameSourceRequest(
                VisionFramePurpose.Tracking,
                context,
                minimumSourceSequence: 1UL);

            FrameSourceResult normal = await VisionProviderExecutor
                .AcquireFrameAsync(
                    source,
                    normalRequest,
                    selection,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Equal(
                VisionOperationStatus.InvalidFrame,
                normal.Status,
                "raw fallback rejection");
            Equal(1, rawResources.DisposeCount, "rejected raw disposal");

            VisionRequestContext staleContext = Context(
                "frame-source-2",
                selection,
                TimeSpan.FromSeconds(1.0));
            var staleResources = new FakeResources(10, 10, hasValidity: true);
            ReachyVisionFrame staleFrame = Frame(
                staleResources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 3UL);
            source.Handler = (_, _) => new ValueTask<FrameSourceResult>(
                FrameSourceResult.Success(
                    descriptor,
                    staleContext,
                    staleFrame));
            var staleRequest = new FrameSourceRequest(
                VisionFramePurpose.Tracking,
                staleContext,
                minimumSourceSequence: 4UL);
            FrameSourceResult stale = await VisionProviderExecutor
                .AcquireFrameAsync(
                    source,
                    staleRequest,
                    selection,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Equal(
                VisionOperationStatus.InvalidFrame,
                stale.Status,
                "stale sequence rejection");
            Equal(1, staleResources.DisposeCount, "stale frame disposal");
        }

        private static async Task CallerCancellationReturnsTypedFailureAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-cancel",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 10UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-cancel",
                    selection,
                    TimeSpan.FromSeconds(2.0)));
            var tracker = new FakeTracker(
                descriptor,
                WaitForTrackerCancellationAsync);
            using var cancellation = new CancellationTokenSource();
            Task<TrackingResult> pending = VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                cancellation.Token).AsTask();
            cancellation.Cancel();
            TrackingResult result = await pending.ConfigureAwait(false);

            Equal(
                VisionOperationStatus.Cancelled,
                result.Status,
                "caller cancellation status");
            False(result.RequiresProviderReset, "cancel does not quarantine");
            Equal(0, result.Objects.Count, "cancelled payload is empty");
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task TimeoutQuarantinesProviderAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-timeout",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 11UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-timeout",
                    selection,
                    TimeSpan.FromMilliseconds(50.0)));
            var completion = new TaskCompletionSource<TrackingResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var tracker = new FakeTracker(
                descriptor,
                (_, _) => new ValueTask<TrackingResult>(completion.Task));

            TrackingResult result = await VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.TimedOut,
                result.Status,
                "timeout status");
            True(result.RequiresProviderReset, "timeout quarantines provider");
            completion.SetResult(
                TrackingResult.Success(
                    descriptor,
                    request,
                    Array.Empty<TrackedObject>()));
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task ProviderFaultRemainsVisibleAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-fault",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 12UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-fault",
                    selection,
                    TimeSpan.FromSeconds(1.0)));
            var tracker = new FakeTracker(
                descriptor,
                ThrowTrackerFailure);

            TrackingResult result = await VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "provider failure status");
            True(result.RequiresProviderReset, "fault quarantines provider");
            Contains(
                result.Diagnostic,
                nameof(InvalidOperationException),
                "provider failure diagnostic");
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task ProviderSwitchSupersedesLateResultsAsync()
        {
            ProviderDescriptor first = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-old",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor replacement = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-new",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(first);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 13UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-switch",
                    selection,
                    TimeSpan.FromSeconds(1.0)));
            var completion = new TaskCompletionSource<TrackingResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var tracker = new FakeTracker(
                first,
                (_, _) => new ValueTask<TrackingResult>(completion.Task));

            Task<TrackingResult> pending = VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None).AsTask();
            selection.Select(replacement);
            completion.SetResult(
                TrackingResult.Success(
                    first,
                    request,
                    new[]
                    {
                        new TrackedObject(
                            "old-result",
                            "person",
                            0.9,
                            new NormalizedVisionBounds(
                                0.1,
                                0.1,
                                0.2,
                                0.2)),
                    }));
            TrackingResult result = await pending.ConfigureAwait(false);

            Equal(
                VisionOperationStatus.Superseded,
                result.Status,
                "provider-switch result");
            Equal(0, result.Objects.Count, "superseded payload discarded");
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task ResultIdentityMismatchFailsClosedAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-identity",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor wrong = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-wrong",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Degraded,
                sourceSequence: 14UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-identity",
                    selection,
                    TimeSpan.FromSeconds(1.0)));
            var tracker = new FakeTracker(
                descriptor,
                (_, _) => new ValueTask<TrackingResult>(
                    TrackingResult.Success(
                        wrong,
                        request,
                        Array.Empty<TrackedObject>())));

            TrackingResult result = await VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "identity mismatch status");
            True(
                result.RequiresProviderReset,
                "identity mismatch quarantines provider");
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task CloudDisclosureIsRequiredBeforeInvocationAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.SemanticVisionLanguage,
                "cloud-vlm",
                VisionProviderLocation.Cloud);
            var selection = new VisionProviderSelection(descriptor);
            ReachyVisionFrame frame = Frame(
                new FakeResources(10, 10, hasValidity: true),
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 15UL);
            VisionRequestContext context = Context(
                "vlm-disclosure",
                selection,
                TimeSpan.FromSeconds(1.0));
            var request = new VisionLanguageRequest(
                frame,
                "What is visible?",
                context,
                networkDisclosureAcknowledged: false);
            var provider = new FakeVisionLanguageProvider(
                descriptor,
                (_, _) => new ValueTask<VisionLanguageResult>(
                    VisionLanguageResult.Success(
                        descriptor,
                        request,
                        "A person is visible.")));

            VisionLanguageResult result = await VisionProviderExecutor
                .AnalyzeVisionLanguageAsync(
                    provider,
                    request,
                    selection,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Unavailable,
                result.Status,
                "network disclosure status");
            Equal(0, provider.CallCount, "cloud provider not invoked");
            Equal(null, result.Text, "no undisclosed cloud result");
            await frame.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task FrameResourcesDisposeExactlyOnceAsync()
        {
            var resources = new FakeResources(10, 10, hasValidity: true);
            ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 16UL);
            await frame.DisposeAsync().ConfigureAwait(false);
            await frame.DisposeAsync().ConfigureAwait(false);
            Equal(1, resources.DisposeCount, "resource dispose count");
            True(frame.IsDisposed, "frame disposed state");
            False(frame.IsObservationEligible, "disposed frame eligibility");
        }

        private static ProviderDescriptor Descriptor(
            VisionProviderKind kind,
            string instanceId,
            VisionProviderLocation location)
        {
            return new ProviderDescriptor(
                kind,
                providerId: "provider." + kind,
                instanceId: instanceId,
                displayName: instanceId,
                version: "1.0.0",
                location: location);
        }

        private static TrackerCapabilities TrackerCapability()
        {
            return new TrackerCapabilities(
                supportsFaces: true,
                supportsPeople: true,
                supportsObjects: false,
                supportsMotion: false,
                consumesGpuFrames: true,
                supportsCancellation: true,
                maximumConcurrentOperations: 1);
        }

        private static VisionLanguageCapabilities VisionLanguageCapability()
        {
            return new VisionLanguageCapabilities(
                supportsVisualQuestions: true,
                supportsSceneDescription: true,
                supportsCancellation: true,
                maximumConcurrentOperations: 1,
                maximumPromptCharacters: 4096);
        }

        private static VisionRequestContext Context(
            string requestId,
            VisionProviderSelection selection,
            TimeSpan timeout)
        {
            return new VisionRequestContext(
                requestId,
                selection.Current,
                timeout);
        }

        private static ReachyVisionFrame Frame(
            FakeResources resources,
            VisionFrameOrigin origin,
            VisionCoverageState state,
            ulong sourceSequence)
        {
            long total = checked((long)resources.Width * resources.Height);
            return new ReachyVisionFrame(
                origin,
                new ReachyVisionFrameIdentity(
                    "rear-0",
                    sourceSessionId: 41UL,
                    sourceSequence: sourceSequence,
                    sourceTimestampNanoseconds:
                        checked((long)sourceSequence * 1_000_000L),
                    authoritativeSequence: sourceSequence + 100UL,
                    continuityId: 2U),
                new ReachyVisionCoverage(
                    state,
                    validPixelCount: state == VisionCoverageState.Degraded
                        ? total / 2L
                        : (total * 8L) / 10L,
                    totalPixelCount: total,
                    hasValidityMask: true,
                    shouldStopVisionDrivenTurning:
                        state != VisionCoverageState.Normal,
                    diagnostic: "explicit test coverage"),
                resources);
        }

        private static ReachyVisionFrame RawFrame(
            FakeResources resources,
            ulong sourceSequence)
        {
            return new ReachyVisionFrame(
                VisionFrameOrigin.RawPhoneDebug,
                new ReachyVisionFrameIdentity(
                    "rear-0",
                    sourceSessionId: 41UL,
                    sourceSequence: sourceSequence,
                    sourceTimestampNanoseconds:
                        checked((long)sourceSequence * 1_000_000L),
                    authoritativeSequence: sourceSequence + 100UL,
                    continuityId: 2U),
                new ReachyVisionCoverage(
                    VisionCoverageState.Unavailable,
                    validPixelCount: 0L,
                    totalPixelCount: 0L,
                    hasValidityMask: false,
                    shouldStopVisionDrivenTurning: true,
                    diagnostic: "raw debug has no transformed coverage"),
                resources);
        }

        private static async ValueTask<TrackingResult>
            WaitForTrackerCancellationAsync(
                TrackingRequest request,
                CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    static state =>
                    {
                        var source =
                            (TaskCompletionSource<bool>)state!;
                        source.TrySetResult(true);
                    },
                    completion);
            await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return TrackingResult.Success(
                Descriptor(
                    VisionProviderKind.LightweightTracker,
                    "unused",
                    VisionProviderLocation.OnDevice),
                request,
                Array.Empty<TrackedObject>());
        }

        private static ValueTask<TrackingResult> ThrowTrackerFailure(
            TrackingRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new InvalidOperationException("explicit tracker failure");
        }

        private static void Contains(
            string value,
            string expected,
            string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' lacks '{expected}'.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void Throws<TException>(Action action, string label)
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
                $"Managed test failed for {label}: expected {typeof(TException).Name}.");
        }

        private sealed class FakeResources : IReachyVisionFrameResources
        {
            private readonly object color = new object();
            private readonly object? validity;
            private int disposed;

            public FakeResources(int width, int height, bool hasValidity)
            {
                Width = width;
                Height = height;
                validity = hasValidity ? new object() : null;
            }

            public string OwnerId => "fake-frame-owner";

            public ulong Generation => 1UL;

            public int Width { get; }

            public int Height { get; }

            public bool IsDisposed => Volatile.Read(ref disposed) != 0;

            public int DisposeCount { get; private set; }

            public bool HasResource(VisionResourceKind kind)
            {
                return kind == VisionResourceKind.Color ||
                    (kind == VisionResourceKind.ValidityMask &&
                        validity != null);
            }

            public VisionPixelEncoding GetEncoding(
                VisionResourceKind kind)
            {
                if (!HasResource(kind))
                {
                    throw new InvalidOperationException(
                        "Requested fake resource is unavailable.");
                }
                return kind == VisionResourceKind.Color
                    ? VisionPixelEncoding.Rgba8
                    : VisionPixelEncoding.ValidityMask8;
            }

            public bool TryGetResource<TResource>(
                VisionResourceKind kind,
                out TResource? resource)
                where TResource : class
            {
                object? selected = kind == VisionResourceKind.Color
                    ? color
                    : validity;
                resource = selected as TResource;
                return resource != null;
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    DisposeCount++;
                }
                return default;
            }
        }

        private sealed class FakeFrameSource : IReachyVisionFrameSource
        {
            public FakeFrameSource(
                ProviderDescriptor descriptor,
                Func<FrameSourceRequest, CancellationToken,
                    ValueTask<FrameSourceResult>> handler)
            {
                Descriptor = descriptor;
                Handler = handler;
                Capabilities = new FrameSourceCapabilities(
                    supportsGpuColor: true,
                    supportsGpuValidityMask: true,
                    supportsCancellation: true,
                    maximumWidth: 4096,
                    maximumHeight: 4096,
                    maximumOutstandingFrames: 2);
            }

            public ProviderDescriptor Descriptor { get; }

            public FrameSourceCapabilities Capabilities { get; }

            public Func<FrameSourceRequest, CancellationToken,
                ValueTask<FrameSourceResult>> Handler { get; set; }

            public ValueTask<FrameSourceResult> AcquireAsync(
                FrameSourceRequest request,
                CancellationToken cancellationToken)
            {
                return Handler(request, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        private sealed class FakeTracker : IVisualTracker
        {
            private readonly Func<TrackingRequest, CancellationToken,
                ValueTask<TrackingResult>> handler;

            public FakeTracker(
                ProviderDescriptor descriptor,
                Func<TrackingRequest, CancellationToken,
                    ValueTask<TrackingResult>> handler)
            {
                Descriptor = descriptor;
                this.handler = handler;
                Capabilities = TrackerCapability();
            }

            public ProviderDescriptor Descriptor { get; }

            public TrackerCapabilities Capabilities { get; }

            public ValueTask<TrackingResult> AnalyzeAsync(
                TrackingRequest request,
                CancellationToken cancellationToken)
            {
                return handler(request, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        private sealed class FakeVisionLanguageProvider :
            IVisionLanguageProvider
        {
            private readonly Func<VisionLanguageRequest, CancellationToken,
                ValueTask<VisionLanguageResult>> handler;

            public FakeVisionLanguageProvider(
                ProviderDescriptor descriptor,
                Func<VisionLanguageRequest, CancellationToken,
                    ValueTask<VisionLanguageResult>> handler)
            {
                Descriptor = descriptor;
                this.handler = handler;
                Capabilities = VisionLanguageCapability();
            }

            public ProviderDescriptor Descriptor { get; }

            public VisionLanguageCapabilities Capabilities { get; }

            public int CallCount { get; private set; }

            public ValueTask<VisionLanguageResult> AnalyzeAsync(
                VisionLanguageRequest request,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return handler(request, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
