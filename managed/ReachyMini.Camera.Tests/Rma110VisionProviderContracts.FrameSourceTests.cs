#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
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
            await using var rawResources = new FakeResources(
                width: 10,
                height: 10,
                hasValidity: false);
            await using ReachyVisionFrame raw =
                RawFrame(rawResources, 5UL);
            await using var source = new FakeFrameSource(
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
            await using var staleResources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame staleFrame = Frame(
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
    }
}
