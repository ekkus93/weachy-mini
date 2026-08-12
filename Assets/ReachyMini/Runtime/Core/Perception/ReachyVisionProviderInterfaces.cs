#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public interface IReachyVisionFrameSource : IAsyncDisposable
    {
        ProviderDescriptor Descriptor { get; }

        FrameSourceCapabilities Capabilities { get; }

        ValueTask<FrameSourceResult> AcquireAsync(
            FrameSourceRequest request,
            CancellationToken cancellationToken);
    }

    public interface IVisualTracker : IAsyncDisposable
    {
        ProviderDescriptor Descriptor { get; }

        TrackerCapabilities Capabilities { get; }

        ValueTask<TrackingResult> AnalyzeAsync(
            TrackingRequest request,
            CancellationToken cancellationToken);
    }

    public interface IVisionLanguageProvider : IAsyncDisposable
    {
        ProviderDescriptor Descriptor { get; }

        VisionLanguageCapabilities Capabilities { get; }

        ValueTask<VisionLanguageResult> AnalyzeAsync(
            VisionLanguageRequest request,
            CancellationToken cancellationToken);
    }
}
