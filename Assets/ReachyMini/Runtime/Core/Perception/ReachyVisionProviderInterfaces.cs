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

    // RMA-195 phase D (VLM half): mirrors ReachyMini.Providers.ICloudLlmProviderCapability
    // -- an optional, as-castable capability on the perception composition service,
    // reachable only from tests and settings actions today, exactly like the local/cloud
    // LLM path's own still-open "no live call site yet" gap (see
    // ReachyLocalLlmProviderApplicationService's header comment). The caller supplies the
    // frame explicitly rather than this capability sourcing one internally -- there is no
    // established "capture a frame on demand" call path yet, so this stays honest about
    // what is and is not wired rather than inventing one.
    public interface ICloudVlmProviderCapability
    {
        ValueTask<VisionLanguageResult> AnalyzeSceneAsync(
            ReachyVisionFrame frame,
            string prompt,
            string requestId,
            CancellationToken cancellationToken);
    }
}
