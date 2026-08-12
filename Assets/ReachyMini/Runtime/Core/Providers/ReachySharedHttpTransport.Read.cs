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

namespace ReachyMini.Providers
{
    public sealed partial class ReachySharedHttpTransport
    {
        private HttpRequestMessage CreateRequestMessage(
            ReachyHttpTransportRequest request)
        {
            string baseUrl = profile.BaseUri.AbsoluteUri.EndsWith('/')
                ? profile.BaseUri.AbsoluteUri
                : profile.BaseUri.AbsoluteUri + "/";
            var message = new HttpRequestMessage(
                request.Method,
                new Uri(
                    new Uri(baseUrl, UriKind.Absolute),
                    request.RelativePath));
            try
            {
                byte[]? requestBody = request.CopyBodyForTransport();
                if (requestBody != null)
                {
                    var content = new ZeroingByteArrayContent(requestBody);
                    if (!content.Headers.TryAddWithoutValidation(
                            "Content-Type",
                            request.ContentType ??
                                throw new InvalidOperationException(
                                    "Validated HTTP request body lost its content type.")))
                    {
                        content.Dispose();
                        throw new InvalidOperationException(
                            "HTTP request content type could not be applied.");
                    }
                    message.Content = content;
                }

                if (request.Accept != null &&
                    !message.Headers.TryAddWithoutValidation(
                        "Accept",
                        request.Accept))
                {
                    throw new InvalidOperationException(
                        "HTTP request Accept header could not be applied.");
                }

                if (request.IdempotencyKey != null &&
                    !message.Headers.TryAddWithoutValidation(
                        "Idempotency-Key",
                        request.IdempotencyKey))
                {
                    throw new InvalidOperationException(
                        "HTTP request idempotency key could not be applied.");
                }

                ApplyProfileHeaders(message);
                return message;
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }

        private void ApplyProfileHeaders(HttpRequestMessage message)
        {
            for (int index = 0; index < profile.Headers.Count; ++index)
            {
                ReachyProviderHeaderBinding binding = profile.Headers[index];
                if (binding.ValueKind == ReachyProviderHeaderValueKind.Literal)
                {
                    if (!message.Headers.TryAddWithoutValidation(
                            binding.Name,
                            binding.ValueOrSecretReference))
                    {
                        throw new InvalidOperationException(
                            "Configured literal provider header could not be applied.");
                    }
                    continue;
                }

                IReachyProviderSecretStore store = secretStore ??
                    throw new InvalidOperationException(
                        "Provider profile requires secret-backed headers but no secret store is configured.");
                byte[] secretBytes = store.GetSecret(
                        binding.ValueOrSecretReference) ??
                    throw new InvalidOperationException(
                        "Provider secret store returned null credential material.");
                try
                {
                    string secretValue = new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true).GetString(secretBytes);
                    if (string.IsNullOrWhiteSpace(secretValue) ||
                        secretValue.Contains('\r') ||
                        secretValue.Contains('\n'))
                    {
                        throw new InvalidOperationException(
                            "Provider secret header value is empty or contains forbidden line breaks.");
                    }
                    if (!message.Headers.TryAddWithoutValidation(
                            binding.Name,
                            secretValue))
                    {
                        throw new InvalidOperationException(
                            "Configured secret-backed provider header could not be applied.");
                    }
                }
                finally
                {
                    Array.Clear(secretBytes, 0, secretBytes.Length);
                }
            }
        }

        private async ValueTask<ReadResult> ReadBufferedAsync(
            Stream stream,
            int maximumBytes,
            int statusCode,
            string? providerRequestId,
            CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            byte[] buffer = new byte[ReadBufferBytes];
            int total = 0;
            while (true)
            {
                TimedReadResult read = await ReadWithTimeoutAsync(
                        stream,
                        buffer,
                        statusCode,
                        providerRequestId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read.Error != null)
                {
                    return ReadResult.Failure(read.Error, total);
                }
                if (read.Count == 0)
                {
                    return ReadResult.Success(output.ToArray(), total);
                }
                total = checked(total + read.Count);
                if (total > maximumBytes)
                {
                    return ReadResult.Failure(
                        Error(
                            ReachyHttpErrorCategory.ResponseTooLarge,
                            ReachyHttpTimeoutPhase.ResponseRead,
                            statusCode,
                            providerRequestId,
                            false,
                            "HTTP response exceeded the configured response bound while reading."),
                        total);
                }
                output.Write(buffer, 0, read.Count);
            }
        }

        private async ValueTask<SseReadResult> ReadSseAsync(
            Stream stream,
            IReachyServerSentEventSink eventSink,
            int maximumBytes,
            int maximumEventCharacters,
            int statusCode,
            string? providerRequestId,
            CancellationToken cancellationToken)
        {
            var parser = new ReachyServerSentEventParser(maximumEventCharacters);
            byte[] buffer = new byte[ReadBufferBytes];
            int total = 0;
            int eventsDelivered = 0;
            while (true)
            {
                TimedReadResult read = await ReadWithTimeoutAsync(
                        stream,
                        buffer,
                        statusCode,
                        providerRequestId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read.Error != null)
                {
                    return SseReadResult.Failure(read.Error, eventsDelivered);
                }
                if (read.Count == 0)
                {
                    try
                    {
                        ReachyHttpTransportError? finalError = parser.Complete(
                            statusCode,
                            providerRequestId);
                        return finalError == null
                            ? SseReadResult.Success(eventsDelivered)
                            : SseReadResult.Failure(finalError, eventsDelivered);
                    }
                    catch (DecoderFallbackException)
                    {
                        return SseReadResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.MalformedResponse,
                                ReachyHttpTimeoutPhase.ResponseRead,
                                statusCode,
                                providerRequestId,
                                false,
                                "Streaming HTTP response ended with invalid UTF-8."),
                            eventsDelivered);
                    }
                }
                total = checked(total + read.Count);
                if (total > maximumBytes)
                {
                    return SseReadResult.Failure(
                        Error(
                            ReachyHttpErrorCategory.ResponseTooLarge,
                            ReachyHttpTimeoutPhase.ResponseRead,
                            statusCode,
                            providerRequestId,
                            false,
                            "Streaming HTTP response exceeded the configured response bound."),
                        eventsDelivered);
                }

                IReadOnlyList<ReachyServerSentEvent> events;
                try
                {
                    events = parser.Append(
                        buffer,
                        read.Count,
                        statusCode,
                        providerRequestId);
                }
                catch (DecoderFallbackException)
                {
                    return SseReadResult.Failure(
                        Error(
                            ReachyHttpErrorCategory.MalformedResponse,
                            ReachyHttpTimeoutPhase.ResponseRead,
                            statusCode,
                            providerRequestId,
                            false,
                            "Streaming HTTP response contained invalid UTF-8."),
                        eventsDelivered);
                }
                catch (InvalidDataException)
                {
                    return SseReadResult.Failure(
                        Error(
                            ReachyHttpErrorCategory.MalformedResponse,
                            ReachyHttpTimeoutPhase.ResponseRead,
                            statusCode,
                            providerRequestId,
                            false,
                            "Streaming HTTP response violated the bounded SSE framing contract."),
                        eventsDelivered);
                }

                for (int index = 0; index < events.Count; ++index)
                {
                    try
                    {
                        await eventSink.OnEventAsync(events[index], cancellationToken)
                            .ConfigureAwait(false);
                        ++eventsDelivered;
                    }
                    catch (OperationCanceledException)
                    {
                        ReachyHttpErrorCategory category =
                            cancellationToken.IsCancellationRequested
                                ? ReachyHttpErrorCategory.Cancelled
                                : ReachyHttpErrorCategory.Consumer;
                        return SseReadResult.Failure(
                            Error(
                                category,
                                ReachyHttpTimeoutPhase.None,
                                statusCode,
                                providerRequestId,
                                false,
                                cancellationToken.IsCancellationRequested
                                    ? "Streaming HTTP response consumption was cancelled."
                                    : "Streaming HTTP response consumer cancelled without caller cancellation."),
                            eventsDelivered);
                    }
                    catch (Exception)
                    {
                        return SseReadResult.Failure(
                            Error(
                                ReachyHttpErrorCategory.Consumer,
                                ReachyHttpTimeoutPhase.None,
                                statusCode,
                                providerRequestId,
                                false,
                                "Streaming HTTP response consumer failed."),
                            eventsDelivered);
                    }
                }
            }
        }

        private async ValueTask<TimedReadResult> ReadWithTimeoutAsync(
            Stream stream,
            byte[] buffer,
            int statusCode,
            string? providerRequestId,
            CancellationToken cancellationToken)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            readTimeout.CancelAfter(policy.ReadTimeoutMilliseconds);
            try
            {
                int count = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        readTimeout.Token)
                    .ConfigureAwait(false);
                return TimedReadResult.Success(count);
            }
            catch (OperationCanceledException)
            {
                return TimedReadResult.Failure(
                    Error(
                        cancellationToken.IsCancellationRequested
                            ? ReachyHttpErrorCategory.Cancelled
                            : ReachyHttpErrorCategory.Timeout,
                        cancellationToken.IsCancellationRequested
                            ? ReachyHttpTimeoutPhase.None
                            : ReachyHttpTimeoutPhase.ResponseRead,
                        statusCode,
                        providerRequestId,
                        retryable: !cancellationToken.IsCancellationRequested,
                        cancellationToken.IsCancellationRequested
                            ? "HTTP response read was cancelled."
                            : "HTTP response read timeout expired."));
            }
            catch (IOException)
            {
                return TimedReadResult.Failure(
                    Error(
                        ReachyHttpErrorCategory.Network,
                        ReachyHttpTimeoutPhase.ResponseRead,
                        statusCode,
                        providerRequestId,
                        true,
                        "HTTP response stream failed while reading."));
            }
            catch (HttpRequestException)
            {
                return TimedReadResult.Failure(
                    Error(
                        ReachyHttpErrorCategory.Network,
                        ReachyHttpTimeoutPhase.ResponseRead,
                        statusCode,
                        providerRequestId,
                        true,
                        "HTTP response transport failed while reading."));
            }
        }

    }
}
