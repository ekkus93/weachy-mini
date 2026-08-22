#nullable enable

using ReachyMini.Providers;

namespace ReachyMini.AppState
{
    // RMA-195 phase D (VLM half): screen-side glue for
    // ReachyCloudVlmCredentialCoordinator, mirroring
    // ReachyMainScreen.CloudLlmCredentials.cs exactly.
    public sealed partial class ReachyMainScreen
    {
        private ReachyCloudVlmCredentialCoordinator? cloudVlmCredentials;

        private string cloudVlmBaseUrlDraft = string.Empty;
        private string cloudVlmModelIdDraft = string.Empty;
        private string cloudVlmApiKeyDraft = string.Empty;
        private ReachyProviderEndpointStyle cloudVlmEndpointStyleDraft =
            ReachyProviderEndpointStyle.ChatCompletions;
        private string cloudVlmStatus =
            "Cloud VLM is off by default. The API key is stored via Android Keystore " +
            "and is never displayed again once saved.";

        public string CloudVlmStatus => cloudVlmStatus;

        public string CloudVlmBaseUrlDraft
        {
            get => cloudVlmBaseUrlDraft;
            set => cloudVlmBaseUrlDraft = value ?? string.Empty;
        }

        public string CloudVlmModelIdDraft
        {
            get => cloudVlmModelIdDraft;
            set => cloudVlmModelIdDraft = value ?? string.Empty;
        }

        public string CloudVlmApiKeyDraft
        {
            get => cloudVlmApiKeyDraft;
            set => cloudVlmApiKeyDraft = value ?? string.Empty;
        }

        public ReachyProviderEndpointStyle CloudVlmEndpointStyleDraft =>
            cloudVlmEndpointStyleDraft;

        public void CycleCloudVlmEndpointStyle()
        {
            cloudVlmEndpointStyleDraft =
                cloudVlmEndpointStyleDraft == ReachyProviderEndpointStyle.ChatCompletions
                    ? ReachyProviderEndpointStyle.Responses
                    : ReachyProviderEndpointStyle.ChatCompletions;
        }

        public void SaveCloudVlmProfile()
        {
            ReachyCloudVlmCredentialCoordinator coordinator = RequireCloudVlmCredentials();
            cloudVlmStatus = coordinator.SaveProfile(
                cloudVlmBaseUrlDraft,
                cloudVlmEndpointStyleDraft,
                cloudVlmModelIdDraft);
        }

        public void SaveCloudVlmApiKey()
        {
            ReachyCloudVlmCredentialCoordinator coordinator = RequireCloudVlmCredentials();
            cloudVlmStatus = coordinator.SaveApiKey(cloudVlmApiKeyDraft);
            cloudVlmApiKeyDraft = string.Empty;
        }

        public void AuthorizeCloudVlm()
        {
            ReachyCloudVlmCredentialCoordinator coordinator = RequireCloudVlmCredentials();
            cloudVlmStatus = coordinator.GrantAuthorization();
        }

        public void RevokeCloudVlmAuthorization()
        {
            ReachyCloudVlmCredentialCoordinator coordinator = RequireCloudVlmCredentials();
            cloudVlmStatus = coordinator.RevokeAuthorization();
        }

        private ReachyCloudVlmCredentialCoordinator RequireCloudVlmCredentials()
        {
            if (cloudVlmCredentials == null)
            {
                cloudVlmCredentials = new ReachyCloudVlmCredentialCoordinator();
                cloudVlmCredentials.Initialize();
            }
            return cloudVlmCredentials;
        }
    }
}
