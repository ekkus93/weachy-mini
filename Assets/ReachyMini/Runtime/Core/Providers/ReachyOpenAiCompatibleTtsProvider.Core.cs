#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public sealed partial class ReachyOpenAiCompatibleTtsProvider : ITtsProvider
    {
        private const string InternalBearerSecretReference =
            "reachy.internal.tts.bearer";

        private readonly ReachyProviderProfile sourceProfile;
        private readonly IReachyProviderSecretStore? sourceSecretStore;
        private readonly IBufferedTtsAudioSink audioSink;
        private readonly ReachyOpenAiCompatibleTtsOptions options;
        private readonly TtsVoice[] voices;
        private readonly ReachySharedHttpTransport transport;
        private readonly bool ownsTransport;
        private int disposed;

        public ReachyOpenAiCompatibleTtsProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            IBufferedTtsAudioSink audioSink,
            ReachyOpenAiCompatibleTtsOptions options,
            IEnumerable<TtsVoice> voices,
            SpeechProviderLocation location,
            string instanceId)
            : this(
                profile,
                secretStore,
                audioSink,
                options,
                voices,
                location,
                instanceId,
                transportOverride: null)
        {
        }

        internal ReachyOpenAiCompatibleTtsProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            IBufferedTtsAudioSink audioSink,
            ReachyOpenAiCompatibleTtsOptions options,
            IEnumerable<TtsVoice> voices,
            SpeechProviderLocation location,
            string instanceId,
            ReachySharedHttpTransport? transportOverride)
        {
            sourceProfile = profile ?? throw new ArgumentNullException(nameof(profile));
            sourceSecretStore = secretStore;
            this.audioSink = audioSink ?? throw new ArgumentNullException(nameof(audioSink));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateProfile(profile, options.AuthenticationMode);
            ValidateLocation(location);
            this.voices = CopyVoices(voices);

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.TextToSpeech,
                profile.ProviderId,
                SpeechProviderDescriptor.RequireText(instanceId, nameof(instanceId)),
                profile.DisplayName,
                "rma-145-v1",
                location,
                SpeechNetworkRequirement.Required);
            Capabilities = new TtsCapabilities(
                supportsCancellation: true,
                options.MaximumInputCharacters);

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

        public TtsCapabilities Capabilities { get; }

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Unavailable,
                        "TTS provider is disposed."));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    Task.FromCanceled<SpeechProviderAvailability>(cancellationToken));
            }

            try
            {
                string? missing = FindMissingSecretReference();
                if (missing != null)
                {
                    return new ValueTask<SpeechProviderAvailability>(
                        new SpeechProviderAvailability(
                            SpeechAvailabilityState.SetupRequired,
                            "TTS provider credential material is not configured."));
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is ArgumentException)
            {
                return new ValueTask<SpeechProviderAvailability>(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Faulted,
                        "TTS provider credential storage could not be evaluated."));
            }

            return new ValueTask<SpeechProviderAvailability>(
                new SpeechProviderAvailability(
                    SpeechAvailabilityState.Available,
                    "TTS provider configuration is available."));
        }

        public ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyOpenAiCompatibleTtsProvider));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<IReadOnlyList<TtsVoice>>(
                    Task.FromCanceled<IReadOnlyList<TtsVoice>>(cancellationToken));
            }
            IReadOnlyList<TtsVoice> result = Array.AsReadOnly((TtsVoice[])voices.Clone());
            return new ValueTask<IReadOnlyList<TtsVoice>>(result);
        }

        public async IAsyncEnumerable<TtsEvent> SpeakAsync(
            TtsRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyOpenAiCompatibleTtsProvider));
            }
            if (!string.Equals(
                    request.Context.ProviderInstanceId,
                    Descriptor.InstanceId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "TTS request targets a different provider instance.",
                    nameof(request));
            }

            yield return new TtsEvent(
                Descriptor.InstanceId,
                request.Context.RequestId,
                1UL,
                TtsEventKind.Started);

            TtsEvent terminal = await ExecuteSpeechAsync(
                    request,
                    2UL,
                    cancellationToken)
                .ConfigureAwait(false);
            yield return terminal;
        }

        private async ValueTask<TtsEvent> ExecuteSpeechAsync(
            TtsRequest request,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            if (request.Text.Length > options.MaximumInputCharacters)
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.ContractViolation,
                    "TTS_INPUT_TOO_LONG",
                    "TTS input exceeds the configured provider character bound.",
                    retryable: false);
            }
            if (!HasVoice(request.VoiceId))
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.MissingVoiceData,
                    "TTS_VOICE_UNSUPPORTED",
                    "The selected TTS voice is not declared by this provider instance.",
                    retryable: false);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            TimeSpan effectiveTimeout = request.Context.Timeout <=
                TimeSpan.FromMilliseconds(sourceProfile.TimeoutMilliseconds)
                ? request.Context.Timeout
                : TimeSpan.FromMilliseconds(sourceProfile.TimeoutMilliseconds);
            timeout.CancelAfter(effectiveTimeout);

            byte[]? requestBody = null;
            byte[]? borrowedResponseBody = null;
            BufferedTtsAudio? audio = null;
            try
            {
                requestBody = BuildRequestBody(
                    sourceProfile.GetModelId(ReachyProviderModelRole.Tts),
                    request.Text,
                    request.VoiceId,
                    options.ResponseFormat,
                    options.Instructions);
                string accept = string.Join(", ", options.AcceptedResponseContentTypes);
                using var transportRequest = new ReachyHttpTransportRequest(
                    HttpMethod.Post,
                    options.RelativeSpeechPath,
                    requestBody,
                    "application/json",
                    accept,
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
                borrowedResponseBody = result.BorrowResponseBodyForTransport() ??
                    throw new InvalidOperationException(
                        "Successful TTS HTTP response omitted its response body.");
                if (!options.AcceptsContentType(result.ContentType))
                {
                    return FailureEvent(
                        request.Context.RequestId,
                        sequence,
                        SpeechErrorCategory.MalformedResponse,
                        "TTS_RESPONSE_CONTENT_TYPE_INVALID",
                        "TTS provider returned a missing or unexpected audio content type.",
                        retryable: false);
                }

                if (borrowedResponseBody.Length == 0)
                {
                    return FailureEvent(
                        request.Context.RequestId,
                        sequence,
                        SpeechErrorCategory.MalformedResponse,
                        "TTS_RESPONSE_EMPTY",
                        "TTS provider returned an empty audio response.",
                        retryable: false);
                }
                audio = new BufferedTtsAudio(
                    borrowedResponseBody,
                    options.ResponseFormat,
                    result.ContentType ?? throw new InvalidOperationException(
                        "Validated TTS response content type became unavailable."));
                try
                {
                    await audioSink.PlayAsync(
                            request.Context.RequestId,
                            audio,
                            timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return FailureEvent(
                        request.Context.RequestId,
                        sequence,
                        SpeechErrorCategory.ServiceFailure,
                        "TTS_AUDIO_SINK_FAILURE",
                        "TTS audio sink failed while consuming the generated audio.",
                        retryable: false);
                }

                return new TtsEvent(
                    Descriptor.InstanceId,
                    request.Context.RequestId,
                    sequence,
                    TtsEventKind.Completed);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        request.Context.RequestId,
                        sequence,
                        TtsEventKind.Cancelled);
                }
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.Timeout,
                    "TTS_TIMEOUT",
                    "TTS generation or audio handoff exceeded its bounded timeout.",
                    retryable: true);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is KeyNotFoundException)
            {
                return FailureEvent(
                    request.Context.RequestId,
                    sequence,
                    SpeechErrorCategory.ContractViolation,
                    "TTS_REQUEST_INVALID",
                    "TTS request could not be completed because its bounded provider contract was not satisfied.",
                    retryable: false);
            }
            finally
            {
                audio?.Dispose();
                if (borrowedResponseBody != null)
                {
                    Array.Clear(
                        borrowedResponseBody,
                        0,
                        borrowedResponseBody.Length);
                }
                if (requestBody != null)
                {
                    Array.Clear(requestBody, 0, requestBody.Length);
                }
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
            await audioSink.DisposeAsync().ConfigureAwait(false);
        }

        private bool HasVoice(string voiceId)
        {
            for (int index = 0; index < voices.Length; ++index)
            {
                if (string.Equals(
                        voices[index].VoiceId,
                        voiceId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private string? FindMissingSecretReference()
        {
            if (options.AuthenticationMode ==
                ReachyCompatibleTtsAuthenticationMode.BearerCredentialReference)
            {
                string reference = sourceProfile.CredentialReference ??
                    throw new InvalidOperationException(
                        "Bearer TTS configuration omitted its credential reference.");
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
