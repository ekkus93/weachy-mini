#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        private static async Task CallerCancellationReturnsTypedFailureAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-cancel",
                VisionProviderLocation.OnDevice);
            var selection = new VisionProviderSelection(descriptor);
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 10UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-cancel",
                    selection,
                    TimeSpan.FromSeconds(2.0)));
            await using var tracker = new FakeTracker(
                descriptor,
                WaitForTrackerCancellationAsync);
            using var cancellation = new CancellationTokenSource();
            Task<TrackingResult> pending = VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                cancellation.Token).AsTask();
            cancellation.Cancel();
            Task completed = await Task.WhenAny(
                pending,
                Task.Delay(
                    TimeSpan.FromSeconds(1.0),
                    CancellationToken.None)).ConfigureAwait(false);
            if (completed != pending)
            {
                throw new InvalidOperationException(
                    "Managed test failed: caller cancellation did not complete within one second.");
            }
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
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
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
            await using var tracker = new FakeTracker(
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
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 12UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-fault",
                    selection,
                    TimeSpan.FromSeconds(1.0)));
            await using var tracker = new FakeTracker(
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
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
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
            await using var tracker = new FakeTracker(
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
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Degraded,
                sourceSequence: 14UL);
            TrackingRequest request = new TrackingRequest(
                frame,
                Context(
                    "track-identity",
                    selection,
                    TimeSpan.FromSeconds(1.0)));
            await using var tracker = new FakeTracker(
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
    }
}
