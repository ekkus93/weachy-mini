#nullable enable

using ReachyMini.Providers;

namespace ReachyMini.AppState
{
    // RMA-195 phase D: screen-side glue for ReachyCloudLlmCredentialCoordinator,
    // following the same lazily-constructed, screen-owned-coordinator pattern
    // as ReachyMainScreen.StorageCleanup.cs (RequestSimulationReset/
    // CleanupRecoverableStorage). Draft fields are ephemeral UI-only state --
    // they are never part of ReachySettingsSnapshot -- and are only committed
    // to the durable stores when the corresponding Save/Authorize action runs.
    public sealed partial class ReachyMainScreen
    {
        private ReachyCloudLlmCredentialCoordinator? cloudLlmCredentials;

        private string cloudLlmBaseUrlDraft = string.Empty;
        private string cloudLlmModelIdDraft = string.Empty;
        private string cloudLlmApiKeyDraft = string.Empty;
        private ReachyProviderEndpointStyle cloudLlmEndpointStyleDraft =
            ReachyProviderEndpointStyle.ChatCompletions;
        private string cloudLlmStatus =
            "Cloud LLM is off by default. The API key is stored via Android Keystore " +
            "and is never displayed again once saved.";

        public string CloudLlmStatus => cloudLlmStatus;

        public string CloudLlmBaseUrlDraft
        {
            get => cloudLlmBaseUrlDraft;
            set => cloudLlmBaseUrlDraft = value ?? string.Empty;
        }

        public string CloudLlmModelIdDraft
        {
            get => cloudLlmModelIdDraft;
            set => cloudLlmModelIdDraft = value ?? string.Empty;
        }

        public string CloudLlmApiKeyDraft
        {
            get => cloudLlmApiKeyDraft;
            set => cloudLlmApiKeyDraft = value ?? string.Empty;
        }

        public ReachyProviderEndpointStyle CloudLlmEndpointStyleDraft =>
            cloudLlmEndpointStyleDraft;

        public void CycleCloudLlmEndpointStyle()
        {
            cloudLlmEndpointStyleDraft =
                cloudLlmEndpointStyleDraft == ReachyProviderEndpointStyle.ChatCompletions
                    ? ReachyProviderEndpointStyle.Responses
                    : ReachyProviderEndpointStyle.ChatCompletions;
        }

        // Only ChatCompletions/Responses are reachable through
        // CycleCloudLlmEndpointStyle; the other ReachyProviderEndpointStyle
        // members (AudioTranscriptions/AudioSpeech) belong to ASR/TTS
        // profiles, not this LLM settings section.
        private static string GetCloudLlmEndpointStyleLabel(
            ReachyProviderEndpointStyle style)
        {
            return style switch
            {
                ReachyProviderEndpointStyle.ChatCompletions => "Chat Completions",
                ReachyProviderEndpointStyle.Responses => "Responses",
                _ => style.ToString(),
            };
        }

        public void SaveCloudLlmProfile()
        {
            ReachyCloudLlmCredentialCoordinator coordinator = RequireCloudLlmCredentials();
            cloudLlmStatus = coordinator.SaveProfile(
                cloudLlmBaseUrlDraft,
                cloudLlmEndpointStyleDraft,
                cloudLlmModelIdDraft);
        }

        public void SaveCloudLlmApiKey()
        {
            ReachyCloudLlmCredentialCoordinator coordinator = RequireCloudLlmCredentials();
            cloudLlmStatus = coordinator.SaveApiKey(cloudLlmApiKeyDraft);
            // The draft never survives a save attempt, successful or not --
            // this is the one field that must never linger in memory or be
            // redrawn back into the text widget.
            cloudLlmApiKeyDraft = string.Empty;
        }

        public void AuthorizeCloudLlm()
        {
            ReachyCloudLlmCredentialCoordinator coordinator = RequireCloudLlmCredentials();
            cloudLlmStatus = coordinator.GrantAuthorization();
        }

        public void RevokeCloudLlmAuthorization()
        {
            ReachyCloudLlmCredentialCoordinator coordinator = RequireCloudLlmCredentials();
            cloudLlmStatus = coordinator.RevokeAuthorization();
        }

        private ReachyCloudLlmCredentialCoordinator RequireCloudLlmCredentials()
        {
            if (cloudLlmCredentials == null)
            {
                cloudLlmCredentials = new ReachyCloudLlmCredentialCoordinator();
                cloudLlmCredentials.Initialize();
            }
            return cloudLlmCredentials;
        }
    }
}
