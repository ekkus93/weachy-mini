#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;
using ReachyMini.Performance;
using ReachyMini.Providers;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyLocalLlmProviderApplicationService
    {
        private async Task<(ReachyOpenAiCompatibleLlmProviderBase?, string)> EnsureCloudLoadedAsync(
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (cloudProvider != null)
                {
                    return (cloudProvider, string.Empty);
                }
            }

            await cloudLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (sync)
                {
                    if (cloudProvider != null)
                    {
                        return (cloudProvider, string.Empty);
                    }
                }
                return await LoadCloudAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cloudLoadGate.Release();
            }
        }

        // Not async: every path below is synchronous today (profile/secret lookups and
        // fallback-policy evaluation are all in-memory). The cancellationToken parameter is
        // kept for signature symmetry with EnsureCloudLoadedAsync and for when a real network
        // preflight is added here.
        private Task<(ReachyOpenAiCompatibleLlmProviderBase?, string)> LoadCloudAsync(
            CancellationToken cancellationToken)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                const string detail = "Cloud LLM generation requires an Android device.";
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>((null, detail));
            }

            PublishSnapshot(
                ReachyProviderServiceExecutionState.Loading,
                string.Empty,
                "Resolving the configured cloud LLM provider profile.");

            ReachyProviderProfilePersistenceStore profileStore = EnsureCloudProfileStore();
            if (!profileStore.TryGet(CloudLlmProfileProviderId, out ReachyProviderProfile? profile) ||
                profile == null)
            {
                const string detail = "No cloud LLM provider profile is configured.";
                PublishSnapshot(ReachyProviderServiceExecutionState.NotLoaded, string.Empty, detail);
                return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>((null, detail));
            }

            IReachyProviderSecretStore? secretStore = EnsureCloudSecretStore();
            if (secretStore == null)
            {
                const string detail = "Cloud LLM credential storage is unavailable on this platform.";
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>((null, detail));
            }

            ReachyAuthorizedProviderSwitch? authorization;
            string authorizationFailureDetail;
            (authorization, authorizationFailureDetail) = EvaluateCloudSwitchAuthorization(profile);
            if (authorization == null)
            {
                PublishSnapshot(
                    ReachyProviderServiceExecutionState.Suspended,
                    string.Empty,
                    authorizationFailureDetail);
                return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>(
                    (null, authorizationFailureDetail));
            }
            authorization.Consume(
                ReachyProviderWorkloadKind.Llm,
                CloudLlmSwitchSourceProviderId,
                profile.ProviderId);

            ReachyOpenAiCompatibleLlmProviderBase built;
            try
            {
                ReachyLlmCapabilities capabilities = ReachyLlmCapabilities.TextOnly();
                built = profile.EndpointStyle == ReachyProviderEndpointStyle.Responses
                    ? new OpenAiResponsesLlmProvider(
                        profile,
                        secretStore,
                        ReachyOpenAiCompatibleLlmOptions.OpenAiResponsesDefault(),
                        capabilities)
                    : new OpenAiChatCompletionsLlmProvider(
                        profile,
                        secretStore,
                        ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                        capabilities);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is ArgumentOutOfRangeException)
            {
                string detail = "Cloud LLM provider profile is invalid for this adapter: " +
                    exception.Message;
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>((null, detail));
            }

            lock (sync)
            {
                cloudProvider = built;
            }
            PublishSnapshot(
                ReachyProviderServiceExecutionState.Ready,
                profile.ProviderId,
                "Cloud LLM provider loaded and ready.");
            return Task.FromResult<(ReachyOpenAiCompatibleLlmProviderBase?, string)>(
                (built, string.Empty));
        }

        private (ReachyAuthorizedProviderSwitch?, string) EvaluateCloudSwitchAuthorization(
            ReachyProviderProfile profile)
        {
            ReachyProviderFallbackPolicyEngine engine = EnsureFallbackPolicyEngine();
            var switchRequest = new ReachyProviderSwitchRequest(
                new ReachyProviderEndpointIdentity(
                    ReachyProviderWorkloadKind.Llm,
                    CloudLlmSwitchSourceProviderId,
                    ReachyProviderPrivacyBoundary.OnDevice),
                new ReachyProviderEndpointIdentity(
                    ReachyProviderWorkloadKind.Llm,
                    profile.ProviderId,
                    // Classified from the configured endpoint rather than
                    // assumed Cloud (RMA-195, 2026-08-22): a LAN-hosted
                    // OpenAI-compatible server is LocalNetwork, and reporting
                    // it as Cloud misstated where data actually goes. Both
                    // boundaries gate identically -- see
                    // ReachyProviderPrivacyBoundaryClassifier.
                    ReachyProviderPrivacyBoundaryClassifier.Classify(profile.BaseUri)),
                "cloud-llm-provider-selected");

            ReachyFallbackDecision initial = engine.EvaluateProviderSwitch(switchRequest);
            if (initial.Status == ReachyFallbackDecisionStatus.Authorized)
            {
                return (initial.Authorization, string.Empty);
            }
            if (initial.Status != ReachyFallbackDecisionStatus.RequiresPrivacyConfirmation)
            {
                return (
                    null,
                    "Cloud LLM access is not authorized by the fallback policy (" +
                        initial.DiagnosticCode +
                        "). No settings surface exists yet to grant this authorization.");
            }

            ReachyPrivacyBoundaryConfirmation confirmation =
                engine.ConfirmPrivacyBoundaryChange(switchRequest);
            ReachyFallbackDecision confirmed = engine.EvaluateProviderSwitch(
                switchRequest,
                confirmation);
            if (confirmed.Status != ReachyFallbackDecisionStatus.Authorized ||
                confirmed.Authorization == null)
            {
                return (
                    null,
                    "Cloud LLM privacy-boundary confirmation did not authorize the switch (" +
                        confirmed.DiagnosticCode +
                        ").");
            }
            return (confirmed.Authorization, string.Empty);
        }

        private ReachyProviderProfilePersistenceStore EnsureCloudProfileStore()
        {
            lock (sync)
            {
                if (cloudProfileStore == null)
                {
                    cloudProfileStore = new ReachyProviderProfilePersistenceStore();
                    cloudProfileStore.Initialize();
                }
                return cloudProfileStore;
            }
        }

        // Loads from ReachyFallbackPolicyPersistenceStore rather than handing
        // back a bare in-memory engine that always starts at NoFallback() --
        // otherwise a grant made through ReachyCloudLlmCredentialCoordinator's
        // settings UI (which persists to the same default file) would never
        // become visible here. Note this is still only read once and cached
        // for this service instance's lifetime: a grant/revoke made after the
        // first cloud generation attempt this process is not picked up until
        // the service is re-created. There is no live call site yet, so in
        // practice configuration always happens before any attempt exists.
        private ReachyProviderFallbackPolicyEngine EnsureFallbackPolicyEngine()
        {
            lock (sync)
            {
                if (fallbackPolicyEngine == null)
                {
                    fallbackPolicyEngine = new ReachyProviderFallbackPolicyEngine();
                    ReachyFallbackPolicyPersistenceStore fallbackPolicyStore =
                        new ReachyFallbackPolicyPersistenceStore(fallbackPolicyEngine);
                    fallbackPolicyStore.Initialize();
                }
                return fallbackPolicyEngine;
            }
        }

        private IReachyProviderSecretStore? EnsureCloudSecretStore()
        {
            lock (sync)
            {
                if (cloudSecretStore == null &&
                    Application.platform == RuntimePlatform.Android)
                {
                    cloudSecretStore = new ReachyAndroidProviderSecretStore();
                }
                return cloudSecretStore;
            }
        }

    }
}
