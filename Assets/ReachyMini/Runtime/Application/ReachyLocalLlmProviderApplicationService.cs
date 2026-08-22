#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;
using ReachyMini.Providers;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase B: replaces the permanently-Unavailable "provider-selection"
    // stub with real wiring for the one provider path that is actually fully
    // built end to end today -- local LLM, on-device. ASR/TTS/VLM and any
    // AndroidService/Cloud execution remain out of scope (phase D); this
    // service still reports every OTHER provider kind/execution exactly as
    // ReachySettingsProviderApplicationService always did ("stored but not
    // integrated"). Scope also deliberately excludes the model install/import
    // UI -- ReachyMainScreen.RequestLocalModelInstall/Import/Select/Delete are
    // still stubs, a separate and materially larger piece. Without them there
    // is genuinely no way to get a model onto a real device today outside
    // RMA-134/135's test harnesses, and this service is honest about that: it
    // resolves against the real LocalModelPackageManager-managed store (not a
    // shortcut fixed path), so on a fresh device it correctly reports "no
    // compatible local model is installed" rather than silently pretending to
    // work.
    //
    // OnInitialize() never triggers a model load. ReachyApplicationServiceBase
    // auto-calls SetReady() if OnInitialize() returns while health is still
    // Initializing, so any subclass whose OnInitialize() kicks off async work
    // without itself calling a setter first races that auto-SetReady --
    // ReachyAndroidSpeechCapabilityApplicationService's fire-and-forget probe
    // has exactly this tolerated hazard elsewhere in this codebase (it briefly
    // reports a generic "Ready" before the real probe result lands). Loading
    // ~700MB is not something to risk that race on, and there is no "Loading"
    // ReachyServiceState to hold at in the meantime. So OnInitialize() stays
    // fully synchronous and the coarse-grained Health never depends on whether
    // a model has loaded; only the fine-grained ProviderSnapshot does. Loading
    // is fully lazy: the first GenerateAsync call loads the model (admission
    // -> resolve installed artifact -> create -> generate), and every
    // subsequent call reuses the same coordinator until Dispose.
    // RMA-195 phase D (composition-wiring-only slice, 2026-08-22): also carries
    // the cloud LLM path now, added alongside the existing local path above
    // rather than as a second "provider-selection" registration -- the
    // composition host allows exactly one registration per ReachyServiceKind
    // (ReachyApplicationComposition.CreateComplete rejects a duplicate kind at
    // construction time), so a second capability on the SAME registered
    // service is the only way to expose it, mirroring how
    // ILocalLlmProviderCapability/IReachyProviderGovernorDiagnosticsSource
    // already coexist as optional as-castable facets of one IReachyProviderService.
    // Cloud loading mirrors the local path's laziness (OnInitialize() never
    // triggers it) but is otherwise much simpler: no model file, no resource
    // governor -- it resolves a ReachyProviderProfile from
    // ReachyProviderProfilePersistenceStore, an Android-Keystore-backed
    // IReachyProviderSecretStore, and requires
    // ReachyProviderFallbackPolicyEngine to authorize the OnDevice->Cloud
    // privacy-boundary switch before constructing the RMA-142/143 adapter.
    // Deliberately excluded from this slice: any settings UI to actually
    // create a cloud provider profile/credential or grant the fallback
    // policy authorization, and any live call site that invokes this
    // capability -- exactly like the local path, ICloudLlmProviderCapability
    // is reachable only from tests today. On a real, unconfigured device this
    // always reports "no cloud LLM provider profile is configured", honestly,
    // the same way the local path reports "no compatible local model is
    // installed" before its own install UI exists.
    public sealed class ReachyLocalLlmProviderApplicationService :
        ReachyApplicationServiceBase,
        IReachyProviderService,
        ILocalLlmProviderCapability,
        ICloudLlmProviderCapability,
        IReachyProviderGovernorDiagnosticsSource,
        IReachyApplicationInterruptionParticipant
    {
        // Bounded patience for a resource-governor Suspended verdict, mirroring
        // ReachyRma135ResourceGovernorAcceptance's own retry loop: a single
        // unlucky physics/thermal sample must not be read as a device that
        // cannot run local inference, but a genuinely sustained suspension
        // still fails. A live generation request is user-facing (unlike the
        // acceptance harness's one-shot startup check), so this uses a
        // shorter budget: 15 attempts at 200ms is 3 seconds of worst-case
        // added latency before reporting suspended, not the acceptance
        // harness's 20-second allowance.
        private const int AdmissionAttemptBudget = 15;
        private static readonly TimeSpan AdmissionRetryInterval =
            TimeSpan.FromMilliseconds(200.0);
        private const string LocalModelStoreDirectoryName = "local-models";

        // Well-known lookup key into ReachyProviderProfilePersistenceStore for a
        // cloud LLM profile. Nothing can write one under this id yet (no settings
        // UI); this is a forward-looking convention for whichever future phase
        // adds that UI to adopt, not evidence a profile exists today.
        private const string CloudLlmProfileProviderId = "reachy-cloud-llm";
        // Stable placeholder "source" identity for the OnDevice->Cloud privacy-
        // boundary switch request -- there is no real "currently active" LLM
        // provider identity to reference (provider selection is a user
        // preference, not a live handoff), so this names the switch's origin
        // boundary without claiming a specific prior provider was active.
        private const string CloudLlmSwitchSourceProviderId = "reachy-llm-unselected";

        private readonly ReachySettingsStateStore settings;
        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly SemaphoreSlim loadGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim cloudLoadGate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();

        private LocalModelPackageManager? packageManager;
        private LocalLlmResourceGovernor? governor;
        private ReachyAndroidLocalLlmResourceSignalSource? resourceSignals;
        private ReachySimulationLocalLlmPhysicsBudgetSource? physicsBudget;
        private LocalLlmProvider? loadedProvider;
        private LocalLlmGovernedGenerationCoordinator? coordinator;
        private ReachyProviderProfilePersistenceStore? cloudProfileStore;
        private ReachyProviderFallbackPolicyEngine? fallbackPolicyEngine;
        private IReachyProviderSecretStore? cloudSecretStore;
        private ReachyOpenAiCompatibleLlmProviderBase? cloudProvider;
        private bool interruptionPaused;
        private ReachyProviderServiceSnapshot snapshot;

        public ReachyLocalLlmProviderApplicationService(
            ReachySettingsStateStore settings,
            ReachyProductionAuthoritativeRuntime runtime)
            : base(
                "provider-selection",
                ReachyServiceKind.Provider,
                ReachyServiceCriticality.Optional)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            snapshot = new ReachyProviderServiceSnapshot(
                ReachyProviderServiceExecutionState.NotLoaded,
                string.Empty,
                "Local LLM has not been loaded.",
                0UL);
        }

        public ReachyProviderServiceSnapshot ProviderSnapshot
        {
            get
            {
                lock (sync)
                {
                    return snapshot;
                }
            }
        }

        public event EventHandler<ReachyProviderServiceSnapshotChangedEventArgs>?
            ProviderSnapshotChanged;

        public ReachyLlmCapabilities Capabilities => ReachyLlmCapabilities.TextOnly();

        public LocalLlmGovernorDiagnosticsSnapshot GovernorDiagnostics =>
            LocalLlmGovernorDiagnosticsSnapshot.Create(coordinator?.CurrentDecision);

        protected override void OnInitialize()
        {
            settings.Changed += OnSettingsChanged;
            PublishCurrentHealth();
        }

        protected override void OnDispose()
        {
            settings.Changed -= OnSettingsChanged;

            LocalLlmProvider? providerToDispose;
            ReachyOpenAiCompatibleLlmProviderBase? cloudProviderToDispose;
            lock (sync)
            {
                providerToDispose = loadedProvider;
                loadedProvider = null;
                coordinator = null;
                cloudProviderToDispose = cloudProvider;
                cloudProvider = null;
            }
            if (providerToDispose != null)
            {
                // OnDispose is a synchronous contract (ReachyApplicationServiceBase);
                // LocalLlmProvider.DisposeAsync already bounds its own drain of any
                // in-flight generation (CancellationDrainTimeout) and is self-contained,
                // so a fire-and-forget dispose here does not leak an unbounded wait.
                _ = providerToDispose.DisposeAsync().AsTask();
            }
            if (cloudProviderToDispose != null)
            {
                // ReachyOpenAiCompatibleLlmProviderBase.DisposeAsync only disposes its
                // owned ReachySharedHttpTransport (a synchronous, bounded call
                // wrapped in a completed ValueTask) -- also safe to fire-and-forget.
                _ = cloudProviderToDispose.DisposeAsync().AsTask();
            }
            resourceSignals?.Dispose();
            packageManager?.Dispose();
        }

        public void PauseForApplicationInterruption()
        {
            lock (sync)
            {
                interruptionPaused = true;
            }
            loadedProvider?.PauseForApplicationInterruption();
        }

        public void ResumeAfterApplicationInterruption()
        {
            lock (sync)
            {
                interruptionPaused = false;
            }
            loadedProvider?.ResumeAfterApplicationInterruption();
        }

        public async Task<LocalLlmGovernedGenerationResult> GenerateAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            (LocalLlmGovernedGenerationCoordinator? activeCoordinator, string modelId, string failureDetail) =
                await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (activeCoordinator == null)
            {
                return new LocalLlmGovernedGenerationResult(
                    LocalLlmGovernedGenerationStatus.SignalFailure,
                    null,
                    null,
                    failureDetail);
            }

            PublishSnapshot(
                ReachyProviderServiceExecutionState.Generating,
                modelId,
                "Generating a local LLM response.");
            LocalLlmGovernedGenerationResult result = await activeCoordinator.GenerateAsync(
                request,
                sink,
                cancellationToken).ConfigureAwait(false);
            PublishSnapshot(
                result.Succeeded
                    ? ReachyProviderServiceExecutionState.Ready
                    : ReachyProviderServiceExecutionState.Suspended,
                modelId,
                "Idle after the last generation (" + result.Status + "): " + result.Detail);
            return result;
        }

        public async Task<ReachyLlmGenerationResult> GenerateAsync(
            ReachyLlmGenerationRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            (ReachyOpenAiCompatibleLlmProviderBase? activeCloudProvider, string failureDetail) =
                await EnsureCloudLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (activeCloudProvider == null)
            {
                return new ReachyLlmGenerationResult(
                    ReachyLlmGenerationStatus.HttpFailure,
                    null,
                    string.Empty,
                    failureDetail);
            }

            return await activeCloudProvider.GenerateAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            PublishCurrentHealth();
        }

        private void PublishCurrentHealth()
        {
            int configured = 0;
            int networkRequired = 0;
            bool llmOnDeviceSelected = false;
            bool llmCloudSelected = false;
            foreach (ReachyProviderSelection provider in
                     settings.Current.ProviderSelections)
            {
                if (provider.Execution != ReachyProviderExecution.Unconfigured)
                {
                    ++configured;
                }
                if (provider.Connectivity ==
                    ReachyConnectivityRequirement.NetworkRequired)
                {
                    ++networkRequired;
                }
                if (provider.Kind == ReachyProviderKind.Llm &&
                    provider.Execution == ReachyProviderExecution.OnDevice)
                {
                    llmOnDeviceSelected = true;
                }
                if (provider.Kind == ReachyProviderKind.Llm &&
                    provider.Execution == ReachyProviderExecution.Cloud)
                {
                    llmCloudSelected = true;
                }
            }

            if (llmOnDeviceSelected)
            {
                SetDegraded(
                    $"{configured} provider preference(s) are stored, including " +
                    $"{networkRequired} network-required selection(s). The on-device " +
                    "local LLM preference is wired and loads lazily on first use; " +
                    "other provider kinds are not yet integrated.");
                return;
            }

            if (llmCloudSelected)
            {
                SetDegraded(
                    $"{configured} provider preference(s) are stored, including " +
                    $"{networkRequired} network-required selection(s). The cloud " +
                    "LLM preference is wired and loads lazily on first use, gated " +
                    "on a configured provider profile and fallback-policy " +
                    "authorization that no settings surface can grant yet; other " +
                    "provider kinds are not yet integrated.");
                return;
            }

            SetUnavailable(
                configured == 0
                    ? "No ASR, TTS, LLM, or VLM provider is configured."
                    : $"{configured} provider preference(s) are stored, including " +
                      $"{networkRequired} network-required selection(s), but provider " +
                      "runtime integrations and credentials are not installed.");
        }

        private async Task<(LocalLlmGovernedGenerationCoordinator?, string, string)>
            EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (coordinator != null)
                {
                    return (coordinator, loadedProvider!.ModelId, string.Empty);
                }
            }

            await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (sync)
                {
                    if (coordinator != null)
                    {
                        return (coordinator, loadedProvider!.ModelId, string.Empty);
                    }
                }
                return await LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                loadGate.Release();
            }
        }

        private async Task<(LocalLlmGovernedGenerationCoordinator?, string, string)> LoadAsync(
            CancellationToken cancellationToken)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                const string detail = "Local LLM generation requires an Android device.";
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, string.Empty, detail);
            }

            PublishSnapshot(
                ReachyProviderServiceExecutionState.Loading,
                string.Empty,
                "Resolving the installed local model.");

            LocalModelManifest manifest = LocalLlmBehaviorContract.CreateSelectedManifest();
            LocalModelPackageManager manager = EnsurePackageManager();
            LocalModelPackageResult resolved;
            try
            {
                resolved = await manager.ResolveInstalledAsync(manifest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                string detail = "Resolving the installed local model failed: " + exception.Message;
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, string.Empty, detail);
            }
            if (!resolved.Succeeded || resolved.Artifact == null)
            {
                string detail = string.IsNullOrEmpty(resolved.Detail)
                    ? "No compatible local model is installed."
                    : resolved.Detail;
                PublishSnapshot(ReachyProviderServiceExecutionState.NotLoaded, string.Empty, detail);
                return (null, string.Empty, detail);
            }

            LocalLlmResourceGovernor activeGovernor = EnsureGovernor();
            ReachyAndroidLocalLlmResourceSignalSource activeResourceSignals;
            ReachySimulationLocalLlmPhysicsBudgetSource activePhysicsBudget;
            try
            {
                activeResourceSignals = EnsureResourceSignals();
                activePhysicsBudget = EnsurePhysicsBudget();
            }
            catch (Exception exception)
            {
                string detail = "Local LLM resource signals are unavailable: " + exception.Message;
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, string.Empty, detail);
            }

            LocalLlmExecutionProfile baselineProfile =
                LocalLlmExecutionProfile.CreateRma133V6Baseline();
            LocalLlmProviderAdmissionResult admission = LocalLlmGovernedGenerationCoordinator
                .EvaluateAdmission(
                    baselineProfile,
                    activeGovernor,
                    activeResourceSignals,
                    activePhysicsBudget,
                    manifest.Artifact.FileSizeBytes);
            int admissionAttempts = 1;
            while (!admission.Succeeded &&
                admission.Status == LocalLlmProviderAdmissionStatus.Suspended &&
                admissionAttempts < AdmissionAttemptBudget)
            {
                await Task.Delay(AdmissionRetryInterval, cancellationToken).ConfigureAwait(false);
                ++admissionAttempts;
                admission = LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                    baselineProfile,
                    activeGovernor,
                    activeResourceSignals,
                    activePhysicsBudget,
                    manifest.Artifact.FileSizeBytes);
            }
            if (!admission.Succeeded || admission.EffectiveProfile == null)
            {
                string detail = "Local LLM resource admission refused inference: " + admission.Detail;
                PublishSnapshot(ReachyProviderServiceExecutionState.Suspended, string.Empty, detail);
                return (null, string.Empty, detail);
            }

            LocalLlmExecutionProfile effectiveProfile = admission.EffectiveProfile;
            LocalLlmProviderCreationResult creation = await LocalLlmProvider.CreateAsync(
                manifest,
                resolved.Artifact,
                effectiveProfile,
                cancellationToken).ConfigureAwait(false);
            if (creation.Status == LocalLlmProviderCreationStatus.InvalidConfiguration &&
                creation.MandatoryPromptTokens > 0)
            {
                // Admission runs before the model is resident, so it cannot know how
                // many tokens the contract's mandatory prompt costs under this model's
                // tokenizer. Creation measured it and refused a context that could not
                // hold it; re-evaluate admission once against the real number, then
                // load at the profile that can actually serve a request.
                admission = LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                    baselineProfile,
                    activeGovernor,
                    activeResourceSignals,
                    activePhysicsBudget,
                    manifest.Artifact.FileSizeBytes,
                    creation.MandatoryPromptTokens);
                if (!admission.Succeeded || admission.EffectiveProfile == null)
                {
                    string detail =
                        "Local LLM resource admission refused inference once the mandatory " +
                        "prompt was measured (" + creation.MandatoryPromptTokens + " tokens): " +
                        admission.Detail;
                    PublishSnapshot(ReachyProviderServiceExecutionState.Suspended, string.Empty, detail);
                    return (null, string.Empty, detail);
                }
                effectiveProfile = admission.EffectiveProfile;
                creation = await LocalLlmProvider.CreateAsync(
                    manifest,
                    resolved.Artifact,
                    effectiveProfile,
                    cancellationToken).ConfigureAwait(false);
            }
            if (!creation.Succeeded || creation.Provider == null)
            {
                string detail = "Local LLM model load failed: " + creation.Detail;
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, string.Empty, detail);
            }

            LocalLlmProvider provider = creation.Provider;
            bool pausedWhileLoading;
            lock (sync)
            {
                pausedWhileLoading = interruptionPaused;
            }
            if (pausedWhileLoading)
            {
                provider.PauseForApplicationInterruption();
            }

            LocalLlmGovernedGenerationCoordinator built = new LocalLlmGovernedGenerationCoordinator(
                provider,
                baselineProfile,
                activeGovernor,
                activeResourceSignals,
                activePhysicsBudget);
            lock (sync)
            {
                loadedProvider = provider;
                coordinator = built;
            }
            PublishSnapshot(
                ReachyProviderServiceExecutionState.Ready,
                provider.ModelId,
                "Local LLM loaded and ready.");
            return (built, provider.ModelId, string.Empty);
        }

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

        private async Task<(ReachyOpenAiCompatibleLlmProviderBase?, string)> LoadCloudAsync(
            CancellationToken cancellationToken)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                const string detail = "Cloud LLM generation requires an Android device.";
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, detail);
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
                return (null, detail);
            }

            IReachyProviderSecretStore? secretStore = EnsureCloudSecretStore();
            if (secretStore == null)
            {
                const string detail = "Cloud LLM credential storage is unavailable on this platform.";
                PublishSnapshot(ReachyProviderServiceExecutionState.Faulted, string.Empty, detail);
                return (null, detail);
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
                return (null, authorizationFailureDetail);
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
                return (null, detail);
            }

            lock (sync)
            {
                cloudProvider = built;
            }
            PublishSnapshot(
                ReachyProviderServiceExecutionState.Ready,
                profile.ProviderId,
                "Cloud LLM provider loaded and ready.");
            return (built, string.Empty);
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
                    ReachyProviderPrivacyBoundary.Cloud),
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

        private ReachyProviderFallbackPolicyEngine EnsureFallbackPolicyEngine()
        {
            lock (sync)
            {
                fallbackPolicyEngine ??= new ReachyProviderFallbackPolicyEngine();
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

        private LocalModelPackageManager EnsurePackageManager()
        {
            lock (sync)
            {
                if (packageManager == null)
                {
                    string root = Path.Combine(
                        Application.persistentDataPath,
                        LocalModelStoreDirectoryName);
                    packageManager = new LocalModelPackageManager(
                        root,
                        new DriveInfoLocalModelStorageProbe());
                }
                return packageManager;
            }
        }

        private LocalLlmResourceGovernor EnsureGovernor()
        {
            lock (sync)
            {
                governor ??= new LocalLlmResourceGovernor();
                return governor;
            }
        }

        private ReachyAndroidLocalLlmResourceSignalSource EnsureResourceSignals()
        {
            lock (sync)
            {
                resourceSignals ??= new ReachyAndroidLocalLlmResourceSignalSource();
                return resourceSignals;
            }
        }

        private ReachySimulationLocalLlmPhysicsBudgetSource EnsurePhysicsBudget()
        {
            lock (sync)
            {
                physicsBudget ??= new ReachySimulationLocalLlmPhysicsBudgetSource(runtime);
                return physicsBudget;
            }
        }

        private void PublishSnapshot(
            ReachyProviderServiceExecutionState state,
            string modelId,
            string message)
        {
            ReachyProviderServiceSnapshot next;
            lock (sync)
            {
                next = new ReachyProviderServiceSnapshot(
                    state,
                    modelId,
                    message,
                    checked(snapshot.Revision + 1UL));
                snapshot = next;
            }
            ProviderSnapshotChanged?.Invoke(
                this,
                new ReachyProviderServiceSnapshotChangedEventArgs(next));
        }
    }
}
