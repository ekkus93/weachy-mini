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
    public enum ReachyHttpResponseMode
    {
        Buffered = 0,
        ServerSentEvents = 1,
    }

    public enum ReachyHttpErrorCategory
    {
        None = 0,
        Cancelled = 1,
        Timeout = 2,
        Authentication = 3,
        Permission = 4,
        QuotaOrRateLimited = 5,
        Tls = 6,
        MalformedResponse = 7,
        Server = 8,
        Client = 9,
        Network = 10,
        ResponseTooLarge = 11,
        RedirectRejected = 12,
        Configuration = 13,
        Consumer = 14,
    }

    public enum ReachyHttpTimeoutPhase
    {
        None = 0,
        ConnectionOrHeaders = 1,
        ResponseRead = 2,
        RetryBackoff = 3,
    }

    public sealed class ReachyHttpTransportPolicy
    {
        public const int MinimumTimeoutMilliseconds = 250;
        public const int MaximumTimeoutMilliseconds = 300000;
        public const int MaximumAttemptsLimit = 4;
        public const int MaximumResponseBytesLimit = 16 * 1024 * 1024;
        public const int MaximumSseEventCharactersLimit = 1024 * 1024;

        public ReachyHttpTransportPolicy(
            int connectionTimeoutMilliseconds,
            int readTimeoutMilliseconds,
            int maximumAttempts,
            int baseBackoffMilliseconds,
            int maximumBackoffMilliseconds)
        {
            ValidateTimeout(connectionTimeoutMilliseconds, nameof(connectionTimeoutMilliseconds));
            ValidateTimeout(readTimeoutMilliseconds, nameof(readTimeoutMilliseconds));
            if (maximumAttempts < 1 || maximumAttempts > MaximumAttemptsLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            }
            if (baseBackoffMilliseconds < 0 ||
                baseBackoffMilliseconds > MaximumTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(baseBackoffMilliseconds));
            }
            if (maximumBackoffMilliseconds < baseBackoffMilliseconds ||
                maximumBackoffMilliseconds > MaximumTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBackoffMilliseconds));
            }

            ConnectionTimeoutMilliseconds = connectionTimeoutMilliseconds;
            ReadTimeoutMilliseconds = readTimeoutMilliseconds;
            MaximumAttempts = maximumAttempts;
            BaseBackoffMilliseconds = baseBackoffMilliseconds;
            MaximumBackoffMilliseconds = maximumBackoffMilliseconds;
        }

        public int ConnectionTimeoutMilliseconds { get; }

        public int ReadTimeoutMilliseconds { get; }

        public int MaximumAttempts { get; }

        public int BaseBackoffMilliseconds { get; }

        public int MaximumBackoffMilliseconds { get; }

        public static ReachyHttpTransportPolicy FromProfile(
            ReachyProviderProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            return new ReachyHttpTransportPolicy(
                profile.TimeoutMilliseconds,
                profile.TimeoutMilliseconds,
                1,
                250,
                2000);
        }

        private static void ValidateTimeout(int value, string name)
        {
            if (value < MinimumTimeoutMilliseconds ||
                value > MaximumTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public sealed class ReachyHttpTransportRequest : IDisposable
    {
        private byte[]? body;
        private int disposed;

        public ReachyHttpTransportRequest(
            HttpMethod method,
            string relativePath,
            byte[]? body,
            string? contentType,
            string? accept,
            ReachyHttpResponseMode responseMode,
            int maximumResponseBytes,
            int maximumSseEventCharacters,
            bool explicitlyAuthorizeNonIdempotentRetry,
            string? idempotencyKey)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RelativePath = ValidateRelativePath(relativePath);
            if (body != null && string.IsNullOrWhiteSpace(contentType))
            {
                throw new ArgumentException(
                    "HTTP request bodies require an explicit content type.",
                    nameof(contentType));
            }
            if (contentType != null && !IsBoundedHeaderValue(contentType, 256))
            {
                throw new ArgumentException(
                    "HTTP content type is invalid or too long.",
                    nameof(contentType));
            }
            if (accept != null && !IsBoundedHeaderValue(accept, 256))
            {
                throw new ArgumentException(
                    "HTTP Accept value is invalid or too long.",
                    nameof(accept));
            }
            if (!Enum.IsDefined(typeof(ReachyHttpResponseMode), responseMode))
            {
                throw new ArgumentOutOfRangeException(nameof(responseMode));
            }
            if (maximumResponseBytes < 1 ||
                maximumResponseBytes > ReachyHttpTransportPolicy.MaximumResponseBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            }
            if (maximumSseEventCharacters < 1 ||
                maximumSseEventCharacters >
                    ReachyHttpTransportPolicy.MaximumSseEventCharactersLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSseEventCharacters));
            }
            if (idempotencyKey != null && !IsIdempotencyKey(idempotencyKey))
            {
                throw new ArgumentException(
                    "HTTP idempotency keys must be bounded visible ASCII.",
                    nameof(idempotencyKey));
            }
            if (explicitlyAuthorizeNonIdempotentRetry && idempotencyKey == null)
            {
                throw new ArgumentException(
                    "Explicit non-idempotent retry authorization requires an idempotency key.",
                    nameof(idempotencyKey));
            }

            this.body = body == null ? null : (byte[])body.Clone();
            ContentType = contentType;
            Accept = accept;
            ResponseMode = responseMode;
            MaximumResponseBytes = maximumResponseBytes;
            MaximumSseEventCharacters = maximumSseEventCharacters;
            ExplicitlyAuthorizeNonIdempotentRetry =
                explicitlyAuthorizeNonIdempotentRetry;
            IdempotencyKey = idempotencyKey;
        }

        public HttpMethod Method { get; }

        public string RelativePath { get; }

        public bool HasBody => body != null;

        public int BodyLength => body?.Length ?? 0;

        public string? ContentType { get; }

        public string? Accept { get; }

        public ReachyHttpResponseMode ResponseMode { get; }

        public int MaximumResponseBytes { get; }

        public int MaximumSseEventCharacters { get; }

        public bool ExplicitlyAuthorizeNonIdempotentRetry { get; }

        public string? IdempotencyKey { get; }

        internal byte[]? CopyBodyForTransport()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyHttpTransportRequest));
            }
            byte[]? source = body;
            return source == null ? null : (byte[])source.Clone();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            byte[]? owned = Interlocked.Exchange(ref body, null);
            if (owned != null)
            {
                Array.Clear(owned, 0, owned.Length);
            }
        }

        public bool IsRetryEligibleMethod
        {
            get
            {
                string method = Method.Method;
                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return ExplicitlyAuthorizeNonIdempotentRetry &&
                    IdempotencyKey != null;
            }
        }

        private static string ValidateRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.Length > 512 ||
                relativePath[0] == '/' ||
                relativePath.IndexOf("\\", StringComparison.Ordinal) >= 0 ||
                relativePath.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                relativePath.IndexOf("?", StringComparison.Ordinal) >= 0 ||
                relativePath.IndexOf("#", StringComparison.Ordinal) >= 0 ||
                Uri.TryCreate(relativePath, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "HTTP endpoint paths must be bounded relative paths without traversal, query, or fragment components.",
                    nameof(relativePath));
            }
            return relativePath;
        }

        private static bool IsBoundedHeaderValue(string value, int maximumLength)
        {
            if (value.Length == 0 || value.Length > maximumLength)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (character == '\r' || character == '\n' || char.IsControl(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsIdempotencyKey(string value)
        {
            if (value.Length == 0 || value.Length > 128)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (character < 0x21 || character > 0x7e)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public sealed class ReachyHttpTransportError
    {
        public ReachyHttpTransportError(
            ReachyHttpErrorCategory category,
            ReachyHttpTimeoutPhase timeoutPhase,
            int? httpStatusCode,
            string? providerRequestId,
            bool retryable,
            string detail)
        {
            if (category == ReachyHttpErrorCategory.None ||
                !Enum.IsDefined(typeof(ReachyHttpErrorCategory), category))
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }
            if (!Enum.IsDefined(typeof(ReachyHttpTimeoutPhase), timeoutPhase))
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutPhase));
            }
            if (httpStatusCode.HasValue &&
                (httpStatusCode.Value < 100 || httpStatusCode.Value > 599))
            {
                throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
            }
            if (providerRequestId != null && !IsSafeDiagnostic(providerRequestId, 256))
            {
                throw new ArgumentException(
                    "Provider request IDs must be bounded visible diagnostic text.",
                    nameof(providerRequestId));
            }
            if (!IsSafeDiagnostic(detail, 512))
            {
                throw new ArgumentException(
                    "HTTP transport diagnostics must be bounded visible text.",
                    nameof(detail));
            }

            Category = category;
            TimeoutPhase = timeoutPhase;
            HttpStatusCode = httpStatusCode;
            ProviderRequestId = providerRequestId;
            Retryable = retryable;
            Detail = detail;
        }

        public ReachyHttpErrorCategory Category { get; }

        public ReachyHttpTimeoutPhase TimeoutPhase { get; }

        public int? HttpStatusCode { get; }

        public string? ProviderRequestId { get; }

        public bool Retryable { get; }

        public string Detail { get; }

        private static bool IsSafeDiagnostic(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (character == '\r' || character == '\n' || char.IsControl(character))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public sealed class ReachyHttpTransportResult
    {
        private readonly byte[]? responseBody;

        private ReachyHttpTransportResult(
            bool succeeded,
            byte[]? responseBody,
            int? httpStatusCode,
            string? providerRequestId,
            int attempts,
            ReachyHttpTransportError? error)
        {
            Succeeded = succeeded;
            this.responseBody = responseBody;
            HttpStatusCode = httpStatusCode;
            ProviderRequestId = providerRequestId;
            Attempts = attempts;
            Error = error;
        }

        public bool Succeeded { get; }

        public bool HasResponseBody => responseBody != null;

        public ReadOnlyMemory<byte> ResponseBody => responseBody == null
            ? ReadOnlyMemory<byte>.Empty
            : new ReadOnlyMemory<byte>(responseBody);

        public int? HttpStatusCode { get; }

        public string? ProviderRequestId { get; }

        public int Attempts { get; }

        public ReachyHttpTransportError? Error { get; }

        internal byte[]? BorrowResponseBodyForTransport()
        {
            return responseBody;
        }

        internal static ReachyHttpTransportResult Success(
            byte[]? responseBody,
            int httpStatusCode,
            string? providerRequestId,
            int attempts)
        {
            if (httpStatusCode < 200 || httpStatusCode > 299)
            {
                throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
            }
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts));
            }
            return new ReachyHttpTransportResult(
                true,
                responseBody,
                httpStatusCode,
                providerRequestId,
                attempts,
                null);
        }

        internal static ReachyHttpTransportResult Failure(
            ReachyHttpTransportError error,
            int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts));
            }
            return new ReachyHttpTransportResult(
                false,
                null,
                error?.HttpStatusCode,
                error?.ProviderRequestId,
                attempts,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }

    public sealed class ReachyServerSentEvent
    {
        public ReachyServerSentEvent(string eventName, string data, string eventId)
        {
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
        }

        public string EventName { get; }

        public string Data { get; }

        public string EventId { get; }
    }

    public interface IReachyServerSentEventSink
    {
        ValueTask OnEventAsync(
            ReachyServerSentEvent serverSentEvent,
            CancellationToken cancellationToken);
    }

    public interface IReachyHttpBackoffDelay
    {
        ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class ReachyTaskHttpBackoffDelay : IReachyHttpBackoffDelay
    {
        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            return new ValueTask(Task.Delay(delay, cancellationToken));
        }
    }

}
