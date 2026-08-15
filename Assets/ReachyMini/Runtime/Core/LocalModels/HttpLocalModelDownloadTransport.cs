#nullable enable

using System;
using ReachyMini.Security;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed class HttpLocalModelDownloadTransport : ILocalModelDownloadTransport
    {
        private readonly HttpClient client;

        public HttpLocalModelDownloadTransport(HttpClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<LocalModelDownloadResponse> OpenAsync(
            Uri sourceUri,
            long requestedOffset,
            CancellationToken cancellationToken)
        {
            if (sourceUri == null)
            {
                throw new ArgumentNullException(nameof(sourceUri));
            }
            if (requestedOffset < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedOffset));
            }

            LocalModelDownloadResponse? uriFailure = ValidateHttpsUri(sourceUri);
            if (uriFailure != null)
            {
                return uriFailure;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            request.Headers.AcceptEncoding.Clear();
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (requestedOffset > 0L)
            {
                request.Headers.Range = new RangeHeaderValue(requestedOffset, null);
            }

            HttpResponseMessage? response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                Uri? finalUri = response.RequestMessage?.RequestUri;
                if (finalUri == null)
                {
                    return LocalModelDownloadResponse.CreateRejected(
                        "The HTTP source did not report a final request URI.");
                }

                uriFailure = ValidateHttpsUri(finalUri);
                if (uriFailure != null)
                {
                    return uriFailure;
                }

                foreach (string encoding in response.Content.Headers.ContentEncoding)
                {
                    if (!string.Equals(
                            encoding,
                            "identity",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return LocalModelDownloadResponse.CreateRejected(
                            "Encoded HTTP model responses are not accepted because byte identity must match the manifest.");
                    }
                }

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    return LocalModelDownloadResponse.CreateRestartRequired(
                        "The HTTP source rejected the requested resume range.");
                }

                long responseOffset;
                long? totalSize;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    responseOffset = 0L;
                    totalSize = response.Content.Headers.ContentLength;
                }
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    ContentRangeHeaderValue? range =
                        response.Content.Headers.ContentRange;
                    if (range == null ||
                        !range.From.HasValue ||
                        !range.To.HasValue ||
                        range.Unit == null ||
                        !string.Equals(
                            range.Unit,
                            "bytes",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return LocalModelDownloadResponse.CreateRejected(
                            "The HTTP partial response is missing an exact byte Content-Range.");
                    }

                    responseOffset = range.From.Value;
                    totalSize = range.Length;
                    if (range.To.Value < range.From.Value)
                    {
                        return LocalModelDownloadResponse.CreateRejected(
                            "The HTTP partial response contains an invalid Content-Range.");
                    }
                }
                else
                {
                    int status = (int)response.StatusCode;
                    return LocalModelDownloadResponse.CreateRejected(
                        "The HTTP model source returned status " +
                        status.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ".");
                }

                Stream content = await response.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false);
                Stream? owned = new HttpResponseOwnedStream(response, content);
                response = null;
                try
                {
                    LocalModelDownloadResponse result =
                        LocalModelDownloadResponse.CreateContent(
                            owned,
                            responseOffset,
                            totalSize);
                    owned = null;
                    return result;
                }
                finally
                {
                    if (owned != null)
                    {
                        owned.Dispose();
                    }
                }
            }
            finally
            {
                if (response != null)
                {
                    response.Dispose();
                }
            }
        }

        private static LocalModelDownloadResponse? ValidateHttpsUri(Uri uri)
        {
            try
            {
                ReachyNetworkEndpointSecurity.RequirePublicHttpsUri(uri, nameof(uri));
            }
            catch (ArgumentException)
            {
                return LocalModelDownloadResponse.CreateRejected(
                    "HTTP model sources and redirects must remain public HTTPS URIs without credentials or fragments.");
            }

            return null;
        }

        private sealed class HttpResponseOwnedStream : Stream
        {
            private HttpResponseMessage? response;
            private Stream? inner;

            public HttpResponseOwnedStream(
                HttpResponseMessage response,
                Stream inner)
            {
                this.response = response ??
                    throw new ArgumentNullException(nameof(response));
                this.inner = inner ??
                    throw new ArgumentNullException(nameof(inner));
            }

            public override bool CanRead => RequireInner().CanRead;

            public override bool CanSeek => RequireInner().CanSeek;

            public override bool CanWrite => false;

            public override long Length => RequireInner().Length;

            public override long Position
            {
                get => RequireInner().Position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return RequireInner().Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                return RequireInner().ReadAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken);
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken)
            {
                return RequireInner().ReadAsync(buffer, cancellationToken);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return RequireInner().Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Stream? ownedInner = inner;
                    inner = null;
                    if (ownedInner != null)
                    {
                        ownedInner.Dispose();
                    }

                    HttpResponseMessage? ownedResponse = response;
                    response = null;
                    if (ownedResponse != null)
                    {
                        ownedResponse.Dispose();
                    }
                }

                base.Dispose(disposing);
            }

            private Stream RequireInner()
            {
                return inner ??
                    throw new ObjectDisposedException(nameof(HttpResponseOwnedStream));
            }
        }
    }
}
