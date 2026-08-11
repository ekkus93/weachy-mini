#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public sealed partial class ReachyOpenAiCompatibleAsrProvider : IAsrProvider
    {
        private const string InternalBearerSecretReference =
            "reachy.internal.asr.bearer";
        private const int MaximumTranscriptCharacters = 256 * 1024;
        private const int MaximumJsonDepth = 64;

        private readonly ReachyProviderProfile sourceProfile;
        private readonly IReachyProviderSecretStore? sourceSecretStore;
        private readonly IBufferedAsrUtteranceSource utteranceSource;
        private readonly ReachyOpenAiCompatibleAsrOptions options;
        private readonly ReachySharedHttpTransport transport;
        private readonly bool ownsTransport;
        private int disposed;

        public ReachyOpenAiCompatibleAsrProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            IBufferedAsrUtteranceSource utteranceSource,
            ReachyOpenAiCompatibleAsrOptions options,
            IEnumerable<string> supportedLanguageTags,
            SpeechProviderLocation location,
            string instanceId)
            : this(
                profile,
                secretStore,
                utteranceSource,
                options,
                supportedLanguageTags,
                location,
                instanceId,
                transportOverride: null)
        {
        }

        internal ReachyOpenAiCompatibleAsrProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            IBufferedAsrUtteranceSource utteranceSource,
            ReachyOpenAiCompatibleAsrOptions options,
            IEnumerable<string> supportedLanguageTags,
            SpeechProviderLocation location,
            string instanceId,
            ReachySharedHttpTransport? transportOverride)
        {
            sourceProfile = profile ?? throw new ArgumentNullException(nameof(profile));
            sourceSecretStore = secretStore;
            this.utteranceSource = utteranceSource ??
                throw new ArgumentNullException(nameof(utteranceSource));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateProfile(profile, options.AuthenticationMode);
            ValidateLocation(location);

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                profile.ProviderId,
                SpeechProviderDescriptor.RequireText(instanceId, nameof(instanceId)),
                profile.DisplayName,
                "rma-144-v1",
                location,
                SpeechNetworkRequirement.Required);
            Capabilities = new AsrCapabilities(
                supportedLanguageTags,
                supportsPartialResults: false,
                supportsCancellation: true,
                options.MaximumUtteranceDuration);

            if (transportOverride != null)
            {
                transport = transportOverride;
                ownsTransport = false;
            }
            else
            {
                (ReachyProviderProfile transportProfile,
                    IReachyProviderSecretStore? transportSecretStore) =
                    CreateTransportBinding(
                        profile,
                        secretStore,
                        options.AuthenticationMode);
                transport = new ReachySharedHttpTransport(
                    transportProfile,
                    transportSecretStore,
                    ReachyHttpTransportPolicy.FromProfile(transportProfile));
                ownsTransport = true;
            }
        }

        public SpeechProviderDescriptor Descriptor { get; }

        public AsrCapabilities Capabilities { get; }

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions asrOptions,
            CancellationToken cancellationToken)
        {
            if (asrOptions == null)
            {
                throw new ArgumentNullException(nameof(asrOptions));
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Unavailable,
                        "ASR provider is disposed."));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    Task.FromCanceled<SpeechProviderAvailability>(cancellationToken));
            }
            if (asrOptions.RequestPartialResults)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Unavailable,
                        "Buffered OpenAI-compatible ASR does not provide partial results."));
            }
            if (!SupportsLanguage(asrOptions.LanguageTag))
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Unavailable,
                        "The selected ASR provider does not declare support for the requested language tag."));
            }

            try
            {
                string? missing = FindMissingSecretReference();
                if (missing != null)
                {
                    return new ValueTask<SpeechProviderAvailability>(
                        new SpeechProviderAvailability(
                            SpeechAvailabilityState.SetupRequired,
                            "ASR provider credential material is not configured."));
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is ArgumentException)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Faulted,
                        "ASR provider credential storage could not be evaluated."));
            }

            return new ValueTask<SpeechProviderAvailability>(
                new SpeechProviderAvailability(
                    SpeechAvailabilityState.Available,
                    "ASR provider configuration is available."));
        }

        public async IAsyncEnumerable<AsrEvent> RecognizeAsync(
            AsrRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyOpenAiCompatibleAsrProvider));
            }
            if (!string.Equals(
                    request.Context.ProviderInstanceId,
                    Descriptor.InstanceId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "ASR request targets a different provider instance.",
                    nameof(request));
            }

            yield return new AsrEvent(
                Descriptor.InstanceId,
                request.Context.RequestId,
                1UL,
                AsrEventKind.Started);

            AsrEvent terminal = await ExecuteRecognitionAsync(
                    request,
                    2UL,
                    cancellationToken)
                .ConfigureAwait(false);
            yield return terminal;
        }

        private async ValueTask<AsrEvent> ExecuteRecognitionAsync(
            AsrRequest request,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            if (request.Options.RequestPartialResults)
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.ContractViolation,
                    "ASR_PARTIAL_RESULTS_UNSUPPORTED",
                    "Buffered OpenAI-compatible ASR does not provide partial results.",
                    retryable: false);
            }
            if (!SupportsLanguage(request.Options.LanguageTag))
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.UnsupportedLanguage,
                    "ASR_LANGUAGE_UNSUPPORTED",
                    "The selected ASR provider does not declare support for the requested language tag.",
                    retryable: false);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            TimeSpan effectiveTimeout = request.Context.Timeout <=
                TimeSpan.FromMilliseconds(sourceProfile.TimeoutMilliseconds)
                ? request.Context.Timeout
                : TimeSpan.FromMilliseconds(sourceProfile.TimeoutMilliseconds);
            timeout.CancelAfter(effectiveTimeout);

            BufferedAsrUtterance? utterance = null;
            byte[]? audio = null;
            byte[]? multipartBody = null;
            try
            {
                utterance = await utteranceSource.CaptureUtteranceAsync(
                        request.Context.RequestId,
                        options.MaximumAudioBytes,
                        options.MaximumUtteranceDuration,
                        timeout.Token)
                    .ConfigureAwait(false);
                if (utterance.Length > options.MaximumAudioBytes)
                {
                    throw new InvalidDataException(
                        "ASR utterance exceeds the configured byte bound.");
                }
                if (utterance.Duration > options.MaximumUtteranceDuration)
                {
                    throw new InvalidDataException(
                        "ASR utterance exceeds the configured duration bound.");
                }

                audio = utterance.CopyAudioBytes();
                multipartBody = BuildMultipartBody(
                    request.Context.RequestId,
                    sourceProfile.GetModelId(ReachyProviderModelRole.Asr),
                    NormalizeLanguage(request.Options.LanguageTag),
                    utterance.Format,
                    audio,
                    options.MaximumAudioBytes);

                using var transportRequest = new ReachyHttpTransportRequest(
                    HttpMethod.Post,
                    options.RelativeTranscriptionPath,
                    multipartBody,
                    CreateMultipartContentType(request.Context.RequestId),
                    "application/json",
                    ReachyHttpResponseMode.Buffered,
                    options.MaximumResponseBytes,
                    maximumSseEventCharacters: 1024,
                    explicitlyAuthorizeNonIdempotentRetry: false,
                    idempotencyKey: null);
                ReachyHttpTransportResult result = await transport.SendAsync(
                        transportRequest,
                        eventSink: null,
                        timeout.Token)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return FailureFromTransport(
                        request.Context.RequestId,
                        sequence,
                        result,
                        cancellationToken.IsCancellationRequested,
                        timeout.IsCancellationRequested);
                }

                string transcript;
                try
                {
                    string response = DecodeStrictUtf8(result.ResponseBody);
                    transcript = new ReachyAsrTranscriptionProtocolParser(
                        response,
                        MaximumJsonDepth,
                        MaximumTranscriptCharacters).Parse();
                }
                catch (FormatException)
                {
                    return FailureEvent(
                        request.Context.RequestId,
                        sequence,
                        SpeechErrorCategory.MalformedResponse,
                        "ASR_RESPONSE_MALFORMED",
                        "ASR provider returned malformed transcription JSON.",
                        retryable: false);
                }

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        request.Context.RequestId,
                        sequence,
                        AsrEventKind.NoMatch);
                }

                return new AsrEvent(
                    Descriptor.InstanceId,
                    request.Context.RequestId,
                    sequence,
                    AsrEventKind.FinalResult,
                    transcript);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        request.Context.RequestId,
                        sequence,
                        AsrEventKind.Cancelled);
                }
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.Timeout,
                    "ASR_TIMEOUT",
                    "ASR capture or transcription exceeded its bounded timeout.",
                    retryable: true);
            }
            catch (Exception exception) when (
                exception is InvalidDataException ||
                exception is ArgumentException ||
                exception is KeyNotFoundException ||
                exception is InvalidOperationException)
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.ContractViolation,
                    "ASR_REQUEST_INVALID",
                    "ASR request could not be completed because its bounded provider contract was not satisfied.",
                    retryable: false);
            }
            finally
            {
                if (multipartBody != null)
                {
                    Array.Clear(multipartBody, 0, multipartBody.Length);
                }
                if (audio != null)
                {
                    Array.Clear(audio, 0, audio.Length);
                }
                utterance?.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ownsTransport)
            {
                transport.Dispose();
            }
            await utteranceSource.DisposeAsync().ConfigureAwait(false);
        }

        private bool SupportsLanguage(string languageTag)
        {
            for (int index = 0; index < Capabilities.SupportedLanguageTags.Count; ++index)
            {
                if (string.Equals(
                        Capabilities.SupportedLanguageTags[index],
                        languageTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private string? FindMissingSecretReference()
        {
            if (options.AuthenticationMode ==
                ReachyCompatibleAsrAuthenticationMode.BearerCredentialReference)
            {
                string reference = sourceProfile.CredentialReference ??
                    throw new InvalidOperationException(
                        "Bearer ASR configuration omitted its credential reference.");
                if (sourceSecretStore == null || !sourceSecretStore.Contains(reference))
                {
                    return reference;
                }
            }

            for (int index = 0; index < sourceProfile.Headers.Count; ++index)
            {
                ReachyProviderHeaderBinding header = sourceProfile.Headers[index];
                if (header.ValueKind != ReachyProviderHeaderValueKind.SecretReference)
                {
                    continue;
                }
                if (sourceSecretStore == null ||
                    !sourceSecretStore.Contains(header.ValueOrSecretReference))
                {
                    return header.ValueOrSecretReference;
                }
            }
            return null;
        }

    }
}
