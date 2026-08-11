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
        private static ReachyHttpTransportError ClassifyStatus(
            HttpStatusCode statusCode,
            string? providerRequestId)
        {
            int status = (int)statusCode;
            if (status == 401)
            {
                return Error(
                    ReachyHttpErrorCategory.Authentication,
                    ReachyHttpTimeoutPhase.None,
                    status,
                    providerRequestId,
                    false,
                    "HTTP provider rejected authentication.");
            }
            if (status == 403)
            {
                return Error(
                    ReachyHttpErrorCategory.Permission,
                    ReachyHttpTimeoutPhase.None,
                    status,
                    providerRequestId,
                    false,
                    "HTTP provider rejected permission for the request.");
            }
            if (status == 408)
            {
                return Error(
                    ReachyHttpErrorCategory.Timeout,
                    ReachyHttpTimeoutPhase.ConnectionOrHeaders,
                    status,
                    providerRequestId,
                    true,
                    "HTTP provider reported a request timeout.");
            }
            if (status == 429)
            {
                return Error(
                    ReachyHttpErrorCategory.QuotaOrRateLimited,
                    ReachyHttpTimeoutPhase.None,
                    status,
                    providerRequestId,
                    true,
                    "HTTP provider reported a quota or rate-limit condition.");
            }
            if (status >= 500 && status <= 599)
            {
                return Error(
                    ReachyHttpErrorCategory.Server,
                    ReachyHttpTimeoutPhase.None,
                    status,
                    providerRequestId,
                    true,
                    "HTTP provider returned a server error.");
            }
            if (status >= 300 && status <= 399)
            {
                return Error(
                    ReachyHttpErrorCategory.RedirectRejected,
                    ReachyHttpTimeoutPhase.None,
                    status,
                    providerRequestId,
                    false,
                    "HTTP provider redirect was rejected; provider endpoints do not follow redirects implicitly.");
            }
            return Error(
                ReachyHttpErrorCategory.Client,
                ReachyHttpTimeoutPhase.None,
                status,
                providerRequestId,
                false,
                "HTTP provider rejected the request.");
        }

        private static ReachyHttpTransportError ClassifyRequestException(
            HttpRequestException exception)
        {
            bool tls = exception.InnerException is AuthenticationException ||
                exception.InnerException is IOException io &&
                    io.InnerException is AuthenticationException;
            return tls
                ? Error(
                    ReachyHttpErrorCategory.Tls,
                    ReachyHttpTimeoutPhase.ConnectionOrHeaders,
                    null,
                    null,
                    false,
                    "HTTP TLS negotiation or certificate validation failed.")
                : Error(
                    ReachyHttpErrorCategory.Network,
                    ReachyHttpTimeoutPhase.ConnectionOrHeaders,
                    null,
                    null,
                    true,
                    "HTTP connection failed before response headers completed.");
        }

        private bool IsPermittedFinalUri(Uri uri)
        {
            if (!uri.IsAbsoluteUri ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.Equals(
                    uri.Host,
                    profile.BaseUri.Host,
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Port != profile.BaseUri.Port)
            {
                return false;
            }
            if (profile.TlsMode == ReachyProviderTlsMode.RequireHttps)
            {
                return string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase) &&
                ReachyProviderConfigurationValidation.IsLocalDevelopmentHost(uri);
        }

        private static string? ReadProviderRequestId(HttpResponseMessage response)
        {
            string[] names =
            {
                "x-request-id",
                "request-id",
                "openai-request-id",
            };
            for (int index = 0; index < names.Length; ++index)
            {
                if (!response.Headers.TryGetValues(names[index], out IEnumerable<string>? values))
                {
                    continue;
                }
                foreach (string value in values)
                {
                    if (IsBoundedVisibleAscii(value, 256))
                    {
                        return value;
                    }
                }
            }
            return null;
        }

        private TimeSpan ComputeBackoff(int completedAttempts, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue)
            {
                double milliseconds = Math.Min(
                    retryAfter.Value.TotalMilliseconds,
                    policy.MaximumBackoffMilliseconds);
                return TimeSpan.FromMilliseconds(Math.Max(0.0, milliseconds));
            }
            long multiplier = 1L << Math.Min(completedAttempts - 1, 10);
            long millisecondsValue = Math.Min(
                checked((long)policy.BaseBackoffMilliseconds * multiplier),
                policy.MaximumBackoffMilliseconds);
            return TimeSpan.FromMilliseconds(millisecondsValue);
        }

        private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter == null)
            {
                return null;
            }
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return ClampRetryAfter(response.Headers.RetryAfter.Delta.Value);
            }
            if (response.Headers.RetryAfter.Date.HasValue)
            {
                TimeSpan delay = response.Headers.RetryAfter.Date.Value -
                    DateTimeOffset.UtcNow;
                return ClampRetryAfter(delay);
            }
            return null;
        }

        private TimeSpan ClampRetryAfter(TimeSpan delay)
        {
            double milliseconds = Math.Max(
                0.0,
                Math.Min(delay.TotalMilliseconds, policy.MaximumBackoffMilliseconds));
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        private static ReachyHttpTransportResult WithAttempts(
            ReachyHttpTransportResult result,
            int attempts)
        {
            if (result.Succeeded)
            {
                return ReachyHttpTransportResult.Success(
                    result.BorrowResponseBodyForTransport(),
                    result.HttpStatusCode ?? throw new InvalidOperationException(
                        "Successful HTTP result omitted status code."),
                    result.ProviderRequestId,
                    attempts);
            }
            return ReachyHttpTransportResult.Failure(
                result.Error ?? throw new InvalidOperationException(
                    "Failed HTTP result omitted error."),
                attempts);
        }

        private static ReachyHttpTransportError Error(
            ReachyHttpErrorCategory category,
            ReachyHttpTimeoutPhase timeoutPhase,
            int? statusCode,
            string? providerRequestId,
            bool retryable,
            string detail)
        {
            return new ReachyHttpTransportError(
                category,
                timeoutPhase,
                statusCode,
                providerRequestId,
                retryable,
                detail);
        }

        private sealed class ZeroingByteArrayContent : ByteArrayContent
        {
            private byte[]? ownedBuffer;

            public ZeroingByteArrayContent(byte[] buffer)
                : base(buffer ?? throw new ArgumentNullException(nameof(buffer)))
            {
                ownedBuffer = buffer;
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    base.Dispose(disposing);
                }
                finally
                {
                    if (disposing)
                    {
                        byte[]? buffer = Interlocked.Exchange(ref ownedBuffer, null);
                        if (buffer != null)
                        {
                            Array.Clear(buffer, 0, buffer.Length);
                        }
                    }
                }
            }
        }

        private static HttpClient CreateProductionClient()
        {
            return CreateClient(new HttpClientHandler());
        }

        private static HttpClient CreateClient(HttpMessageHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (handler is HttpClientHandler clientHandler)
            {
                clientHandler.AllowAutoRedirect = false;
            }
            return new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        private static bool IsBoundedVisibleAscii(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (character < 0x20 || character > 0x7e)
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class AttemptResult
        {
            private AttemptResult(
                ReachyHttpTransportResult result,
                TimeSpan? retryAfter,
                bool responseProgressObserved)
            {
                Result = result;
                RetryAfter = retryAfter;
                ResponseProgressObserved = responseProgressObserved;
            }

            public ReachyHttpTransportResult Result { get; }

            public TimeSpan? RetryAfter { get; }

            public bool ResponseProgressObserved { get; }

            public static AttemptResult Success(
                ReachyHttpTransportResult result,
                bool responseProgressObserved = false)
            {
                return new AttemptResult(result, null, responseProgressObserved);
            }

            public static AttemptResult Failure(
                ReachyHttpTransportError error,
                TimeSpan? retryAfter = null,
                bool responseProgressObserved = false)
            {
                return new AttemptResult(
                    ReachyHttpTransportResult.Failure(error, 1),
                    retryAfter,
                    responseProgressObserved);
            }
        }

        private sealed class TimedReadResult
        {
            private TimedReadResult(int count, ReachyHttpTransportError? error)
            {
                Count = count;
                Error = error;
            }

            public int Count { get; }

            public ReachyHttpTransportError? Error { get; }

            public static TimedReadResult Success(int count)
            {
                return new TimedReadResult(count, null);
            }

            public static TimedReadResult Failure(ReachyHttpTransportError error)
            {
                return new TimedReadResult(0, error);
            }
        }

        private sealed class ReadResult
        {
            private ReadResult(
                byte[]? body,
                int bytesObserved,
                ReachyHttpTransportError? error)
            {
                Body = body;
                BytesObserved = bytesObserved;
                Error = error;
            }

            public byte[]? Body { get; }

            public int BytesObserved { get; }

            public ReachyHttpTransportError? Error { get; }

            public static ReadResult Success(byte[] body, int bytesObserved)
            {
                return new ReadResult(body, bytesObserved, null);
            }

            public static ReadResult Failure(
                ReachyHttpTransportError error,
                int bytesObserved)
            {
                return new ReadResult(null, bytesObserved, error);
            }
        }

        private sealed class SseReadResult
        {
            private SseReadResult(
                int eventsDelivered,
                ReachyHttpTransportError? error)
            {
                EventsDelivered = eventsDelivered;
                Error = error;
            }

            public int EventsDelivered { get; }

            public ReachyHttpTransportError? Error { get; }

            public static SseReadResult Success(int eventsDelivered)
            {
                return new SseReadResult(eventsDelivered, null);
            }

            public static SseReadResult Failure(
                ReachyHttpTransportError error,
                int eventsDelivered)
            {
                return new SseReadResult(eventsDelivered, error);
            }
        }
    }
}
