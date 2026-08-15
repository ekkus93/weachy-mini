#nullable enable

using System;
using ReachyMini.Security;
using System.Collections.Generic;

namespace ReachyMini.Providers
{
    public enum ReachyProviderEndpointStyle
    {
        Responses = 0,
        ChatCompletions = 1,
        AudioTranscriptions = 2,
        AudioSpeech = 3,
    }

    public enum ReachyProviderTlsMode
    {
        RequireHttps = 0,
        LocalDevelopmentCleartext = 1,
    }

    public enum ReachyProviderModelRole
    {
        Text = 0,
        Vision = 1,
        Asr = 2,
        Tts = 3,
    }

    public enum ReachyProviderHeaderValueKind
    {
        Literal = 0,
        SecretReference = 1,
    }

    public sealed class ReachyProviderModelBinding
    {
        public ReachyProviderModelBinding(
            ReachyProviderModelRole role,
            string modelId)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderModelRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
            if (!ReachyProviderConfigurationValidation.IsBoundedText(
                    modelId,
                    1,
                    160))
            {
                throw new ArgumentException(
                    "A provider model binding requires a bounded model identifier.",
                    nameof(modelId));
            }

            Role = role;
            ModelId = modelId;
        }

        public ReachyProviderModelRole Role { get; }

        public string ModelId { get; }
    }

    public sealed class ReachyProviderHeaderBinding
    {
        public ReachyProviderHeaderBinding(
            string name,
            ReachyProviderHeaderValueKind valueKind,
            string valueOrSecretReference)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderHeaderValueKind), valueKind))
            {
                throw new ArgumentOutOfRangeException(nameof(valueKind));
            }
            if (!ReachyProviderConfigurationValidation.IsHeaderName(name))
            {
                throw new ArgumentException(
                    "Provider header names must use RFC token characters only.",
                    nameof(name));
            }
            if (!ReachyProviderConfigurationValidation.IsBoundedText(
                    valueOrSecretReference,
                    1,
                    2048))
            {
                throw new ArgumentException(
                    "Provider header values and secret references must be bounded non-empty text.",
                    nameof(valueOrSecretReference));
            }
            if (ReachyProviderConfigurationValidation.IsReservedTransportHeaderName(name))
            {
                throw new ArgumentException(
                    "Provider profiles cannot override transport-owned HTTP headers.",
                    nameof(name));
            }
            if (valueKind == ReachyProviderHeaderValueKind.Literal &&
                ReachyProviderConfigurationValidation.IsSensitiveHeaderName(name))
            {
                throw new ArgumentException(
                    "Sensitive provider headers must use a secret reference, not a persisted literal value.",
                    nameof(valueKind));
            }
            if (valueKind == ReachyProviderHeaderValueKind.SecretReference)
            {
                ReachyProviderSecretReference.Validate(valueOrSecretReference);
            }

            Name = name;
            ValueKind = valueKind;
            ValueOrSecretReference = valueOrSecretReference;
        }

        public string Name { get; }

        public ReachyProviderHeaderValueKind ValueKind { get; }

        public string ValueOrSecretReference { get; }
    }

    public static class ReachyProviderSecretReference
    {
        public const int MaximumLength = 96;

        public static void Validate(string reference)
        {
            if (!ReachyProviderConfigurationValidation.IsIdentifier(
                    reference,
                    MaximumLength))
            {
                throw new ArgumentException(
                    "Provider secret references must use bounded lowercase identifier characters.",
                    nameof(reference));
            }
        }
    }

    public interface IReachyProviderSecretStore
    {
        void Put(string reference, byte[] secretUtf8);

        byte[] GetSecret(string reference);

        bool Contains(string reference);

        bool Delete(string reference);
    }

    public sealed class ReachyProviderProfile
    {
        public const int MinimumTimeoutMilliseconds = 1000;
        public const int MaximumTimeoutMilliseconds = 300000;
        public const int MaximumHeaders = 32;
        public const int MaximumModelBindings = 8;

        private readonly ReachyProviderModelBinding[] modelBindings;
        private readonly ReachyProviderHeaderBinding[] headers;

        public ReachyProviderProfile(
            string providerId,
            string displayName,
            Uri baseUri,
            ReachyProviderEndpointStyle endpointStyle,
            ReachyProviderModelBinding[] modelBindings,
            ReachyProviderHeaderBinding[] headers,
            int timeoutMilliseconds,
            bool streamingEnabled,
            ReachyProviderTlsMode tlsMode,
            string? credentialReference)
        {
            if (!ReachyProviderConfigurationValidation.IsIdentifier(
                    providerId,
                    64))
            {
                throw new ArgumentException(
                    "Provider identifiers must use bounded lowercase identifier characters.",
                    nameof(providerId));
            }
            if (!ReachyProviderConfigurationValidation.IsBoundedText(
                    displayName,
                    1,
                    96))
            {
                throw new ArgumentException(
                    "Provider display names must be bounded non-empty text.",
                    nameof(displayName));
            }
            BaseUri = ValidateBaseUri(baseUri, tlsMode);
            if (!Enum.IsDefined(typeof(ReachyProviderEndpointStyle), endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (timeoutMilliseconds < MinimumTimeoutMilliseconds ||
                timeoutMilliseconds > MaximumTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMilliseconds),
                    "Provider timeout is outside the supported range.");
            }
            if (credentialReference != null)
            {
                ReachyProviderSecretReference.Validate(credentialReference);
            }

            this.modelBindings = CopyAndValidateModels(modelBindings);
            this.headers = CopyAndValidateHeaders(headers);

            ProviderId = providerId;
            DisplayName = displayName;
            EndpointStyle = endpointStyle;
            TimeoutMilliseconds = timeoutMilliseconds;
            StreamingEnabled = streamingEnabled;
            TlsMode = tlsMode;
            CredentialReference = credentialReference;
            SecurityWarning = tlsMode == ReachyProviderTlsMode.LocalDevelopmentCleartext
                ? "LOCAL DEVELOPMENT ONLY: provider traffic uses cleartext HTTP and may expose request data or credentials on the local network."
                : string.Empty;
        }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public Uri BaseUri { get; }

        public ReachyProviderEndpointStyle EndpointStyle { get; }

        public IReadOnlyList<ReachyProviderModelBinding> ModelBindings =>
            Array.AsReadOnly(modelBindings);

        public IReadOnlyList<ReachyProviderHeaderBinding> Headers =>
            Array.AsReadOnly(headers);

        public int TimeoutMilliseconds { get; }

        public bool StreamingEnabled { get; }

        public ReachyProviderTlsMode TlsMode { get; }

        public string? CredentialReference { get; }

        public bool HasCredentialReference => CredentialReference != null;

        public bool UsesCleartextLocalDevelopmentTransport =>
            TlsMode == ReachyProviderTlsMode.LocalDevelopmentCleartext;

        public string SecurityWarning { get; }

        public string GetModelId(ReachyProviderModelRole role)
        {
            for (int index = 0; index < modelBindings.Length; ++index)
            {
                if (modelBindings[index].Role == role)
                {
                    return modelBindings[index].ModelId;
                }
            }
            throw new KeyNotFoundException(
                "The provider profile does not define the requested model role.");
        }

        public ReachyProviderProfileDiagnostic CreateRedactedDiagnostic()
        {
            string[] modelRoles = new string[modelBindings.Length];
            for (int index = 0; index < modelBindings.Length; ++index)
            {
                modelRoles[index] = modelBindings[index].Role.ToString();
            }
            string[] headerNames = new string[headers.Length];
            for (int index = 0; index < headers.Length; ++index)
            {
                headerNames[index] = headers[index].Name;
            }
            return new ReachyProviderProfileDiagnostic(
                ProviderId,
                DisplayName,
                BaseUri,
                EndpointStyle,
                modelRoles,
                headerNames,
                TimeoutMilliseconds,
                StreamingEnabled,
                TlsMode,
                HasCredentialReference,
                SecurityWarning);
        }

        private static Uri ValidateBaseUri(
            Uri baseUri,
            ReachyProviderTlsMode tlsMode)
        {
            if (baseUri == null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }
            ReachyNetworkEndpointSecurity.RequireValidHost(baseUri, nameof(baseUri));
            if (!string.IsNullOrEmpty(baseUri.UserInfo) ||
                !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment))
            {
                throw new ArgumentException(
                    "Provider base URLs cannot contain user-info, query strings, or fragments.",
                    nameof(baseUri));
            }

            if (tlsMode == ReachyProviderTlsMode.RequireHttps)
            {
                if (!string.Equals(
                        baseUri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Provider profiles require HTTPS unless the explicit local-development cleartext mode is selected.",
                        nameof(baseUri));
                }
            }
            else if (tlsMode == ReachyProviderTlsMode.LocalDevelopmentCleartext)
            {
                if (!string.Equals(
                        baseUri.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.OrdinalIgnoreCase) ||
                    !ReachyNetworkEndpointSecurity.IsTrustedLocalDevelopmentHost(baseUri))
                {
                    throw new ArgumentException(
                        "Cleartext provider profiles are restricted to explicit local-development hosts.",
                        nameof(baseUri));
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(tlsMode));
            }

            return new Uri(baseUri.AbsoluteUri, UriKind.Absolute);
        }

        private static ReachyProviderModelBinding[] CopyAndValidateModels(
            ReachyProviderModelBinding[] bindings)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }
            if (bindings.Length == 0 || bindings.Length > MaximumModelBindings)
            {
                throw new ArgumentException(
                    "Provider profiles require a bounded non-empty model binding set.",
                    nameof(bindings));
            }

            var roles = new HashSet<ReachyProviderModelRole>();
            var copy = new ReachyProviderModelBinding[bindings.Length];
            for (int index = 0; index < bindings.Length; ++index)
            {
                ReachyProviderModelBinding binding = bindings[index] ??
                    throw new ArgumentException(
                        "Provider model bindings cannot contain null.",
                        nameof(bindings));
                if (!roles.Add(binding.Role))
                {
                    throw new ArgumentException(
                        "Provider model roles must be unique within a profile.",
                        nameof(bindings));
                }
                copy[index] = binding;
            }
            return copy;
        }

        private static ReachyProviderHeaderBinding[] CopyAndValidateHeaders(
            ReachyProviderHeaderBinding[] bindings)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }
            if (bindings.Length > MaximumHeaders)
            {
                throw new ArgumentException(
                    "Provider profiles contain too many custom headers.",
                    nameof(bindings));
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var copy = new ReachyProviderHeaderBinding[bindings.Length];
            for (int index = 0; index < bindings.Length; ++index)
            {
                ReachyProviderHeaderBinding binding = bindings[index] ??
                    throw new ArgumentException(
                        "Provider header bindings cannot contain null.",
                        nameof(bindings));
                if (!names.Add(binding.Name))
                {
                    throw new ArgumentException(
                        "Provider header names must be unique within a profile.",
                        nameof(bindings));
                }
                copy[index] = binding;
            }
            return copy;
        }
    }

    public sealed class ReachyProviderProfileDiagnostic
    {
        private readonly string[] modelRoles;
        private readonly string[] headerNames;

        public ReachyProviderProfileDiagnostic(
            string providerId,
            string displayName,
            Uri baseUri,
            ReachyProviderEndpointStyle endpointStyle,
            string[] modelRoles,
            string[] headerNames,
            int timeoutMilliseconds,
            bool streamingEnabled,
            ReachyProviderTlsMode tlsMode,
            bool credentialConfigured,
            string securityWarning)
        {
            ProviderId = providerId;
            DisplayName = displayName;
            BaseUri = baseUri;
            EndpointStyle = endpointStyle;
            this.modelRoles = (string[])modelRoles.Clone();
            this.headerNames = (string[])headerNames.Clone();
            TimeoutMilliseconds = timeoutMilliseconds;
            StreamingEnabled = streamingEnabled;
            TlsMode = tlsMode;
            CredentialConfigured = credentialConfigured;
            SecurityWarning = securityWarning;
        }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public Uri BaseUri { get; }

        public ReachyProviderEndpointStyle EndpointStyle { get; }

        public IReadOnlyList<string> ModelRoles => Array.AsReadOnly(modelRoles);

        public IReadOnlyList<string> HeaderNames => Array.AsReadOnly(headerNames);

        public int TimeoutMilliseconds { get; }

        public bool StreamingEnabled { get; }

        public ReachyProviderTlsMode TlsMode { get; }

        public bool CredentialConfigured { get; }

        public string SecurityWarning { get; }
    }

    internal static class ReachyProviderConfigurationValidation
    {
        public static bool IsIdentifier(string? value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' || character == '_' || character == '-';
                if (!valid)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsBoundedText(
            string? value,
            int minimumLength,
            int maximumLength)
        {
            if (value == null ||
                value.Length < minimumLength ||
                value.Length > maximumLength)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (char.IsControl(character))
                {
                    return false;
                }
            }
            return !string.IsNullOrWhiteSpace(value);
        }

        public static bool IsHeaderName(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128)
            {
                return false;
            }
            const string extraTokenCharacters = "!#$%&'*+-.^_`|~";
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    extraTokenCharacters.Contains(character);
                if (!valid)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsReservedTransportHeaderName(string name)
        {
            string normalized = name.ToLowerInvariant();
            return normalized == "host" ||
                normalized == "content-length" ||
                normalized == "content-type" ||
                normalized == "transfer-encoding" ||
                normalized == "connection" ||
                normalized == "proxy-connection" ||
                normalized == "expect" ||
                normalized == "upgrade" ||
                normalized == "te" ||
                normalized == "trailer" ||
                normalized == "idempotency-key";
        }

        public static bool IsSensitiveHeaderName(string name)
        {
            string normalized = name.ToLowerInvariant();
            return normalized.Contains("authorization") ||
                normalized.Contains("api-key") ||
                normalized.Contains("apikey") ||
                normalized.Contains("secret") ||
                normalized.Contains("token") ||
                normalized.Contains("cookie") ||
                normalized.EndsWith("-key", StringComparison.Ordinal);
        }


    }
}
