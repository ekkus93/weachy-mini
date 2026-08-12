#nullable enable

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public sealed class RemoteVlmImageEncodingRequest
    {
        public RemoteVlmImageEncodingRequest(
            ReachyVisionFrame frame,
            RemoteVlmImagePolicy policy)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!frame.IsObservationEligible ||
                frame.Origin != VisionFrameOrigin.TransformedReachyEye ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(VisionResourceKind.ValidityMask))
            {
                throw new ArgumentException(
                    "Remote VLM encoding requires an eligible transformed Reachy-eye frame with a validity mask.",
                    nameof(frame));
            }
        }

        public ReachyVisionFrame Frame { get; }

        public RemoteVlmImagePolicy Policy { get; }

        public bool RequireValidityMask { get; } = true;

        public bool ApplyValidityBeforeResize { get; } = true;
    }

    public sealed class RemoteVlmEncodedImage : IDisposable
    {
        private readonly byte[] encodedBytes;
        private int disposed;

        public RemoteVlmEncodedImage(
            ReachyVisionFrameIdentity sourceIdentity,
            VisionFrameOrigin sourceOrigin,
            int sourceWidth,
            int sourceHeight,
            int width,
            int height,
            RemoteVlmImageFormat format,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicyApplied,
            bool validityMaskApplied,
            bool containsOnlyValidPixels,
            bool upscaled,
            byte[] encodedBytes)
        {
            SourceIdentity = sourceIdentity ??
                throw new ArgumentNullException(nameof(sourceIdentity));
            if (sourceOrigin != VisionFrameOrigin.TransformedReachyEye)
            {
                throw new ArgumentException(
                    "Remote VLM payloads may originate only from transformed Reachy-eye frames.",
                    nameof(sourceOrigin));
            }
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            }
            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));
            }
            if (width <= 0 || width > sourceWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0 || height > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageFormat), format))
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            if (!Enum.IsDefined(
                    typeof(RemoteVlmInvalidPixelPolicy),
                    invalidPixelPolicyApplied))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(invalidPixelPolicyApplied));
            }
            if (!validityMaskApplied || !containsOnlyValidPixels)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images must apply the validity mask and contain only valid transformed image content.");
            }
            if (upscaled)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images cannot be upscaled.",
                    nameof(upscaled));
            }
            if (encodedBytes == null)
            {
                throw new ArgumentNullException(nameof(encodedBytes));
            }
            if (encodedBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images cannot be empty.",
                    nameof(encodedBytes));
            }

            SourceOrigin = sourceOrigin;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            Width = width;
            Height = height;
            Format = format;
            InvalidPixelPolicyApplied = invalidPixelPolicyApplied;
            ValidityMaskApplied = true;
            ContainsOnlyValidPixels = true;
            Upscaled = false;
            this.encodedBytes = (byte[])encodedBytes.Clone();
        }

        public ReachyVisionFrameIdentity SourceIdentity { get; }

        public VisionFrameOrigin SourceOrigin { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int Width { get; }

        public int Height { get; }

        public RemoteVlmImageFormat Format { get; }

        public RemoteVlmInvalidPixelPolicy InvalidPixelPolicyApplied { get; }

        public bool ValidityMaskApplied { get; }

        public bool ContainsOnlyValidPixels { get; }

        public bool Upscaled { get; }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public int EncodedByteCount => encodedBytes.Length;

        public string MediaType
        {
            get
            {
                switch (Format)
                {
                    case RemoteVlmImageFormat.Jpeg:
                        return "image/jpeg";
                    case RemoteVlmImageFormat.Png:
                        return "image/png";
                    case RemoteVlmImageFormat.WebP:
                        return "image/webp";
                    default:
                        throw new InvalidOperationException(
                            "Unsupported remote VLM image format.");
                }
            }
        }

        public ReadOnlyMemory<byte> EncodedBytes
        {
            get
            {
                if (IsDisposed)
                {
                    throw new ObjectDisposedException(
                        nameof(RemoteVlmEncodedImage));
                }
                return encodedBytes;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            Array.Clear(encodedBytes, 0, encodedBytes.Length);
        }
    }

    public sealed class RemoteVlmImageEncodingResult
    {
        private RemoteVlmImageEncodingResult(
            RemoteVlmImageEncodingStatus status,
            RemoteVlmEncodedImage? image,
            string diagnosticCode,
            bool requiresEncoderReset)
        {
            Status = status;
            Image = image;
            DiagnosticCode = diagnosticCode;
            RequiresEncoderReset = requiresEncoderReset;
        }

        public RemoteVlmImageEncodingStatus Status { get; }

        public RemoteVlmEncodedImage? Image { get; }

        public string DiagnosticCode { get; }

        public bool RequiresEncoderReset { get; }

        public bool Succeeded => Status == RemoteVlmImageEncodingStatus.Succeeded;

        public static RemoteVlmImageEncodingResult Success(
            RemoteVlmEncodedImage image)
        {
            return new RemoteVlmImageEncodingResult(
                RemoteVlmImageEncodingStatus.Succeeded,
                image ?? throw new ArgumentNullException(nameof(image)),
                "encoded",
                requiresEncoderReset: false);
        }

        public static RemoteVlmImageEncodingResult Failure(
            RemoteVlmImageEncodingStatus status,
            string diagnosticCode,
            bool requiresEncoderReset)
        {
            if (status == RemoteVlmImageEncodingStatus.Succeeded ||
                !Enum.IsDefined(typeof(RemoteVlmImageEncodingStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new RemoteVlmImageEncodingResult(
                status,
                null,
                RequireSafeToken(
                    diagnosticCode,
                    nameof(diagnosticCode),
                    64),
                requiresEncoderReset);
        }

        internal static string RequireSafeToken(
            string value,
            string name,
            int maximumLength)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            for (int index = 0; index < text.Length; ++index)
            {
                char character = text[index];
                bool valid =
                    (character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '.' ||
                    character == '_' ||
                    character == '-';
                if (!valid)
                {
                    throw new ArgumentException(
                        "Diagnostic tokens may contain only ASCII letters, digits, '.', '_' and '-'.",
                        name);
                }
            }
            return text;
        }
    }

    public interface IRemoteVlmImageEncoder
    {
        ValueTask<RemoteVlmImageEncodingResult> EncodeAsync(
            RemoteVlmImageEncodingRequest request,
            CancellationToken cancellationToken);
    }

    public sealed class OpenAiVisionProviderConfiguration
    {
        public OpenAiVisionProviderConfiguration(
            OpenAiVisionEndpointStyle endpointStyle,
            string providerId,
            string providerInstanceId,
            string displayName,
            string version,
            string modelId,
            VisionProviderLocation location,
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            int maximumConcurrentOperations,
            int maximumPromptCharacters,
            int maximumOutputTokens,
            int maximumResponseCharacters,
            RemoteVlmImagePolicy imagePolicy)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionEndpointStyle),
                    endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (location != VisionProviderLocation.Cloud &&
                location != VisionProviderLocation.LocalNetwork)
            {
                throw new ArgumentException(
                    "OpenAI-compatible VLM providers must be explicit cloud or local-network providers.",
                    nameof(location));
            }
            if (!supportsVisualQuestions && !supportsSceneDescription)
            {
                throw new ArgumentException(
                    "A remote VLM provider must declare at least one semantic capability.");
            }
            if (maximumConcurrentOperations <= 0 ||
                maximumConcurrentOperations > 16)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }
            if (maximumPromptCharacters <= 0 ||
                maximumPromptCharacters > 131072)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPromptCharacters));
            }
            if (maximumOutputTokens <= 0 || maximumOutputTokens > 32768)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutputTokens));
            }
            if (maximumResponseCharacters <= 0 ||
                maximumResponseCharacters > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumResponseCharacters));
            }

            EndpointStyle = endpointStyle;
            ProviderId = RequireBoundedText(
                providerId,
                nameof(providerId),
                128);
            ProviderInstanceId = RequireBoundedText(
                providerInstanceId,
                nameof(providerInstanceId),
                128);
            DisplayName = RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            Version = RequireBoundedText(
                version,
                nameof(version),
                128);
            ModelId = RequireBoundedText(modelId, nameof(modelId), 256);
            Location = location;
            SupportsVisualQuestions = supportsVisualQuestions;
            SupportsSceneDescription = supportsSceneDescription;
            MaximumConcurrentOperations = maximumConcurrentOperations;
            MaximumPromptCharacters = maximumPromptCharacters;
            MaximumOutputTokens = maximumOutputTokens;
            MaximumResponseCharacters = maximumResponseCharacters;
            ImagePolicy = imagePolicy ??
                throw new ArgumentNullException(nameof(imagePolicy));
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public string ProviderId { get; }

        public string ProviderInstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }

        public string ModelId { get; }

        public VisionProviderLocation Location { get; }

        public bool SupportsVisualQuestions { get; }

        public bool SupportsSceneDescription { get; }

        public int MaximumConcurrentOperations { get; }

        public int MaximumPromptCharacters { get; }

        public int MaximumOutputTokens { get; }

        public int MaximumResponseCharacters { get; }

        public RemoteVlmImagePolicy ImagePolicy { get; }

        public ProviderDescriptor CreateDescriptor()
        {
            return new ProviderDescriptor(
                VisionProviderKind.SemanticVisionLanguage,
                ProviderId,
                ProviderInstanceId,
                DisplayName,
                Version,
                Location);
        }

        public VisionLanguageCapabilities CreateCapabilities()
        {
            return new VisionLanguageCapabilities(
                SupportsVisualQuestions,
                SupportsSceneDescription,
                supportsCancellation: true,
                maximumConcurrentOperations: MaximumConcurrentOperations,
                maximumPromptCharacters: MaximumPromptCharacters);
        }

        private static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            return text;
        }
    }

    public sealed class OpenAiVisionProviderError
    {
        private static readonly string[] ForbiddenDetailFragments =
        {
            "authorization:",
            "bearer ",
            "x-api-key",
            "api-key",
            "api_key",
            "data:image/",
            "base64,",
            "sk-",
            "secret=",
            "access_token",
        };

        public OpenAiVisionProviderError(
            OpenAiVisionProviderErrorCategory category,
            string code,
            int? httpStatusCode,
            string? providerRequestId,
            string detail)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionProviderErrorCategory),
                    category))
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }
            if (httpStatusCode.HasValue &&
                (httpStatusCode.Value < 100 || httpStatusCode.Value > 599))
            {
                throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
            }

            Category = category;
            Code = RemoteVlmImageEncodingResult.RequireSafeToken(
                code,
                nameof(code),
                64);
            HttpStatusCode = httpStatusCode;
            ProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId)
                ? null
                : RemoteVlmImageEncodingResult.RequireSafeToken(
                    providerRequestId,
                    nameof(providerRequestId),
                    128);
            Detail = SanitizeDetail(detail, out bool redacted);
            DetailRedacted = redacted;
        }

        public OpenAiVisionProviderErrorCategory Category { get; }

        public string Code { get; }

        public int? HttpStatusCode { get; }

        public string? ProviderRequestId { get; }

        public string Detail { get; }

        public bool DetailRedacted { get; }

        private static string SanitizeDetail(
            string value,
            out bool redacted)
        {
            string text = ProviderDescriptor.RequireText(
                value,
                nameof(value)).Trim();
            var builder = new StringBuilder(Math.Min(text.Length, 512));
            bool previousWasSpace = false;
            for (int index = 0; index < text.Length && builder.Length < 512; ++index)
            {
                char character = text[index];
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                    continue;
                }
                builder.Append(character);
                previousWasSpace = false;
            }

            string sanitized = builder.ToString().Trim();
            redacted = text.Length > 512 || ContainsForbiddenDetail(sanitized) ||
                ContainsLongOpaqueToken(sanitized);
            return redacted
                ? "Provider detail was redacted because it contained credential or payload-like material."
                : sanitized;
        }

        private static bool ContainsForbiddenDetail(string value)
        {
            for (int index = 0; index < ForbiddenDetailFragments.Length; ++index)
            {
                if (value.Contains(
                        ForbiddenDetailFragments[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsLongOpaqueToken(string value)
        {
            int run = 0;
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool opaque =
                    (character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '+' ||
                    character == '/' ||
                    character == '=' ||
                    character == '_' ||
                    character == '-';
                run = opaque ? run + 1 : 0;
                if (run >= 80)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class OpenAiVisionTransportRequest
    {
        public OpenAiVisionTransportRequest(
            OpenAiVisionEndpointStyle endpointStyle,
            string requestId,
            string modelId,
            string systemContext,
            string userPrompt,
            RemoteVlmEncodedImage image,
            RemoteVlmImageDetail detail,
            int maximumOutputTokens)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionEndpointStyle),
                    endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageDetail), detail))
            {
                throw new ArgumentOutOfRangeException(nameof(detail));
            }
            if (maximumOutputTokens <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutputTokens));
            }

            EndpointStyle = endpointStyle;
            RequestId = ProviderDescriptor.RequireText(
                requestId,
                nameof(requestId));
            ModelId = ProviderDescriptor.RequireText(modelId, nameof(modelId));
            SystemContext = ProviderDescriptor.RequireText(
                systemContext,
                nameof(systemContext));
            UserPrompt = ProviderDescriptor.RequireText(
                userPrompt,
                nameof(userPrompt));
            Image = image ?? throw new ArgumentNullException(nameof(image));
            if (image.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(image));
            }
            Detail = detail;
            MaximumOutputTokens = maximumOutputTokens;
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public string RequestId { get; }

        public string ModelId { get; }

        public string SystemContext { get; }

        public string UserPrompt { get; }

        public RemoteVlmEncodedImage Image { get; }

        public RemoteVlmImageDetail Detail { get; }

        public int MaximumOutputTokens { get; }

        public bool StoreResponse { get; }

        public bool Stream { get; }
    }

    public sealed class OpenAiVisionTransportResult
    {
        private OpenAiVisionTransportResult(
            OpenAiVisionTransportStatus status,
            string? text,
            string? providerRequestId,
            string? finishReason,
            long? inputTokens,
            long? outputTokens,
            OpenAiVisionProviderError? error,
            bool requiresTransportReset)
        {
            Status = status;
            Text = text;
            ProviderRequestId = providerRequestId;
            FinishReason = finishReason;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            Error = error;
            RequiresTransportReset = requiresTransportReset;
        }

        public OpenAiVisionTransportStatus Status { get; }

        public string? Text { get; }

        public string? ProviderRequestId { get; }

        public string? FinishReason { get; }

        public long? InputTokens { get; }

        public long? OutputTokens { get; }

        public OpenAiVisionProviderError? Error { get; }

        public bool RequiresTransportReset { get; }

        public bool Succeeded => Status == OpenAiVisionTransportStatus.Succeeded;

        public static OpenAiVisionTransportResult Success(
            string text,
            string? providerRequestId,
            string? finishReason,
            long? inputTokens,
            long? outputTokens)
        {
            string resultText = ProviderDescriptor.RequireText(
                text,
                nameof(text));
            string? safeRequestId = string.IsNullOrWhiteSpace(providerRequestId)
                ? null
                : RemoteVlmImageEncodingResult.RequireSafeToken(
                    providerRequestId,
                    nameof(providerRequestId),
                    128);
            string? safeFinishReason = string.IsNullOrWhiteSpace(finishReason)
                ? null
                : RemoteVlmImageEncodingResult.RequireSafeToken(
                    finishReason,
                    nameof(finishReason),
                    64);
            RequireNonNegative(inputTokens, nameof(inputTokens));
            RequireNonNegative(outputTokens, nameof(outputTokens));

            return new OpenAiVisionTransportResult(
                OpenAiVisionTransportStatus.Succeeded,
                resultText,
                safeRequestId,
                safeFinishReason,
                inputTokens,
                outputTokens,
                null,
                requiresTransportReset: false);
        }

        public static OpenAiVisionTransportResult Failure(
            OpenAiVisionTransportStatus status,
            OpenAiVisionProviderError error,
            bool requiresTransportReset)
        {
            if (status == OpenAiVisionTransportStatus.Succeeded ||
                !Enum.IsDefined(typeof(OpenAiVisionTransportStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new OpenAiVisionTransportResult(
                status,
                null,
                null,
                null,
                null,
                null,
                error ?? throw new ArgumentNullException(nameof(error)),
                requiresTransportReset);
        }

        private static void RequireNonNegative(long? value, string name)
        {
            if (value.HasValue && value.Value < 0L)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public interface IOpenAiVisionTransport
    {
        OpenAiVisionEndpointStyle EndpointStyle { get; }

        ValueTask<OpenAiVisionTransportResult> SendAsync(
            OpenAiVisionTransportRequest request,
            CancellationToken cancellationToken);
    }

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
