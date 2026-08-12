#nullable enable

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public abstract class OpenAiVisionLanguageProviderBase :
        IVisionLanguageProvider
    {
        private const int CoverageContextMaximumCharacters = 1024;

        private readonly OpenAiVisionProviderConfiguration configuration;
        private readonly IOpenAiVisionTransport transport;
        private readonly IRemoteVlmImageEncoder encoder;
        private int disposed;
        private int activeOperations;

        protected OpenAiVisionLanguageProviderBase(
            OpenAiVisionEndpointStyle requiredEndpointStyle,
            OpenAiVisionProviderConfiguration configuration,
            IOpenAiVisionTransport transport,
            IRemoteVlmImageEncoder encoder)
        {
            this.configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            this.transport = transport ??
                throw new ArgumentNullException(nameof(transport));
            this.encoder = encoder ??
                throw new ArgumentNullException(nameof(encoder));
            if (configuration.EndpointStyle != requiredEndpointStyle)
            {
                throw new ArgumentException(
                    "Provider configuration endpoint style does not match the adapter class.",
                    nameof(configuration));
            }
            if (transport.EndpointStyle != requiredEndpointStyle)
            {
                throw new ArgumentException(
                    "The selected transport endpoint style does not match the adapter class.",
                    nameof(transport));
            }

            Descriptor = configuration.CreateDescriptor();
            Capabilities = configuration.CreateCapabilities();
        }

        public ProviderDescriptor Descriptor { get; }

        public VisionLanguageCapabilities Capabilities { get; }

        public async ValueTask<VisionLanguageResult> AnalyzeAsync(
            VisionLanguageRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    VisionOperationStatus.Cancelled,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM request was cancelled before image encoding.");
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                return Failure(
                    VisionOperationStatus.Unavailable,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM provider is disposed.");
            }
            if (request.Context.ProviderKind !=
                    VisionProviderKind.SemanticVisionLanguage ||
                !string.Equals(
                    request.Context.ProviderInstanceId,
                    Descriptor.InstanceId,
                    StringComparison.Ordinal))
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM request identity does not match the selected provider.");
            }
            if (!request.NetworkDisclosureAcknowledged)
            {
                return Failure(
                    VisionOperationStatus.Unavailable,
                    request,
                    requiresProviderReset: false,
                    "Network disclosure acknowledgement is required before remote VLM invocation.");
            }
            if (!request.Frame.IsObservationEligible ||
                request.Frame.Origin != VisionFrameOrigin.TransformedReachyEye)
            {
                return Failure(
                    VisionOperationStatus.InvalidFrame,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM input is not an eligible transformed Reachy-eye frame.");
            }
            if (request.Prompt.Length > Capabilities.MaximumPromptCharacters)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM prompt exceeds the configured provider limit.");
            }
            if (!TryAcquireOperation())
            {
                return Failure(
                    VisionOperationStatus.Unavailable,
                    request,
                    requiresProviderReset: false,
                    "Remote VLM concurrency limit is active; the request was not queued or rerouted.");
            }

            try
            {
                RemoteVlmImageEncodingResult encoding;
                try
                {
                    encoding = await encoder.EncodeAsync(
                        new RemoteVlmImageEncodingRequest(
                            request.Frame,
                            configuration.ImagePolicy),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Failure(
                        cancellationToken.IsCancellationRequested
                            ? VisionOperationStatus.Cancelled
                            : VisionOperationStatus.ProviderFailure,
                        request,
                        requiresProviderReset:
                            !cancellationToken.IsCancellationRequested,
                        cancellationToken.IsCancellationRequested
                            ? "Remote VLM image encoding was cancelled."
                            : "Remote VLM image encoder cancelled without caller cancellation.");
                }
                catch (Exception exception)
                {
                    return Failure(
                        VisionOperationStatus.ProviderFailure,
                        request,
                        requiresProviderReset: true,
                        "Remote VLM image encoder failed with " +
                            exception.GetType().Name + ".");
                }

                if (!encoding.Succeeded)
                {
                    return MapEncodingFailure(encoding, request);
                }

                RemoteVlmEncodedImage image = encoding.Image ??
                    throw new InvalidOperationException(
                        "Successful image encoding returned no payload.");
                try
                {
                    VisionLanguageResult? invalidImage = ValidateEncodedImage(
                        image,
                        request);
                    if (invalidImage != null)
                    {
                        return invalidImage;
                    }

                    string context = BuildCoverageContext(
                        request.Frame.Coverage,
                        image.InvalidPixelPolicyApplied);
                    if (context.Length > CoverageContextMaximumCharacters)
                    {
                        return Failure(
                            VisionOperationStatus.ContractViolation,
                            request,
                            requiresProviderReset: true,
                            "Remote VLM coverage context exceeded its fixed bound.");
                    }

                    var transportRequest = new OpenAiVisionTransportRequest(
                        configuration.EndpointStyle,
                        request.Context.RequestId,
                        configuration.ModelId,
                        context,
                        request.Prompt,
                        image,
                        configuration.ImagePolicy.Detail,
                        configuration.MaximumOutputTokens);
                    OpenAiVisionTransportResult transportResult;
                    try
                    {
                        transportResult = await transport.SendAsync(
                            transportRequest,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return Failure(
                            cancellationToken.IsCancellationRequested
                                ? VisionOperationStatus.Cancelled
                                : VisionOperationStatus.ProviderFailure,
                            request,
                            requiresProviderReset:
                                !cancellationToken.IsCancellationRequested,
                            cancellationToken.IsCancellationRequested
                                ? "Remote VLM transport was cancelled."
                                : "Remote VLM transport cancelled without caller cancellation.");
                    }
                    catch (Exception exception)
                    {
                        return Failure(
                            VisionOperationStatus.ProviderFailure,
                            request,
                            requiresProviderReset: true,
                            "Remote VLM transport failed with " +
                                exception.GetType().Name + ".");
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Failure(
                            VisionOperationStatus.Cancelled,
                            request,
                            requiresProviderReset: false,
                            "Remote VLM request was cancelled before result acceptance.");
                    }
                    if (Volatile.Read(ref disposed) != 0)
                    {
                        return Failure(
                            VisionOperationStatus.Unavailable,
                            request,
                            requiresProviderReset: false,
                            "Remote VLM provider was disposed before result acceptance.");
                    }

                    return MapTransportResult(transportResult, request);
                }
                finally
                {
                    image.Dispose();
                }
            }
            finally
            {
                _ = Interlocked.Decrement(ref activeOperations);
            }
        }

        public ValueTask DisposeAsync()
        {
            _ = Interlocked.Exchange(ref disposed, 1);
            GC.SuppressFinalize(this);
            return default;
        }

        private bool TryAcquireOperation()
        {
            while (true)
            {
                int current = Volatile.Read(ref activeOperations);
                if (current >= configuration.MaximumConcurrentOperations)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(
                        ref activeOperations,
                        current + 1,
                        current) == current)
                {
                    return true;
                }
            }
        }

        private VisionLanguageResult MapEncodingFailure(
            RemoteVlmImageEncodingResult encoding,
            VisionLanguageRequest request)
        {
            VisionOperationStatus status;
            switch (encoding.Status)
            {
                case RemoteVlmImageEncodingStatus.InvalidFrame:
                    status = VisionOperationStatus.InvalidFrame;
                    break;
                case RemoteVlmImageEncodingStatus.Unsupported:
                    status = VisionOperationStatus.Unavailable;
                    break;
                case RemoteVlmImageEncodingStatus.Cancelled:
                    status = VisionOperationStatus.Cancelled;
                    break;
                case RemoteVlmImageEncodingStatus.Failed:
                    status = VisionOperationStatus.ProviderFailure;
                    break;
                default:
                    status = VisionOperationStatus.ContractViolation;
                    break;
            }

            return Failure(
                status,
                request,
                encoding.RequiresEncoderReset,
                "Remote VLM image encoding failed with code " +
                    encoding.DiagnosticCode + ".");
        }

        private VisionLanguageResult? ValidateEncodedImage(
            RemoteVlmEncodedImage image,
            VisionLanguageRequest request)
        {
            if (!request.Frame.Identity.Matches(image.SourceIdentity) ||
                image.SourceOrigin != VisionFrameOrigin.TransformedReachyEye ||
                image.SourceWidth != request.Frame.Width ||
                image.SourceHeight != request.Frame.Height)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: true,
                    "Remote VLM image payload identity does not match the request frame.");
            }
            if (!image.ValidityMaskApplied ||
                !image.ContainsOnlyValidPixels ||
                image.Upscaled)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: true,
                    "Remote VLM image payload violated the transformed valid-pixel contract.");
            }
            if (image.Width > configuration.ImagePolicy.MaximumWidth ||
                image.Height > configuration.ImagePolicy.MaximumHeight ||
                image.EncodedByteCount >
                    configuration.ImagePolicy.MaximumEncodedBytes)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: true,
                    "Remote VLM image payload exceeded the configured dimension or byte limit.");
            }
            if (image.Format != configuration.ImagePolicy.PreferredFormat ||
                image.InvalidPixelPolicyApplied !=
                    configuration.ImagePolicy.InvalidPixelPolicy)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: true,
                    "Remote VLM image payload did not apply the selected encoding policy exactly.");
            }
            return null;
        }

        private VisionLanguageResult MapTransportResult(
            OpenAiVisionTransportResult result,
            VisionLanguageRequest request)
        {
            if (result == null)
            {
                return Failure(
                    VisionOperationStatus.ContractViolation,
                    request,
                    requiresProviderReset: true,
                    "Remote VLM transport returned no structured result.");
            }
            if (result.Succeeded)
            {
                string text = result.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) ||
                    text.Length > configuration.MaximumResponseCharacters)
                {
                    return Failure(
                        VisionOperationStatus.ContractViolation,
                        request,
                        requiresProviderReset: true,
                        "Remote VLM transport returned missing or oversized semantic text.");
                }
                return VisionLanguageResult.Success(
                    Descriptor,
                    request,
                    text);
            }

            VisionOperationStatus status;
            switch (result.Status)
            {
                case OpenAiVisionTransportStatus.Cancelled:
                    status = VisionOperationStatus.Cancelled;
                    break;
                case OpenAiVisionTransportStatus.TimedOut:
                    status = VisionOperationStatus.TimedOut;
                    break;
                case OpenAiVisionTransportStatus.Unavailable:
                    status = VisionOperationStatus.Unavailable;
                    break;
                case OpenAiVisionTransportStatus.InvalidRequest:
                case OpenAiVisionTransportStatus.Unauthorized:
                case OpenAiVisionTransportStatus.RateLimited:
                case OpenAiVisionTransportStatus.ServerFailure:
                    status = VisionOperationStatus.ProviderFailure;
                    break;
                case OpenAiVisionTransportStatus.ProtocolFailure:
                    status = VisionOperationStatus.ContractViolation;
                    break;
                default:
                    status = VisionOperationStatus.ContractViolation;
                    break;
            }

            return Failure(
                status,
                request,
                result.RequiresTransportReset,
                FormatProviderError(result));
        }

        private static string FormatProviderError(
            OpenAiVisionTransportResult result)
        {
            OpenAiVisionProviderError? error = result.Error;
            if (error == null)
            {
                return "Remote VLM transport failed without structured provider error detail.";
            }

            var builder = new StringBuilder();
            builder.Append("Remote VLM provider error category=");
            builder.Append(error.Category.ToString());
            builder.Append(", code=");
            builder.Append(error.Code);
            if (error.HttpStatusCode.HasValue)
            {
                builder.Append(", http_status=");
                builder.Append(
                    error.HttpStatusCode.Value.ToString(
                        CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrEmpty(error.ProviderRequestId))
            {
                builder.Append(", provider_request_id=");
                builder.Append(error.ProviderRequestId);
            }
            builder.Append(". Detail: ");
            builder.Append(error.Detail);
            return builder.ToString();
        }

        private VisionLanguageResult Failure(
            VisionOperationStatus status,
            VisionLanguageRequest request,
            bool requiresProviderReset,
            string diagnostic)
        {
            return VisionLanguageResult.Failure(
                status,
                Descriptor,
                request,
                requiresProviderReset,
                diagnostic);
        }

        private static string BuildCoverageContext(
            ReachyVisionCoverage coverage,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicy)
        {
            if (coverage == null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }

            var builder = new StringBuilder();
            builder.Append(
                "Analyze only the supplied transformed Reachy-eye image, never the raw phone camera frame. ");
            builder.Append(
                "The validity mask was applied before resizing and encoding. ");
            builder.Append("Invalid pixels were ");
            builder.Append(
                invalidPixelPolicy ==
                    RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack
                    ? "replaced with opaque black"
                    : "removed by cropping to valid bounds");
            builder.Append(
                "; do not infer objects or scene content from invalid regions or outside current coverage. ");

            bool limitedCoverage =
                coverage.State == VisionCoverageState.Degraded ||
                coverage.Fraction < 0.999999;
            if (limitedCoverage)
            {
                builder.Append("Coverage is degraded: ");
                builder.Append(
                    (coverage.Fraction * 100.0).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
                builder.Append("% of transformed pixels are valid. ");
            }
            else
            {
                builder.Append("Coverage is normal. ");
            }

            builder.Append(
                "No world-model entity history or recent-but-stale entity list is included by this adapter; treat only the supplied image as current visual evidence.");
            return builder.ToString();
        }
    }

    public sealed class OpenAiResponsesVisionLanguageProvider :
        OpenAiVisionLanguageProviderBase
    {
        public OpenAiResponsesVisionLanguageProvider(
            OpenAiVisionProviderConfiguration configuration,
            IOpenAiVisionTransport transport,
            IRemoteVlmImageEncoder encoder)
            : base(
                OpenAiVisionEndpointStyle.Responses,
                configuration,
                transport,
                encoder)
        {
        }
    }

    public sealed class OpenAiChatCompletionsVisionLanguageProvider :
        OpenAiVisionLanguageProviderBase
    {
        public OpenAiChatCompletionsVisionLanguageProvider(
            OpenAiVisionProviderConfiguration configuration,
            IOpenAiVisionTransport transport,
            IRemoteVlmImageEncoder encoder)
            : base(
                OpenAiVisionEndpointStyle.ChatCompletions,
                configuration,
                transport,
                encoder)
        {
        }
    }
}
