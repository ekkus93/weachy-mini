#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal sealed class FakeProvider : IVisionLanguageProvider
    {
        public FakeProvider(
            ProviderDescriptor descriptor,
            VisionLanguageCapabilities capabilities)
        {
            Descriptor = descriptor ??
                throw new ArgumentNullException(nameof(descriptor));
            Capabilities = capabilities ??
                throw new ArgumentNullException(nameof(capabilities));
        }

        public ProviderDescriptor Descriptor { get; }

        public VisionLanguageCapabilities Capabilities { get; }

        public ValueTask<VisionLanguageResult> AnalyzeAsync(
            VisionLanguageRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<VisionLanguageResult>(
                    VisionLanguageResult.Failure(
                        VisionOperationStatus.Cancelled,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "Cancelled."));
            }
            return new ValueTask<VisionLanguageResult>(
                VisionLanguageResult.Success(
                    Descriptor,
                    request,
                    "Synthetic semantic output."));
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
