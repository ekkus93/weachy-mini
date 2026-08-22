#nullable enable

using System;
using System.Text;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase D (VLM half): the settings-facing counterpart to
    // ReachyAndroidPerceptionApplicationService's cloud VLM path
    // (ReachyAndroidPerceptionApplicationService.CloudVlm.cs), mirroring
    // ReachyCloudLlmCredentialCoordinator exactly -- see that class's header
    // comment for the shared design rationale (independent
    // ReachyProviderProfilePersistenceStore/ReachyFallbackPolicyPersistenceStore
    // instances pointed at the same default files, same in-process staleness
    // caveat if a grant is made after cloud analysis was already attempted once
    // this process).
    public sealed class ReachyCloudVlmCredentialCoordinator
    {
        public const string ProviderId =
            ReachyAndroidPerceptionApplicationService.CloudVlmProfileProviderId;
        public const string CredentialReference = "reachy-cloud-vlm-api-key";
        private const string AuthorizedPolicyName = "cloud-vlm-user-authorized";
        private const string ProfileDisplayName = "Cloud VLM (OpenAI-compatible)";
        private const int TimeoutMilliseconds = 30000;

        private readonly ReachyProviderProfilePersistenceStore profileStore;
        private readonly ReachyFallbackPolicyPersistenceStore fallbackStore;
        private IReachyProviderSecretStore? secretStore;
        private bool initialized;

        public ReachyCloudVlmCredentialCoordinator()
            : this(
                new ReachyProviderProfilePersistenceStore(),
                new ReachyFallbackPolicyPersistenceStore(
                    new ReachyProviderFallbackPolicyEngine()))
        {
        }

        internal ReachyCloudVlmCredentialCoordinator(
            ReachyProviderProfilePersistenceStore profileStore,
            ReachyFallbackPolicyPersistenceStore fallbackStore)
        {
            this.profileStore = profileStore ??
                throw new ArgumentNullException(nameof(profileStore));
            this.fallbackStore = fallbackStore ??
                throw new ArgumentNullException(nameof(fallbackStore));
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }
            profileStore.Initialize();
            fallbackStore.Initialize();
            secretStore = Application.platform == RuntimePlatform.Android
                ? new ReachyAndroidProviderSecretStore()
                : null;
            initialized = true;
        }

        public bool SecretStoreAvailable => secretStore != null;

        public ReachyProviderProfile? CurrentProfile =>
            profileStore.TryGet(ProviderId, out ReachyProviderProfile? profile)
                ? profile
                : null;

        public bool HasApiKey =>
            secretStore != null && secretStore.Contains(CredentialReference);

        public bool IsAuthorized
        {
            get
            {
                ReachyFallbackPolicy policy =
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Vlm);
                return policy.AllowCrossProviderSwitch &&
                    policy.AllowNetworkProviderSwitch &&
                    policy.IsTargetAuthorized(ProviderId);
            }
        }

        public string SaveProfile(
            string baseUrl,
            ReachyProviderEndpointStyle endpointStyle,
            string modelId)
        {
            RequireInitialized();
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? baseUri) ||
                baseUri == null)
            {
                return "Enter a valid absolute base URL, e.g. https://api.openai.com.";
            }
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return "Enter a model ID, e.g. gpt-4o-mini.";
            }
            if (endpointStyle != ReachyProviderEndpointStyle.Responses &&
                endpointStyle != ReachyProviderEndpointStyle.ChatCompletions)
            {
                return "Cloud VLM only supports the Responses or Chat Completions endpoint style.";
            }

            try
            {
                // Local-network/loopback http:// endpoints (dev/test servers such as a
                // local Ollama instance reached via `adb reverse`) are deliberately
                // permitted here, not just https:// -- see
                // ReachyCloudLlmCredentialCoordinator.SaveProfile for the identical
                // reasoning. ReachyProviderProfile's own ValidateBaseUri still enforces
                // the real security boundary.
                ReachyProviderTlsMode tlsMode = string.Equals(
                        baseUri.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.OrdinalIgnoreCase)
                    ? ReachyProviderTlsMode.LocalDevelopmentCleartext
                    : ReachyProviderTlsMode.RequireHttps;
                ReachyProviderProfile profile = new ReachyProviderProfile(
                    ProviderId,
                    ProfileDisplayName,
                    baseUri,
                    endpointStyle,
                    new[]
                    {
                        new ReachyProviderModelBinding(
                            ReachyProviderModelRole.Vision,
                            modelId.Trim()),
                    },
                    Array.Empty<ReachyProviderHeaderBinding>(),
                    TimeoutMilliseconds,
                    streamingEnabled: false,
                    tlsMode,
                    CredentialReference);
                profileStore.Upsert(profile);
                return profile.UsesCleartextLocalDevelopmentTransport
                    ? "Cloud VLM provider profile saved (local-development cleartext). " +
                        profile.SecurityWarning
                    : "Cloud VLM provider profile saved. Add an API key below to enable it.";
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return $"Cloud VLM provider profile is invalid: {exception.Message}";
            }
        }

        public string SaveApiKey(string apiKey)
        {
            RequireInitialized();
            if (secretStore == null)
            {
                return "Cloud VLM credential storage requires an Android device (Keystore-backed).";
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "Enter an API key before saving.";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
            try
            {
                ReachyProviderCredentialLifecycle lifecycle = new ReachyProviderCredentialLifecycle(
                    profileStore,
                    secretStore);
                if (secretStore.Contains(CredentialReference))
                {
                    lifecycle.UpdateCredential(CredentialReference, bytes);
                }
                else
                {
                    lifecycle.CreateCredential(CredentialReference, bytes);
                }
                return "Cloud VLM API key saved to Android Keystore.";
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                return $"Cloud VLM API key was not saved: {exception.Message}";
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public string GrantAuthorization()
        {
            RequireInitialized();
            if (CurrentProfile == null)
            {
                return "Save a cloud VLM provider profile before authorizing cloud analysis.";
            }
            if (!HasApiKey)
            {
                return "Save an API key before authorizing cloud analysis.";
            }

            ReachyFallbackPolicy policy = new ReachyFallbackPolicy(
                AuthorizedPolicyName,
                allowLocalQualityReduction: false,
                allowSameProviderRetry: false,
                allowCrossProviderSwitch: true,
                allowNetworkProviderSwitch: true,
                new[] { ProviderId });
            fallbackStore.SetPolicy(ReachyProviderWorkloadKind.Vlm, policy);
            return "Cloud VLM authorized: camera frames may now be sent to the configured endpoint.";
        }

        public string RevokeAuthorization()
        {
            RequireInitialized();
            fallbackStore.SetPolicy(
                ReachyProviderWorkloadKind.Vlm,
                ReachyFallbackPolicy.NoFallback());
            return "Cloud VLM authorization revoked. Analysis requests will fail closed.";
        }

        private void RequireInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The cloud VLM credential coordinator was not initialized.");
            }
        }
    }
}
