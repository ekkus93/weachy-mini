#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Performance;

namespace ReachyMini.Providers
{
    public sealed partial class ReachySharedHttpTransport : IDisposable
    {
        private const int ReadBufferBytes = 8192;

        private readonly ReachyProviderProfile profile;
        private readonly IReachyProviderSecretStore? secretStore;
        private readonly ReachyHttpTransportPolicy policy;
        private readonly IReachyHttpBackoffDelay backoffDelay;
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private int disposed;

        public ReachySharedHttpTransport(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyHttpTransportPolicy? policy = null)
            : this(
                profile,
                secretStore,
                policy,
                new ReachyTaskHttpBackoffDelay(),
                CreateProductionClient(),
                ownsClient: true)
        {
        }

        internal ReachySharedHttpTransport(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyHttpTransportPolicy? policy,
            IReachyHttpBackoffDelay backoffDelay,
            HttpMessageHandler handler)
            : this(
                profile,
                secretStore,
                policy,
                backoffDelay,
                CreateClient(handler),
                ownsClient: true)
        {
        }

        private ReachySharedHttpTransport(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyHttpTransportPolicy? policy,
            IReachyHttpBackoffDelay backoffDelay,
            HttpClient client,
            bool ownsClient)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.secretStore = secretStore;
            this.policy = policy ?? ReachyHttpTransportPolicy.FromProfile(profile);
            this.backoffDelay = backoffDelay ??
                throw new ArgumentNullException(nameof(backoffDelay));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.ownsClient = ownsClient;
        }

        public async ValueTask<ReachyHttpTransportResult> SendAsync(
            ReachyHttpTransportRequest request,
            IReachyServerSentEventSink? eventSink,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.ResponseMode == ReachyHttpResponseMode.ServerSentEvents &&
                eventSink == null)
            {
                throw new ArgumentNullException(nameof(eventSink));
            }
            if (request.ResponseMode == ReachyHttpResponseMode.Buffered && eventSink != null)
            {
                throw new ArgumentException(
                    "Buffered HTTP requests cannot receive a server-sent-event sink.",
                    nameof(eventSink));
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                return ReachyHttpTransportResult.Failure(
                    Error(
                        ReachyHttpErrorCategory.Configuration,
                        ReachyHttpTimeoutPhase.None,
                        null,
                        null,
                        false,
                        "HTTP transport is disposed."),
                    1);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return ReachyHttpTransportResult.Failure(
                    Error(
                        ReachyHttpErrorCategory.Cancelled,
                        ReachyHttpTimeoutPhase.None,
                        null,
                        null,
                        false,
                        "HTTP request was cancelled before transport start."),
                    1);
            }

            using ReachyPerformanceMeasurement measurement =
                ReachyPerformanceTelemetry.Measure(
                    ReachyPerformanceWorkload.Network);

            int attempts = 0;
            ReachyHttpTransportResult? lastFailure = null;
            while (attempts < policy.MaximumAttempts)
            {
                ++attempts;
                AttemptResult attempt = await SendAttemptAsync(
                        request,
                        eventSink,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (attempt.Result.Succeeded)
                {
                    return WithAttempts(attempt.Result, attempts);
                }

                lastFailure = WithAttempts(attempt.Result, attempts);
                ReachyHttpTransportError failure = lastFailure.Error ??
                    throw new InvalidOperationException(
                        "Failed HTTP transport attempt omitted its error.");
                bool retry = attempts < policy.MaximumAttempts &&
                    request.IsRetryEligibleMethod &&
                    failure.Retryable &&
                    !attempt.ResponseProgressObserved;
                if (!retry)
                {
                    return lastFailure;
                }

                TimeSpan delay = ComputeBackoff(attempts, attempt.RetryAfter);
                try
                {
                    await backoffDelay.DelayAsync(delay, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    bool callerCancelled = cancellationToken.IsCancellationRequested;
                    return ReachyHttpTransportResult.Failure(
                        Error(
                            callerCancelled
                                ? ReachyHttpErrorCategory.Cancelled
                                : ReachyHttpErrorCategory.Configuration,
                            ReachyHttpTimeoutPhase.RetryBackoff,
                            failure.HttpStatusCode,
                            failure.ProviderRequestId,
                            false,
                            callerCancelled
                                ? "HTTP request was cancelled during retry backoff."
                                : "HTTP retry backoff aborted without caller cancellation."),
                        attempts);
                }
            }

            return lastFailure ?? throw new InvalidOperationException(
                "HTTP transport exhausted attempts without a result.");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ownsClient)
            {
                client.Dispose();
            }
        }

        private async ValueTask<AttemptResult> SendAttemptAsync(
            ReachyHttpTransportRequest request,
            IReachyServerSentEventSink? eventSink,
            CancellationToken cancellationToken)
        {
            HttpRequestMessage? message = null;
            try
            {
                try
                {
                    message = CreateRequestMessage(request);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is KeyNotFoundException ||
                    exception is DecoderFallbackException)
                {
                    return AttemptResult.Failure(
                        Error(
                            ReachyHttpErrorCategory.Configuration,
                            ReachyHttpTimeoutPhase.None,
                            null,
                            null,
                            false,
                            "HTTP provider configuration or credential material is unavailable or invalid."));
                }

                HttpResponseMessage response;
                using (var connectionTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken))
                {
                    connectionTimeout.CancelAfter(
                        policy.ConnectionTimeoutMilliseconds);
                    try
                    {
                        response = await client.SendAsync(
                                message,
                                HttpCompletionOption.ResponseHeadersRead,
                                connectionTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        ReachyHttpErrorCategory category =
                            cancellationToken.IsCancellationRequested
                                ? ReachyHttpErrorCategory.Cancelled
                                : ReachyHttpErrorCategory.Timeout;
                        return AttemptResult.Failure(
                            Error(
                                category,
                                cancellationToken.IsCancellationRequested
                                    ? ReachyHttpTimeoutPhase.None
                                    : ReachyHttpTimeoutPhase.ConnectionOrHeaders,
                                null,
                                null,
                                retryable:
                                    !cancellationToken.IsCancellationRequested,
                                cancellationToken.IsCancellationRequested
                                    ? "HTTP request was cancelled before response headers completed."
                                    : "HTTP connection or response-header timeout expired."));
                    }
                    catch (HttpRequestException exception)
                    {
                        return AttemptResult.Failure(
                            ClassifyRequestException(exception));
                    }
                }

                using (response)
                {
                    string? providerRequestId = ReadProviderRequestId(response);
                    Uri? finalUri = response.RequestMessage?.RequestUri;
                    if (finalUri == null || !IsPermittedFinalUri(finalUri))
                    {
                        return AttemptResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.RedirectRejected,
                                ReachyHttpTimeoutPhase.None,
                                (int)response.StatusCode,
                                providerRequestId,
                                false,
                                "HTTP response final URI violated the configured provider transport boundary."));
                    }

                    int statusCode = (int)response.StatusCode;
                    if (statusCode < 200 || statusCode > 299)
                    {
                        return AttemptResult.Failure(
                            ClassifyStatus(
                                response.StatusCode,
                                providerRequestId),
                            ReadRetryAfter(response));
                    }

                    HttpContent? responseContent = response.Content;
                    if (responseContent == null)
                    {
                        if (request.ResponseMode ==
                            ReachyHttpResponseMode.ServerSentEvents)
                        {
                            return AttemptResult.Failure(
                                Error(
                                    ReachyHttpErrorCategory.MalformedResponse,
                                    ReachyHttpTimeoutPhase.None,
                                    statusCode,
                                    providerRequestId,
                                    false,
                                    "Streaming HTTP response omitted its response content."));
                        }
                        return AttemptResult.Success(
                            ReachyHttpTransportResult.Success(
                                Array.Empty<byte>(),
                                statusCode,
                                providerRequestId,
                                null,
                                1));
                    }

                    if (request.ResponseMode ==
                            ReachyHttpResponseMode.ServerSentEvents &&
                        !string.Equals(
                            responseContent.Headers.ContentType?.MediaType,
                            "text/event-stream",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return AttemptResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.MalformedResponse,
                                ReachyHttpTimeoutPhase.None,
                                statusCode,
                                providerRequestId,
                                false,
                                "Streaming HTTP response did not use text/event-stream content type."));
                    }

                    long? contentLength =
                        responseContent.Headers.ContentLength;
                    if (contentLength.HasValue &&
                        contentLength.Value > request.MaximumResponseBytes)
                    {
                        return AttemptResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.ResponseTooLarge,
                                ReachyHttpTimeoutPhase.None,
                                statusCode,
                                providerRequestId,
                                false,
                                "HTTP response Content-Length exceeds the configured response bound."));
                    }

                    Stream stream;
                    try
                    {
                        stream = await responseContent.ReadAsStreamAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is IOException ||
                        exception is HttpRequestException)
                    {
                        return AttemptResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.Network,
                                ReachyHttpTimeoutPhase.ResponseRead,
                                statusCode,
                                providerRequestId,
                                true,
                                "HTTP response stream could not be opened."));
                    }

                    string? responseContentType =
                        responseContent.Headers.ContentType?.MediaType;
                    using (stream)
                    {
                        if (request.ResponseMode ==
                            ReachyHttpResponseMode.Buffered)
                        {
                            ReadResult buffered = await ReadBufferedAsync(
                                    stream,
                                    request.MaximumResponseBytes,
                                    statusCode,
                                    providerRequestId,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (buffered.Error != null)
                            {
                                return AttemptResult.Failure(
                                    buffered.Error,
                                    responseProgressObserved:
                                        buffered.BytesObserved > 0);
                            }
                            return AttemptResult.Success(
                                ReachyHttpTransportResult.Success(
                                    buffered.Body,
                                    statusCode,
                                    providerRequestId,
                                    responseContentType,
                                    1));
                        }

                        SseReadResult sse = await ReadSseAsync(
                                stream,
                                eventSink ??
                                    throw new InvalidOperationException(
                                        "SSE request lost its event sink."),
                                request.MaximumResponseBytes,
                                request.MaximumSseEventCharacters,
                                statusCode,
                                providerRequestId,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (sse.Error != null)
                        {
                            return AttemptResult.Failure(
                                sse.Error,
                                responseProgressObserved:
                                    sse.EventsDelivered > 0);
                        }
                        return AttemptResult.Success(
                            ReachyHttpTransportResult.Success(
                                null,
                                statusCode,
                                providerRequestId,
                                responseContentType,
                                1),
                            responseProgressObserved:
                                sse.EventsDelivered > 0);
                    }
                }
            }
            finally
            {
                message?.Dispose();
            }
        }

    }
}
