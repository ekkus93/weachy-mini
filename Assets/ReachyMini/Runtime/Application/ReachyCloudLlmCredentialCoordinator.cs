#nullable enable

using System;
using System.Text;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase D: the settings-facing counterpart to
    // ReachyLocalLlmProviderApplicationService's cloud LLM path. That
    // service reads a ReachyProviderProfilePersistenceStore entry keyed by
    // CloudLlmProfileProviderId, an IReachyProviderSecretStore credential
    // keyed by CredentialReference, and a fallback-policy authorization for
    // the Llm workload -- but until this coordinator existed, nothing in
    // the app could write any of the three, so cloud LLM generation always
    // failed closed with "no cloud LLM provider profile is configured" or
    // was denied by the fallback policy. This class is what the settings UI
    // (ReachyMainScreen's Cloud LLM section) drives to create/update that
    // profile, store the API key, and grant the fallback-policy privacy-
    // boundary authorization.
    //
    // This coordinator and ReachyLocalLlmProviderApplicationService each
    // construct their own ReachyProviderProfilePersistenceStore /
    // ReachyFallbackPolicyPersistenceStore instances (no shared object
    // reference), but both use the default Application.persistentDataPath
    // location, so they read/write the same durable files. One real
    // consequence: if ReachyLocalLlmProviderApplicationService already
    // cached its in-memory fallback-policy engine this process (i.e. cloud
    // generation was already attempted once), a grant made afterward
    // through this coordinator is not visible to it until the process
    // restarts. There is no live call site yet (deferred to a later phase,
    // same as the local-LLM path), so in practice configuration always
    // happens before there is anything to race with.
    public sealed class ReachyCloudLlmCredentialCoordinator
    {
        public const string ProviderId =
            ReachyLocalLlmProviderApplicationService.CloudLlmProfileProviderId;
        public const string CredentialReference = "reachy-cloud-llm-api-key";
        private const string AuthorizedPolicyName = "cloud-llm-user-authorized";
        private const string ProfileDisplayName = "Cloud LLM (OpenAI-compatible)";
        private const int TimeoutMilliseconds = 30000;

        private readonly ReachyProviderProfilePersistenceStore profileStore;
        private readonly ReachyFallbackPolicyPersistenceStore fallbackStore;
        private IReachyProviderSecretStore? secretStore;
        private bool initialized;

        public ReachyCloudLlmCredentialCoordinator()
            : this(
                new ReachyProviderProfilePersistenceStore(),
                new ReachyFallbackPolicyPersistenceStore(
                    new ReachyProviderFallbackPolicyEngine()))
        {
        }

        internal ReachyCloudLlmCredentialCoordinator(
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
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Llm);
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
                return "Cloud LLM only supports the Responses or Chat Completions endpoint style.";
            }

            try
            {
                ReachyProviderProfile profile = new ReachyProviderProfile(
                    ProviderId,
                    ProfileDisplayName,
                    baseUri,
                    endpointStyle,
                    new[]
                    {
                        new ReachyProviderModelBinding(
                            ReachyProviderModelRole.Text,
                            modelId.Trim()),
                    },
                    Array.Empty<ReachyProviderHeaderBinding>(),
                    TimeoutMilliseconds,
                    streamingEnabled: false,
                    ReachyProviderTlsMode.RequireHttps,
                    CredentialReference);
                profileStore.Upsert(profile);
                return "Cloud LLM provider profile saved. Add an API key below to enable it.";
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return $"Cloud LLM provider profile is invalid: {exception.Message}";
            }
        }

        public string SaveApiKey(string apiKey)
        {
            RequireInitialized();
            if (secretStore == null)
            {
                return "Cloud LLM credential storage requires an Android device (Keystore-backed).";
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
                return "Cloud LLM API key saved to Android Keystore.";
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                return $"Cloud LLM API key was not saved: {exception.Message}";
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
                return "Save a cloud LLM provider profile before authorizing cloud generation.";
            }
            if (!HasApiKey)
            {
                return "Save an API key before authorizing cloud generation.";
            }

            ReachyFallbackPolicy policy = new ReachyFallbackPolicy(
                AuthorizedPolicyName,
                allowLocalQualityReduction: false,
                allowSameProviderRetry: false,
                allowCrossProviderSwitch: true,
                allowNetworkProviderSwitch: true,
                new[] { ProviderId });
            fallbackStore.SetPolicy(ReachyProviderWorkloadKind.Llm, policy);
            return "Cloud LLM authorized: conversation text may now be sent to the configured endpoint.";
        }

        public string RevokeAuthorization()
        {
            RequireInitialized();
            fallbackStore.SetPolicy(
                ReachyProviderWorkloadKind.Llm,
                ReachyFallbackPolicy.NoFallback());
            return "Cloud LLM authorization revoked. Generation requests will fail closed.";
        }

        private void RequireInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The cloud LLM credential coordinator was not initialized.");
            }
        }
    }
}
