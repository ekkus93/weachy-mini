#nullable enable

using System;
using System.IO;
using System.Net;
using System.Text;

namespace ReachyMini.Security
{
    public enum ReachyImportedDocumentKind
    {
        CameraCalibration = 0,
        LocalModelManifest = 1,
        LocalVlmManifest = 2,
        DurableSettings = 3,
        ProviderProfiles = 4,
        FallbackPolicies = 5,
        LocalModelMetadata = 6,
    }

    public static class ReachyImportedContentPolicy
    {
        public const long MaximumCameraCalibrationBytes = 512L * 1024L;
        public const long MaximumLocalModelManifestBytes = 256L * 1024L;
        public const long MaximumLocalVlmManifestBytes = 256L * 1024L;
        public const long MaximumDurableSettingsBytes = 1024L * 1024L;
        public const long MaximumProviderProfilesBytes = 512L * 1024L;
        public const long MaximumFallbackPoliciesBytes = 256L * 1024L;
        public const long MaximumLocalModelMetadataBytes = 64L * 1024L;

        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public static long GetMaximumBytes(ReachyImportedDocumentKind kind)
        {
            return kind switch
            {
                ReachyImportedDocumentKind.CameraCalibration => MaximumCameraCalibrationBytes,
                ReachyImportedDocumentKind.LocalModelManifest => MaximumLocalModelManifestBytes,
                ReachyImportedDocumentKind.LocalVlmManifest => MaximumLocalVlmManifestBytes,
                ReachyImportedDocumentKind.DurableSettings => MaximumDurableSettingsBytes,
                ReachyImportedDocumentKind.ProviderProfiles => MaximumProviderProfilesBytes,
                ReachyImportedDocumentKind.FallbackPolicies => MaximumFallbackPoliciesBytes,
                ReachyImportedDocumentKind.LocalModelMetadata => MaximumLocalModelMetadataBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind."),
            };
        }

        public static string ReadBoundedUtf8File(
            string path,
            ReachyImportedDocumentKind kind)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Imported document paths cannot be empty.",
                    nameof(path));
            }

            long maximumBytes = GetMaximumBytes(kind);
            using FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                FileOptions.SequentialScan);
            if (stream.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The {kind} document exceeds the {maximumBytes}-byte input limit.");
            }

            using MemoryStream bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            long total = 0L;
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new InvalidDataException(
                        $"The {kind} document exceeded the {maximumBytes}-byte " +
                        "input limit while reading.");
                }
                bytes.Write(buffer, 0, read);
            }

            return DecodeStrictUtf8(bytes.ToArray(), kind);
        }

        public static string RequireBoundedUtf8Text(
            string text,
            ReachyImportedDocumentKind kind)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            long maximumBytes = GetMaximumBytes(kind);
            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(text);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException(
                    $"The {kind} document contains invalid UTF-16 input.",
                    exception);
            }

            if ((long)byteCount > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The {kind} document exceeds the {maximumBytes}-byte input limit.");
            }
            return text;
        }

        private static string DecodeStrictUtf8(
            byte[] bytes,
            ReachyImportedDocumentKind kind)
        {
            int offset = 0;
            if (bytes.Length >= 3 &&
                bytes[0] == 0xef &&
                bytes[1] == 0xbb &&
                bytes[2] == 0xbf)
            {
                offset = 3;
            }

            try
            {
                return StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    $"The {kind} document is not valid UTF-8.",
                    exception);
            }
        }
    }

    public static class ReachyNetworkEndpointSecurity
    {
        public const int MaximumUriCharacters = 2048;

        public static void RequireValidHost(Uri uri, string parameterName)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (!uri.IsAbsoluteUri ||
                uri.AbsoluteUri.Length > MaximumUriCharacters ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentException(
                    "Network endpoints require a bounded absolute URI with a host.",
                    parameterName);
            }

            string host = TrimIpv6Brackets(uri.Host);
            UriHostNameType hostType = Uri.CheckHostName(host);
            if (hostType != UriHostNameType.Dns &&
                hostType != UriHostNameType.IPv4 &&
                hostType != UriHostNameType.IPv6)
            {
                throw new ArgumentException(
                    "Network endpoint hosts must be valid DNS names or IP addresses.",
                    parameterName);
            }
            if (host.Length > 253)
            {
                throw new ArgumentException(
                    "Network endpoint hosts exceed the supported length.",
                    parameterName);
            }
        }

        public static bool IsTrustedLocalDevelopmentHost(Uri uri)
        {
            RequireValidHost(uri, nameof(uri));
            string host = TrimIpv6Brackets(uri.Host);
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host, out IPAddress? address) || address == null)
            {
                return false;
            }
            return IsPrivateOrLocalAddress(address);
        }

        public static bool IsPublicInternetHost(Uri uri)
        {
            RequireValidHost(uri, nameof(uri));
            string host = TrimIpv6Brackets(uri.Host);
            if (IsLocalOnlyDnsName(host))
            {
                return false;
            }

            if (IPAddress.TryParse(host, out IPAddress? address) && address != null)
            {
                return !IsPrivateOrLocalAddress(address);
            }

            return host.IndexOf('.') >= 0;
        }

        public static void RequirePublicHttpsUri(Uri uri, string parameterName)
        {
            RequireValidHost(uri, parameterName);
            if (!string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !IsPublicInternetHost(uri))
            {
                throw new ArgumentException(
                    "Remote model URLs must use public HTTPS hosts without credentials or fragments.",
                    parameterName);
            }
        }

        private static bool IsLocalOnlyDnsName(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateOrLocalAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrLocalAddress(address.MapToIPv4());
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return IsPrivateOrLocalIpv4(bytes);
            }
            if (bytes.Length != 16)
            {
                return true;
            }

            bool unspecified = true;
            for (int index = 0; index < bytes.Length; ++index)
            {
                if (bytes[index] != 0)
                {
                    unspecified = false;
                    break;
                }
            }
            if (unspecified)
            {
                return true;
            }

            return (bytes[0] & 0xfe) == 0xfc ||
                bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80 ||
                bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0 ||
                bytes[0] == 0xff;
        }

        private static bool IsPrivateOrLocalIpv4(byte[] bytes)
        {
            return bytes[0] == 0 ||
                bytes[0] == 10 ||
                bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127 ||
                bytes[0] == 127 ||
                bytes[0] == 169 && bytes[1] == 254 ||
                bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
                bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
                bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 ||
                bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19) ||
                bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                bytes[0] >= 224;
        }

        private static string TrimIpv6Brackets(string host)
        {
            if (host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']')
            {
                return host.Substring(1, host.Length - 2);
            }
            return host;
        }
    }

    public enum ReachyDiagnosticArtifactKind
    {
        RedactedText = 0,
        Secret = 1,
        PrivateMedia = 2,
    }

    public static class ReachyDiagnosticBundleSecurityPolicy
    {
        public const bool IncludeSecretsByDefault = false;
        public const bool IncludePrivateMediaByDefault = false;

        public static bool IsExportable(ReachyDiagnosticArtifactKind kind)
        {
            return kind == ReachyDiagnosticArtifactKind.RedactedText;
        }

        public static void RequireExportable(ReachyDiagnosticArtifactKind kind)
        {
            if (!Enum.IsDefined(typeof(ReachyDiagnosticArtifactKind), kind) ||
                !IsExportable(kind))
            {
                throw new InvalidOperationException(
                    "Diagnostic bundles can contain redacted text only; " +
                    "secrets and private media are denied.");
            }
        }
    }
}
