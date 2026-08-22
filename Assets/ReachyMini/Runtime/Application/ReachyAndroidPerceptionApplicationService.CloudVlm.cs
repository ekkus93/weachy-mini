#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase D (VLM half): the cloud-VLM counterpart to
    // ReachyLocalLlmProviderApplicationService's cloud LLM path -- same
    // laziness discipline (nothing loads until AnalyzeSceneAsync is first
    // called), same ReachyProviderProfilePersistenceStore /
    // IReachyProviderSecretStore / ReachyProviderFallbackPolicyEngine
    // authorization flow, just for ReachyProviderWorkloadKind.Vlm instead of
    // .Llm. Deliberately excludes any live call site: the frame parameter is
    // caller-supplied because there is no established "capture a frame on
    // demand" path yet (the tracking driver's own frames are consumed
    // in-place by ReachyOnDeviceLightweightTracker, not exposed for reuse
    // here) -- exactly the same still-open gap the LLM path documents for
    // itself.
    // Outcome of AnalyzeCurrentSceneAsync: distinguishes "no camera frame
    // could be captured at all" (FrameCaptured == false, e.g. camera preview
    // toggle is off) from "a frame was captured and a real VLM call was
    // attempted" (FrameCaptured == true, Result holds whatever
    // AnalyzeSceneAsync returned, including its own possible failure
    // statuses). Callers that only want a best-effort scene description to
    // fold into an LLM prompt should treat both FrameCaptured == false and a
    // non-succeeded Result as "no scene description available" and proceed
    // voice-only rather than failing the whole conversational turn.
    public readonly struct SceneCaptureAnalysisOutcome
    {
        private SceneCaptureAnalysisOutcome(bool frameCaptured, VisionLanguageResult? result, string detail)
        {
            FrameCaptured = frameCaptured;
            Result = result;
            Detail = detail;
        }

        public static SceneCaptureAnalysisOutcome NoFrame(string detail) =>
            new SceneCaptureAnalysisOutcome(frameCaptured: false, result: null, detail);

        public static SceneCaptureAnalysisOutcome Analyzed(VisionLanguageResult result) =>
            new SceneCaptureAnalysisOutcome(frameCaptured: true, result, string.Empty);

        public bool FrameCaptured { get; }
        public VisionLanguageResult? Result { get; }
        public string Detail { get; }
    }

    public sealed partial class ReachyAndroidPerceptionApplicationService
    {
        // Public because ReachyCloudVlmCredentialCoordinator (the settings-UI
        // side that writes a profile under this id) needs the exact same
        // identifier -- a single source of truth avoids the two sides
        // silently drifting apart.
        public const string CloudVlmProfileProviderId = "reachy-cloud-vlm";
        private const string CloudVlmSwitchSourceProviderId = "reachy-vlm-unselected";
        private const string CloudVlmDisplayName = "Cloud VLM (OpenAI-compatible)";
        private const int CloudVlmMaximumConcurrentOperations = 1;
        private const int CloudVlmMaximumPromptCharacters = 4096;
        private const int CloudVlmMaximumOutputTokens = 512;
        private const int CloudVlmMaximumResponseCharacters = 8192;
        private const int CloudVlmMaximumTransportResponseBytes = 1024 * 1024;
        private const int CloudVlmMaximumDiagnosticCharacters = 4096;

        private readonly SemaphoreSlim cloudVlmLoadGate = new SemaphoreSlim(1, 1);
        private readonly object cloudVlmSync = new object();
        private ReachyProviderProfilePersistenceStore? cloudVlmProfileStore;
        private ReachyProviderFallbackPolicyEngine? cloudVlmFallbackPolicyEngine;
        private IReachyProviderSecretStore? cloudVlmSecretStore;
        private ReachyOpenAiVisionHttpTransport? cloudVlmTransport;
        private IVisionLanguageProvider? cloudVlmProvider;

        // RMA-195 (voice+visual grounding, 2026-08-22): a caller (the
        // conversation orchestrator's VLM fold-in) that wants "describe
        // whatever the camera currently sees" cannot supply a
        // ReachyVisionFrame itself -- there is no production way to build
        // one outside the real camera/warp pipeline, and
        // VisionLanguageResult.Failure/VisionLanguageRequest both require a
        // real, fully-valid frame (no placeholder/null path exists in either
        // constructor). So "no frame could be captured" (perception not
        // running, camera preview toggle off, simulation not yet publishing
        // state) is modeled as its own outcome here rather than forced into
        // VisionLanguageResult's frame-requiring shape -- the caller needs
        // to distinguish "proceed voice-only, no scene description" from a
        // real VLM failure anyway. Owns the captured frame's disposal
        // itself, so the caller never has to touch a ReachyVisionFrame
        // directly. Never starts camera acquisition itself -- matches
        // ReachyAndroidPerceptionDriver.CaptureFrameForAnalysisAsync's own
        // fail-closed contract.
        public async ValueTask<SceneCaptureAnalysisOutcome> AnalyzeCurrentSceneAsync(
            string prompt,
            string requestId,
            CancellationToken cancellationToken)
        {
            if (driver == null)
            {
                return SceneCaptureAnalysisOutcome.NoFrame(
                    "Perception is not running on this platform.");
            }

            ReachyVisionFrame? frame = await driver.CaptureFrameForAnalysisAsync(cancellationToken)
                .ConfigureAwait(false);
            if (frame == null)
            {
                return SceneCaptureAnalysisOutcome.NoFrame(
                    "No camera frame is available. Turn on camera preview in Settings " +
                        "and make sure the authoritative simulation has started.");
            }

            try
            {
                VisionLanguageResult result =
                    await AnalyzeSceneAsync(frame, prompt, requestId, cancellationToken)
                        .ConfigureAwait(false);
                return SceneCaptureAnalysisOutcome.Analyzed(result);
            }
            finally
            {
                await frame.DisposeAsync().ConfigureAwait(false);
            }
        }

        public async ValueTask<VisionLanguageResult> AnalyzeSceneAsync(
            ReachyVisionFrame frame,
            string prompt,
            string requestId,
            CancellationToken cancellationToken)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            (IVisionLanguageProvider? provider, string detail) =
                await EnsureCloudVlmLoadedAsync(cancellationToken).ConfigureAwait(false);

            VisionProviderSelectionSnapshot selection = new VisionProviderSelectionSnapshot(
                VisionProviderKind.SemanticVisionLanguage,
                provider?.Descriptor.InstanceId ?? CloudVlmProfileProviderId,
                epoch: 1UL);
            VisionRequestContext context = new VisionRequestContext(
                requestId,
                selection,
                VisionRequestContext.MaximumTimeout);
            VisionLanguageRequest request = new VisionLanguageRequest(
                frame,
                prompt,
                context,
                // The fallback-policy authorization already gated loading the
                // provider at all (see EvaluateCloudVlmSwitchAuthorization) --
                // that IS this workload's network-disclosure/consent gate, so
                // by the time a provider exists to call, disclosure has
                // already been explicitly granted through the settings UI.
                networkDisclosureAcknowledged: true);

            if (provider == null)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Unavailable,
                    UnconfiguredDescriptor(),
                    request,
                    requiresProviderReset: false,
                    detail);
            }

            return await provider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private void DisposeCloudVlm()
        {
            lock (cloudVlmSync)
            {
                cloudVlmProvider = null;
                cloudVlmTransport?.Dispose();
                cloudVlmTransport = null;
            }
        }

        private async ValueTask<(IVisionLanguageProvider? provider, string detail)>
            EnsureCloudVlmLoadedAsync(CancellationToken cancellationToken)
        {
            lock (cloudVlmSync)
            {
                if (cloudVlmProvider != null)
                {
                    return (cloudVlmProvider, string.Empty);
                }
            }

            await cloudVlmLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (cloudVlmSync)
                {
                    if (cloudVlmProvider != null)
                    {
                        return (cloudVlmProvider, string.Empty);
                    }
                }
                return LoadCloudVlm();
            }
            finally
            {
                cloudVlmLoadGate.Release();
            }
        }

        private (IVisionLanguageProvider? provider, string detail) LoadCloudVlm()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return (null, "Cloud VLM analysis requires an Android device.");
            }

            ReachyProviderProfilePersistenceStore profileStore = EnsureCloudVlmProfileStore();
            if (!profileStore.TryGet(
                    CloudVlmProfileProviderId,
                    out ReachyProviderProfile? profile) ||
                profile == null)
            {
                return (null, "No cloud VLM provider profile is configured.");
            }

            IReachyProviderSecretStore? secretStore = EnsureCloudVlmSecretStore();
            if (secretStore == null)
            {
                return (null, "Cloud VLM credential storage is unavailable on this platform.");
            }

            (ReachyAuthorizedProviderSwitch? authorization, string authorizationFailureDetail) =
                EvaluateCloudVlmSwitchAuthorization(profile);
            if (authorization == null)
            {
                return (null, authorizationFailureDetail);
            }
            authorization.Consume(
                ReachyProviderWorkloadKind.Vlm,
                CloudVlmSwitchSourceProviderId,
                profile.ProviderId);

            ReachyOpenAiVisionHttpTransport built;
            IVisionLanguageProvider provider;
            try
            {
                OpenAiVisionProviderConfiguration configuration = BuildConfiguration(profile);
                string relativePath = configuration.EndpointStyle == OpenAiVisionEndpointStyle.Responses
                    ? "v1/responses"
                    : "v1/chat/completions";
                built = new ReachyOpenAiVisionHttpTransport(
                    configuration.EndpointStyle,
                    profile,
                    secretStore,
                    relativePath,
                    CloudVlmMaximumTransportResponseBytes,
                    CloudVlmMaximumDiagnosticCharacters);
                IRemoteVlmImageEncoder encoder = new ReachyUnityRemoteVlmImageEncoder();
                provider = configuration.EndpointStyle == OpenAiVisionEndpointStyle.Responses
                    ? new OpenAiResponsesVisionLanguageProvider(configuration, built, encoder)
                    : new OpenAiChatCompletionsVisionLanguageProvider(configuration, built, encoder);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is ArgumentOutOfRangeException ||
                exception is KeyNotFoundException)
            {
                string detail = "Cloud VLM provider profile is invalid for this adapter: " +
                    exception.Message;
                return (null, detail);
            }

            lock (cloudVlmSync)
            {
                cloudVlmTransport = built;
                cloudVlmProvider = provider;
            }
            return (provider, string.Empty);
        }

        private (ReachyAuthorizedProviderSwitch?, string) EvaluateCloudVlmSwitchAuthorization(
            ReachyProviderProfile profile)
        {
            ReachyProviderFallbackPolicyEngine engine = EnsureCloudVlmFallbackPolicyEngine();
            ReachyProviderSwitchRequest switchRequest = new ReachyProviderSwitchRequest(
                new ReachyProviderEndpointIdentity(
                    ReachyProviderWorkloadKind.Vlm,
                    CloudVlmSwitchSourceProviderId,
                    ReachyProviderPrivacyBoundary.OnDevice),
                new ReachyProviderEndpointIdentity(
                    ReachyProviderWorkloadKind.Vlm,
                    profile.ProviderId,
                    ReachyProviderPrivacyBoundary.Cloud),
                "cloud-vlm-provider-selected");

            ReachyFallbackDecision initial = engine.EvaluateProviderSwitch(switchRequest);
            if (initial.Status == ReachyFallbackDecisionStatus.Authorized)
            {
                return (initial.Authorization, string.Empty);
            }
            if (initial.Status != ReachyFallbackDecisionStatus.RequiresPrivacyConfirmation)
            {
                return (
                    null,
                    "Cloud VLM access is not authorized by the fallback policy (" +
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
                    "Cloud VLM privacy-boundary confirmation did not authorize the switch (" +
                        confirmed.DiagnosticCode +
                        ").");
            }
            return (confirmed.Authorization, string.Empty);
        }

        private ReachyProviderProfilePersistenceStore EnsureCloudVlmProfileStore()
        {
            lock (cloudVlmSync)
            {
                if (cloudVlmProfileStore == null)
                {
                    cloudVlmProfileStore = new ReachyProviderProfilePersistenceStore();
                    cloudVlmProfileStore.Initialize();
                }
                return cloudVlmProfileStore;
            }
        }

        // Loads from ReachyFallbackPolicyPersistenceStore rather than a bare
        // in-memory engine, same reasoning as
        // ReachyLocalLlmProviderApplicationService.EnsureFallbackPolicyEngine:
        // otherwise a grant made through ReachyCloudVlmCredentialCoordinator's
        // settings UI (which persists to the same default file) would never
        // become visible here.
        private ReachyProviderFallbackPolicyEngine EnsureCloudVlmFallbackPolicyEngine()
        {
            lock (cloudVlmSync)
            {
                if (cloudVlmFallbackPolicyEngine == null)
                {
                    cloudVlmFallbackPolicyEngine = new ReachyProviderFallbackPolicyEngine();
                    ReachyFallbackPolicyPersistenceStore fallbackPolicyStore =
                        new ReachyFallbackPolicyPersistenceStore(cloudVlmFallbackPolicyEngine);
                    fallbackPolicyStore.Initialize();
                }
                return cloudVlmFallbackPolicyEngine;
            }
        }

        private IReachyProviderSecretStore? EnsureCloudVlmSecretStore()
        {
            lock (cloudVlmSync)
            {
                if (cloudVlmSecretStore == null &&
                    Application.platform == RuntimePlatform.Android)
                {
                    cloudVlmSecretStore = new ReachyAndroidProviderSecretStore();
                }
                return cloudVlmSecretStore;
            }
        }

        private static OpenAiVisionProviderConfiguration BuildConfiguration(
            ReachyProviderProfile profile)
        {
            OpenAiVisionEndpointStyle endpointStyle =
                profile.EndpointStyle == ReachyProviderEndpointStyle.Responses
                    ? OpenAiVisionEndpointStyle.Responses
                    : OpenAiVisionEndpointStyle.ChatCompletions;
            return new OpenAiVisionProviderConfiguration(
                endpointStyle,
                profile.ProviderId,
                profile.ProviderId,
                CloudVlmDisplayName,
                "1",
                profile.GetModelId(ReachyProviderModelRole.Vision),
                VisionProviderLocation.Cloud,
                supportsVisualQuestions: true,
                supportsSceneDescription: true,
                CloudVlmMaximumConcurrentOperations,
                CloudVlmMaximumPromptCharacters,
                CloudVlmMaximumOutputTokens,
                CloudVlmMaximumResponseCharacters,
                RemoteVlmImagePolicy.CreateDefault());
        }

        private static ProviderDescriptor UnconfiguredDescriptor()
        {
            return new ProviderDescriptor(
                VisionProviderKind.SemanticVisionLanguage,
                CloudVlmProfileProviderId,
                CloudVlmProfileProviderId,
                CloudVlmDisplayName,
                "1",
                VisionProviderLocation.Cloud);
        }
    }
}
