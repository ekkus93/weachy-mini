#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class LocalVlmAdapterDescriptor
    {
        public LocalVlmAdapterDescriptor(
            string runtimeId,
            string runtimeInstanceId,
            string displayName,
            string version)
        {
            RuntimeId = LocalVlmModelIdentity.RequireIdentifier(
                runtimeId,
                nameof(runtimeId));
            RuntimeInstanceId = LocalVlmModelIdentity.RequireIdentifier(
                runtimeInstanceId,
                nameof(runtimeInstanceId));
            DisplayName = LocalVlmModelIdentity.RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            Version = LocalVlmModelIdentity.RequireBoundedText(
                version,
                nameof(version),
                128);
        }

        public string RuntimeId { get; }

        public string RuntimeInstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }
    }

    public sealed class LocalVlmAdapterCapabilities
    {
        public LocalVlmAdapterCapabilities(
            bool canLoadModels,
            bool canCreateProviders,
            bool supportsCancellation,
            int maximumConcurrentLoads)
        {
            if (canCreateProviders && !canLoadModels)
            {
                throw new ArgumentException(
                    "An adapter cannot create providers without loading models.",
                    nameof(canCreateProviders));
            }

            bool operational = canLoadModels || canCreateProviders;
            if (operational)
            {
                if (!supportsCancellation)
                {
                    throw new ArgumentException(
                        "Operational local VLM adapters must support cancellation.",
                        nameof(supportsCancellation));
                }
                if (maximumConcurrentLoads <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumConcurrentLoads));
                }
            }
            else
            {
                if (supportsCancellation)
                {
                    throw new ArgumentException(
                        "An unavailable adapter cannot claim operational cancellation support.",
                        nameof(supportsCancellation));
                }
                if (maximumConcurrentLoads != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumConcurrentLoads));
                }
            }

            CanLoadModels = canLoadModels;
            CanCreateProviders = canCreateProviders;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentLoads = maximumConcurrentLoads;
        }

        public bool CanLoadModels { get; }

        public bool CanCreateProviders { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentLoads { get; }
    }

    public sealed class LocalVlmAdapterAvailability
    {
        public LocalVlmAdapterAvailability(
            LocalVlmAdapterState state,
            bool runtimePresent,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(LocalVlmAdapterState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            if (state == LocalVlmAdapterState.Available && !runtimePresent)
            {
                throw new ArgumentException(
                    "An available local VLM adapter must have a runtime present.",
                    nameof(runtimePresent));
            }
            if ((state == LocalVlmAdapterState.Unavailable ||
                    state == LocalVlmAdapterState.Disposed) &&
                runtimePresent)
            {
                throw new ArgumentException(
                    "Unavailable or disposed local VLM adapters cannot report a present runtime.",
                    nameof(runtimePresent));
            }

            State = state;
            RuntimePresent = runtimePresent;
            Diagnostic = ProviderDescriptor.RequireText(
                diagnostic,
                nameof(diagnostic));
        }

        public LocalVlmAdapterState State { get; }

        public bool RuntimePresent { get; }

        public string Diagnostic { get; }

        public bool CanCreateProvider =>
            State == LocalVlmAdapterState.Available && RuntimePresent;
    }

    public sealed class LocalVlmProviderConfiguration
    {
        public LocalVlmProviderConfiguration(
            LocalVlmModelManifest manifest,
            string localArtifactRoot,
            string providerInstanceId,
            bool artifactIntegrityVerified)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            LocalArtifactRoot = RequireLocalArtifactRoot(
                localArtifactRoot,
                nameof(localArtifactRoot));
            ProviderInstanceId = LocalVlmModelIdentity.RequireIdentifier(
                providerInstanceId,
                nameof(providerInstanceId));
            if (!artifactIntegrityVerified)
            {
                throw new ArgumentException(
                    "Local VLM artifacts must be verified against the manifest before adapter use.",
                    nameof(artifactIntegrityVerified));
            }
            ArtifactIntegrityVerified = true;
        }

        public LocalVlmModelManifest Manifest { get; }

        public string LocalArtifactRoot { get; }

        public string ProviderInstanceId { get; }

        public bool ArtifactIntegrityVerified { get; }

        private static string RequireLocalArtifactRoot(
            string value,
            string name)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Local VLM artifact roots cannot exceed 1024 characters.");
            }
            if (text.StartsWith("//", StringComparison.Ordinal) ||
                text[0] == (char)92)
            {
                throw new ArgumentException(
                    "Local VLM artifact roots cannot use UNC or network-share paths.",
                    name);
            }

            bool unixAbsolutePath = text[0] == '/';
            bool windowsDrivePath =
                text.Length >= 3 &&
                ((text[0] >= 'A' && text[0] <= 'Z') ||
                    (text[0] >= 'a' && text[0] <= 'z')) &&
                text[1] == ':' &&
                (text[2] == '/' || text[2] == (char)92);
            if (unixAbsolutePath || windowsDrivePath)
            {
                return text;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(
                    "Local VLM artifact roots must be absolute local paths, hostless file URIs, or Android content URIs.",
                    name);
            }

            if (string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(uri.Host) ||
                    !string.IsNullOrEmpty(uri.UserInfo))
                {
                    throw new ArgumentException(
                        "Local VLM file URIs cannot name a remote host or credentials.",
                        name);
                }
                return text;
            }

            if (string.Equals(
                    uri.Scheme,
                    "content",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                uri.Port == -1)
            {
                return text;
            }

            throw new ArgumentException(
                "Local VLM artifact roots must be absolute local paths, hostless file URIs, or authority-bearing Android content URIs; network locations are forbidden.",
                name);
        }
    }
}
