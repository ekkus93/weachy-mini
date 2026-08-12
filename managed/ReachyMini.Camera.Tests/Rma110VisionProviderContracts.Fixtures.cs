#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
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
    }
}
