#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace ReachyMini.Providers
{
    internal sealed class ReachyBearerCredentialTransportBinding
    {
        private ReachyBearerCredentialTransportBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore secretStore)
        {
            Profile = profile;
            SecretStore = secretStore;
        }

        public ReachyProviderProfile Profile { get; }

        public IReachyProviderSecretStore SecretStore { get; }

        public static ReachyBearerCredentialTransportBinding Create(
            ReachyProviderProfile source,
            IReachyProviderSecretStore secretStore,
            string internalSecretReference)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (secretStore == null)
            {
                throw new ArgumentNullException(nameof(secretStore));
            }
            ReachyProviderSecretReference.Validate(internalSecretReference);
            string sourceReference = source.CredentialReference ??
                throw new ArgumentException(
                    "Bearer-authenticated provider profiles require a primary credential reference.",
                    nameof(source));

            for (int index = 0; index < source.Headers.Count; ++index)
            {
                ReachyProviderHeaderBinding header = source.Headers[index];
                if (string.Equals(
                        header.Name,
                        "Authorization",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Bearer-authenticated provider profiles cannot also configure an Authorization header.",
                        nameof(source));
                }
                if (header.ValueKind == ReachyProviderHeaderValueKind.SecretReference &&
                    string.Equals(
                        header.ValueOrSecretReference,
                        internalSecretReference,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Provider profile uses an adapter-reserved bearer secret reference.",
                        nameof(source));
                }
            }

            var models = new ReachyProviderModelBinding[source.ModelBindings.Count];
            for (int index = 0; index < models.Length; ++index)
            {
                models[index] = source.ModelBindings[index];
            }

            var headers = new ReachyProviderHeaderBinding[source.Headers.Count + 1];
            for (int index = 0; index < source.Headers.Count; ++index)
            {
                headers[index] = source.Headers[index];
            }
            headers[headers.Length - 1] = new ReachyProviderHeaderBinding(
                "Authorization",
                ReachyProviderHeaderValueKind.SecretReference,
                internalSecretReference);

            var transportProfile = new ReachyProviderProfile(
                source.ProviderId,
                source.DisplayName,
                source.BaseUri,
                source.EndpointStyle,
                models,
                headers,
                source.TimeoutMilliseconds,
                source.StreamingEnabled,
                source.TlsMode,
                credentialReference: null);

            return new ReachyBearerCredentialTransportBinding(
                transportProfile,
                new BearerSecretStore(
                    secretStore,
                    sourceReference,
                    internalSecretReference));
        }

        private sealed class BearerSecretStore : IReachyProviderSecretStore
        {
            private const int MaximumCredentialBytes = 16 * 1024;
            private static readonly byte[] BearerPrefix =
                Encoding.ASCII.GetBytes("Bearer ");

            private readonly IReachyProviderSecretStore inner;
            private readonly string sourceReference;
            private readonly string internalReference;

            public BearerSecretStore(
                IReachyProviderSecretStore inner,
                string sourceReference,
                string internalReference)
            {
                this.inner = inner;
                ReachyProviderSecretReference.Validate(sourceReference);
                ReachyProviderSecretReference.Validate(internalReference);
                this.sourceReference = sourceReference;
                this.internalReference = internalReference;
            }

            public void Put(string reference, byte[] secretUtf8)
            {
                if (string.Equals(reference, internalReference, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The bearer authorization mapping is read-only.");
                }
                inner.Put(reference, secretUtf8);
            }

            public byte[] Get(string reference)
            {
                if (!string.Equals(reference, internalReference, StringComparison.Ordinal))
                {
                    return inner.Get(reference);
                }

                byte[] source = inner.Get(sourceReference) ??
                    throw new InvalidOperationException(
                        "Provider secret store returned null credential material.");
                try
                {
                    if (source.Length == 0 || source.Length > MaximumCredentialBytes)
                    {
                        throw new InvalidOperationException(
                            "Bearer credential material is empty or exceeds the credential bound.");
                    }
                    for (int index = 0; index < source.Length; ++index)
                    {
                        if (source[index] < 0x21 || source[index] > 0x7e)
                        {
                            throw new InvalidOperationException(
                                "Bearer credential material must be visible ASCII without whitespace.");
                        }
                    }

                    var result = new byte[checked(BearerPrefix.Length + source.Length)];
                    Buffer.BlockCopy(BearerPrefix, 0, result, 0, BearerPrefix.Length);
                    Buffer.BlockCopy(source, 0, result, BearerPrefix.Length, source.Length);
                    return result;
                }
                finally
                {
                    Array.Clear(source, 0, source.Length);
                }
            }

            public bool Contains(string reference)
            {
                return string.Equals(reference, internalReference, StringComparison.Ordinal)
                    ? inner.Contains(sourceReference)
                    : inner.Contains(reference);
            }

            public bool Delete(string reference)
            {
                if (string.Equals(reference, internalReference, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The bearer authorization mapping is read-only.");
                }
                return inner.Delete(reference);
            }
        }
    }
}
