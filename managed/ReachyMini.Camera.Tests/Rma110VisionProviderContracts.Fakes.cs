#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        // FakeTracker and FakeVisionLanguageProvider construct their
        // Capabilities from TrackerCapability()/VisionLanguageCapability()
        // in Rma110VisionProviderContracts.Fixtures.cs.
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
