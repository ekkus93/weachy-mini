#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Interop;
using ReachyMini.LocalModels;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma135ResourceGovernorAcceptance
    {
        private static async Task<Rma135AcceptanceReport> RunAcceptanceAsync()
        {
            string modelPath = Path.Combine(UnityEngine.Application.persistentDataPath, ModelFileName);
            WriteCheckpoint("artifact_verification_started", "Verifying exact RMA-133 selected GGUF artifact.");
            Rma135ArtifactVerification artifact = await Task.Run(() => VerifyArtifact(modelPath)).ConfigureAwait(true);
            WriteCheckpoint(
                "artifact_verified",
                "bytes=" + artifact.bytes.ToString(CultureInfo.InvariantCulture) +
                " sha256=" + artifact.sha256);

            uint nativeAbi = NativeReachyLlama.AbiVersion();
            if (nativeAbi != 2U)
            {
                throw new InvalidOperationException(
                    "RMA-135 physical acceptance loaded reachy_llama ABI " + nativeAbi +
                    " instead of ABI 2.");
            }
            WriteCheckpoint("abi_verified", "reachy_llama ABI=2.");

            LocalLlmProvider? provider = null;
            using var androidSignals = new ReachyAndroidLocalLlmResourceSignalSource();
            try
            {
                WriteCheckpoint(
                    "production_physics_runtime_started",
                    "Locating the live ReachyProductionAuthoritativeRuntime; the acceptance will not create a second MuJoCo worker.");
                ReachyProductionAuthoritativeRuntime productionRuntime =
                    await WaitForProductionRuntimeAsync().ConfigureAwait(true);
                var realPhysics = new ReachySimulationLocalLlmPhysicsBudgetSource(productionRuntime);
                Rma135PhysicsStartupStabilization startupPhysics =
                    await WaitForPhysicsBudgetAsync(realPhysics).ConfigureAwait(true);
                ReachySimulationTimingSnapshot workerBeforeAdmission =
                    await WaitForTimingProgressAsync(
                        productionRuntime,
                        startupPhysics.MinimumObservedStepCount).ConfigureAwait(true);
                WriteCheckpoint(
                    "production_physics_runtime_ready",
                    "state=" + startupPhysics.State +
                    " observations=" + startupPhysics.Observations.ToString(CultureInfo.InvariantCulture) +
                    " exceeded_observations=" + startupPhysics.ExceededObservations.ToString(CultureInfo.InvariantCulture) +
                    " steps=" + workerBeforeAdmission.TotalStepCount.ToString(CultureInfo.InvariantCulture));

                LocalLlmResourceSnapshot initialResources = androidSignals.Capture(startupPhysics.State);
                LocalLlmExecutionProfile baselineProfile =
                    LocalLlmExecutionProfile.CreateRma133V6Baseline();
                var governor = new LocalLlmResourceGovernor();
                // Reserve the artifact the load is about to make resident. Without it,
                // admission judges a process that has not paid for the model yet and can
                // admit a profile that loading immediately invalidates.
                LocalLlmProviderAdmissionResult admission =
                    LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                        baselineProfile,
                        governor,
                        androidSignals,
                        realPhysics,
                        LocalLlmBehaviorContract.ArtifactBytes);
                // Admission takes a single physics sample, and a new deadline miss is an
                // immediate suspension condition by design. On real hardware a miss is often
                // transient -- the startup loop above needed several observations to find
                // three consecutive admissible ones -- so a single unlucky sample must not be
                // read as a device that cannot run local inference. Production would simply
                // re-evaluate on the next request; give admission the same bounded patience
                // the startup loop already uses, at the same interval. A genuinely sustained
                // suspension still fails, and only Suspended is retried: a signal failure is
                // a real fault and stays fail-visible.
                int admissionAttempts = 1;
                string firstAdmissionRefusal = admission.Succeeded
                    ? string.Empty
                    : admission.Decision?.Reasons.ToString() ?? admission.Status.ToString();
                while (!admission.Succeeded &&
                    admission.Status == LocalLlmProviderAdmissionStatus.Suspended &&
                    admissionAttempts < AdmissionAttemptBudget)
                {
                    await Task.Delay(AdmissionRetryInterval).ConfigureAwait(true);
                    ++admissionAttempts;
                    admission = LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                        baselineProfile,
                        governor,
                        androidSignals,
                        realPhysics,
                        LocalLlmBehaviorContract.ArtifactBytes);
                }
                if (!admission.Succeeded || admission.Decision == null ||
                    admission.EffectiveProfile == null)
                {
                    TryWriteCheckpoint(
                        "admission_refused",
                        "attempts=" + admissionAttempts.ToString(CultureInfo.InvariantCulture) +
                        " status=" + admission.Status +
                        " first_reasons=" + firstAdmissionRefusal +
                        " last_reasons=" + (admission.Decision?.Reasons.ToString() ?? "none") +
                        " detail=" + admission.Detail);
                    throw new InvalidOperationException(
                        "RMA-135 provider admission refused local inference after " +
                        admissionAttempts.ToString(CultureInfo.InvariantCulture) +
                        " attempts: status=" + admission.Status +
                        " detail=" + admission.Detail);
                }
                LocalLlmExecutionProfile effectiveProfile = admission.EffectiveProfile;
                WriteCheckpoint(
                    "resource_admission_ready",
                    "mode=" + admission.Decision.Mode + " device=" +
                    admission.Decision.DeviceProfile.Kind + " thermal=" +
                    initialResources.ThermalStatus + " ctx=" +
                    effectiveProfile.ContextTokens.ToString(CultureInfo.InvariantCulture) +
                    " threads=" + effectiveProfile.Threads.ToString(CultureInfo.InvariantCulture) +
                    " attempts=" + admissionAttempts.ToString(CultureInfo.InvariantCulture) +
                    " first_refusal=" + (firstAdmissionRefusal.Length == 0
                        ? "none"
                        : firstAdmissionRefusal));

                LocalModelManifest manifest = CreateSelectedManifest();
                LocalModelApprovedArtifact approvedArtifact = new LocalModelApprovedArtifact(
                    LocalLlmBehaviorContract.ManifestId,
                    LocalLlmBehaviorContract.ModelId,
                    modelPath,
                    LocalLlmBehaviorContract.ArtifactBytes,
                    LocalLlmBehaviorContract.ArtifactSha256);
                WriteCheckpoint(
                    "model_load_started",
                    "Creating provider with the governor-selected execution profile; no ambient profile mutation is allowed.");
                Stopwatch loadStopwatch = Stopwatch.StartNew();
                LocalLlmProviderCreationResult creation = await LocalLlmProvider.CreateAsync(
                    manifest,
                    approvedArtifact,
                    effectiveProfile,
                    CancellationToken.None).ConfigureAwait(true);
                loadStopwatch.Stop();
                if (creation.Status == LocalLlmProviderCreationStatus.InvalidConfiguration &&
                    creation.MandatoryPromptTokens > 0)
                {
                    // Admission runs before the model is resident, so it cannot know how many
                    // tokens the contract's mandatory prompt costs under this model's
                    // tokenizer. Creation measured it and refused a context that could not
                    // hold it. Re-evaluate admission once against the real number rather than
                    // estimating it, then load at the profile that can actually serve a
                    // request. This is provider creation, not a replayed generation.
                    WriteCheckpoint(
                        "admission_remeasured",
                        "Creation refused the admitted context; re-evaluating admission with the " +
                        "measured mandatory prompt. mandatory_prompt_tokens=" +
                        creation.MandatoryPromptTokens.ToString(CultureInfo.InvariantCulture) +
                        " refused_ctx=" + effectiveProfile.ContextTokens.ToString(
                            CultureInfo.InvariantCulture));
                    admission = LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                        baselineProfile,
                        governor,
                        androidSignals,
                        realPhysics,
                        LocalLlmBehaviorContract.ArtifactBytes,
                        creation.MandatoryPromptTokens);
                    if (!admission.Succeeded || admission.Decision == null ||
                        admission.EffectiveProfile == null)
                    {
                        throw new InvalidOperationException(
                            "RMA-135 admission refused local inference once the mandatory prompt " +
                            "was measured (" +
                            creation.MandatoryPromptTokens.ToString(CultureInfo.InvariantCulture) +
                            " tokens): status=" + admission.Status + " detail=" + admission.Detail);
                    }
                    effectiveProfile = admission.EffectiveProfile;
                    loadStopwatch = Stopwatch.StartNew();
                    creation = await LocalLlmProvider.CreateAsync(
                        manifest,
                        approvedArtifact,
                        effectiveProfile,
                        CancellationToken.None).ConfigureAwait(true);
                    loadStopwatch.Stop();
                    WriteCheckpoint(
                        "admission_remeasured_reloaded",
                        "status=" + creation.Status +
                        " ctx=" + effectiveProfile.ContextTokens.ToString(
                            CultureInfo.InvariantCulture) +
                        " threads=" + effectiveProfile.Threads.ToString(
                            CultureInfo.InvariantCulture));
                }
                if (!creation.Succeeded || creation.Provider == null)
                {
                    throw new InvalidOperationException(
                        "RMA-135 local LLM provider creation failed: status=" + creation.Status +
                        " native=" + creation.NativeStatus + " detail=" + creation.Detail);
                }
                provider = creation.Provider;
                WriteCheckpoint(
                    "model_loaded",
                    "load_ms=" + loadStopwatch.Elapsed.TotalMilliseconds.ToString(
                        "F3", CultureInfo.InvariantCulture));

                Rma135FaultInjectingPhysicsBudgetSource faultPhysics =
                    new Rma135FaultInjectingPhysicsBudgetSource(realPhysics);
                LocalLlmGovernedGenerationCoordinator coordinator =
                    new LocalLlmGovernedGenerationCoordinator(
                        provider,
                        baselineProfile,
                        governor,
                        androidSignals,
                        faultPhysics,
                        MonitorInterval);

                WriteCheckpoint(
                    "post_load_stabilization_started",
                    "Re-evaluating the real physics/resource envelope after model load before any generation request.");
                int postLoadStabilizationObservations = 0;
                LocalLlmGovernorDecision? postLoadInitialDecision = null;
                LocalLlmGovernorDecision? postLoadStabilizedDecision = null;
                LocalLlmGovernorDecision? postLoadLastObservedDecision = null;
                while (postLoadStabilizationObservations < 12)
                {
                    if (postLoadStabilizationObservations > 0)
                    {
                        await Task.Delay(30).ConfigureAwait(true);
                    }
                    ++postLoadStabilizationObservations;
                    LocalLlmGovernorDecision observed = coordinator.EvaluateCurrentBudget();
                    postLoadInitialDecision ??= observed;
                    postLoadLastObservedDecision = observed;
                    if (observed.InferenceAllowed && observed.EffectiveProfile != null &&
                        LocalLlmGovernedGenerationCoordinator.ProfileFitsWithin(
                            provider.ExecutionProfile, observed.EffectiveProfile))
                    {
                        postLoadStabilizedDecision = observed;
                        break;
                    }
                }
                if (postLoadInitialDecision == null)
                {
                    throw new InvalidOperationException(
                        "RMA-135 post-load stabilization recorded no governor observation.");
                }
                LocalLlmGovernorDecision initialDecision = postLoadInitialDecision;
                LocalLlmGovernorDecision? stabilizedDecision = postLoadStabilizedDecision;
                if (stabilizedDecision == null)
                {
                    // Diagnostics-only: the terminal acceptance report is never populated on
                    // this path (the exception below aborts before the report object is
                    // built), so without this checkpoint every field that would explain *why*
                    // admission was refused (governor mode/reasons, last real physics sample)
                    // is lost. Emit it before throwing; it does not change acceptance behavior.
                    // Memory is reported alongside the governor reasons because a
                    // MemoryPressure reason is only actionable next to the available-memory
                    // value and the threshold it fell under.
                    LocalLlmResourceSnapshot exhaustedResources = androidSignals.Capture(
                        faultPhysics.LastObservedRealState);
                    TryWriteCheckpoint(
                        "post_load_stabilization_exhausted",
                        "samples=" + postLoadStabilizationObservations.ToString(CultureInfo.InvariantCulture) +
                        " initial_mode=" + initialDecision.Mode +
                        " initial_reasons=" + initialDecision.Reasons +
                        " last_mode=" + postLoadLastObservedDecision?.Mode +
                        " last_reasons=" + postLoadLastObservedDecision?.Reasons +
                        " last_real_physics_state=" + faultPhysics.LastObservedRealState +
                        " available_memory=" + exhaustedResources.AvailableMemoryBytes.ToString(
                            CultureInfo.InvariantCulture) +
                        " total_memory=" + exhaustedResources.TotalMemoryBytes.ToString(
                            CultureInfo.InvariantCulture) +
                        " low_memory_threshold=" + exhaustedResources.LowMemoryThresholdBytes.ToString(
                            CultureInfo.InvariantCulture) +
                        " loaded_ctx=" + provider.ExecutionProfile.ContextTokens.ToString(
                            CultureInfo.InvariantCulture) +
                        " loaded_threads=" + provider.ExecutionProfile.Threads.ToString(
                            CultureInfo.InvariantCulture) +
                        " last_allowed_ctx=" + (postLoadLastObservedDecision?.EffectiveProfile?.ContextTokens
                            .ToString(CultureInfo.InvariantCulture) ?? "suspended") +
                        " last_allowed_threads=" + (postLoadLastObservedDecision?.EffectiveProfile?.Threads
                            .ToString(CultureInfo.InvariantCulture) ?? "suspended"));
                    // The loaded profile no longer fits the allowed envelope, which no amount
                    // of further observation can change: the governor will keep offering a
                    // smaller profile than the one already resident. RMA-135 answers this
                    // with explicit provider recreation rather than by mutating the live
                    // provider, so exercise exactly that documented path here.
                    WriteCheckpoint(
                        "provider_recreation_started",
                        "The loaded profile exceeds the allowed envelope; explicitly recreating the provider at the currently allowed profile. No generation request has been started or replayed.");
                    LocalLlmProviderRecreationResult recreation =
                        await LocalLlmProviderRecreation.ForCurrentEnvelopeAsync(
                            provider,
                            manifest,
                            approvedArtifact,
                            baselineProfile,
                            governor,
                            androidSignals,
                            faultPhysics,
                            CancellationToken.None).ConfigureAwait(true);
                    // Adopt whatever the recreation reports, including null, so the outer
                    // cleanup never disposes a provider that was already released.
                    provider = recreation.Provider;
                    LocalLlmGovernorDecision? recreatedDecision = recreation.Decision;
                    LocalLlmExecutionProfile? recreatedAllowed = recreatedDecision?.EffectiveProfile;
                    if (!recreation.Succeeded || provider == null ||
                        recreatedDecision == null || recreatedAllowed == null)
                    {
                        TryWriteCheckpoint(
                            "provider_recreation_failed",
                            "status=" + recreation.Status + " detail=" + recreation.Detail);
                        throw new InvalidOperationException(
                            "The real post-model-load physics/resource envelope did not recover enough to admit the already-loaded provider after " +
                            postLoadStabilizationObservations.ToString(CultureInfo.InvariantCulture) +
                            " observations, and explicit provider recreation did not yield a runnable provider: status=" +
                            recreation.Status + " detail=" + recreation.Detail);
                    }
                    if (!LocalLlmGovernedGenerationCoordinator.ProfileFitsWithin(
                        provider.ExecutionProfile,
                        recreatedAllowed))
                    {
                        throw new InvalidOperationException(
                            "RMA-135 recreated the local provider but the resulting profile still exceeds the allowed envelope.");
                    }
                    coordinator = new LocalLlmGovernedGenerationCoordinator(
                        provider,
                        baselineProfile,
                        governor,
                        androidSignals,
                        faultPhysics,
                        MonitorInterval);
                    stabilizedDecision = recreatedDecision;
                    WriteCheckpoint(
                        "provider_recreated",
                        "status=" + recreation.Status +
                        " mode=" + recreatedDecision.Mode +
                        " reasons=" + recreatedDecision.Reasons +
                        " recreated_ctx=" + provider.ExecutionProfile.ContextTokens.ToString(
                            CultureInfo.InvariantCulture) +
                        " recreated_threads=" + provider.ExecutionProfile.Threads.ToString(
                            CultureInfo.InvariantCulture));
                }
                LocalLlmResourceSnapshot postLoadResources = androidSignals.Capture(
                    LocalLlmPhysicsBudgetState.Healthy);
                WriteCheckpoint(
                    "post_load_stabilized",
                    "samples=" + postLoadStabilizationObservations.ToString(CultureInfo.InvariantCulture) +
                    " initial_mode=" + initialDecision.Mode +
                    " initial_reasons=" + initialDecision.Reasons +
                    " final_mode=" + stabilizedDecision.Mode +
                    " final_reasons=" + stabilizedDecision.Reasons +
                    " available_memory=" + postLoadResources.AvailableMemoryBytes.ToString(
                        CultureInfo.InvariantCulture));

                ReachySimulationTimingSnapshot beforeInjection =
                    await WaitForTimingProgressAsync(
                        productionRuntime,
                        checked(workerBeforeAdmission.TotalStepCount + 5UL)).ConfigureAwait(true);
                faultPhysics.ArmOneShotExceededAfterPassThrough();
                var cancellationSink = new Rma135CollectingSink();
                WriteCheckpoint(
                    "physics_fault_injection_generation_started",
                    "Controlled one-shot PhysicsBudgetState.Exceeded is armed after the real preflight sample.");
                LocalLlmGovernedGenerationResult injected;
                using (var injectedTimeout = new CancellationTokenSource(GenerationTimeout))
                {
                    injected = await coordinator.GenerateAsync(
                        CreateRequest(
                            "rma135-physics-fault",
                            "Please give a friendly short acknowledgment."),
                        cancellationSink,
                        injectedTimeout.Token).ConfigureAwait(true);
                }
                WriteCheckpoint(
                    "physics_fault_injection_generation_completed",
                    "governed_status=" + injected.Status + " provider_status=" +
                    (injected.ProviderResult?.Status.ToString() ?? "none") +
                    " injected_count=" + faultPhysics.InjectedCount.ToString(CultureInfo.InvariantCulture));
                if (injected.Status != LocalLlmGovernedGenerationStatus.ResourceCancelledDuringGeneration)
                {
                    throw new InvalidOperationException(
                        "Controlled physics-budget violation did not cancel local inference: status=" +
                        injected.Status + " detail=" + injected.Detail);
                }
                if (faultPhysics.InjectedCount != 1)
                {
                    throw new InvalidOperationException(
                        "RMA-135 physics fault injection was not consumed exactly once.");
                }
                if (injected.ProviderResult?.Status != LocalLlmGenerationStatus.Cancelled)
                {
                    throw new InvalidOperationException(
                        "RMA-135 governed cancellation did not terminate the provider as Cancelled: " +
                        (injected.ProviderResult?.Status.ToString() ?? "none"));
                }

                ReachySimulationTimingSnapshot afterInjection =
                    await WaitForTimingProgressAsync(
                        productionRuntime,
                        checked(beforeInjection.TotalStepCount + 5UL)).ConfigureAwait(true);
                if (afterInjection.TotalStepCount <= beforeInjection.TotalStepCount)
                {
                    throw new InvalidOperationException(
                        "The authoritative simulation worker did not advance across LLM cancellation.");
                }
                WriteCheckpoint(
                    "physics_continuity_verified",
                    "worker_steps_before=" + beforeInjection.TotalStepCount.ToString(
                        CultureInfo.InvariantCulture) + " worker_steps_after=" +
                    afterInjection.TotalStepCount.ToString(CultureInfo.InvariantCulture));

                int recoverySamples = 0;
                LocalLlmGovernorDecision? recoveredDecision = null;
                LocalLlmGovernorDecision? recoveryLastObservedDecision = null;
                WriteCheckpoint(
                    "governor_recovery_started",
                    "Waiting for explicit healthy observations after injected suspension; no request is replayed.");
                while (recoverySamples < RecoveryObservationBudget)
                {
                    await Task.Delay(RecoveryObservationInterval).ConfigureAwait(true);
                    ++recoverySamples;
                    LocalLlmGovernorDecision decision = coordinator.EvaluateCurrentBudget();
                    recoveryLastObservedDecision = decision;
                    if (decision.Mode != LocalLlmGovernorMode.Suspended)
                    {
                        recoveredDecision = decision;
                        break;
                    }
                }
                if (recoveredDecision == null)
                {
                    // Diagnostics-only, mirrors the post-load-stabilization-exhausted
                    // checkpoint above: the terminal report is never populated on this path,
                    // so record the last observed mode/reasons before throwing.
                    TryWriteCheckpoint(
                        "governor_recovery_exhausted",
                        "samples=" + recoverySamples.ToString(CultureInfo.InvariantCulture) +
                        " last_mode=" + recoveryLastObservedDecision?.Mode +
                        " last_reasons=" + recoveryLastObservedDecision?.Reasons +
                        " last_real_physics_state=" + faultPhysics.LastObservedRealState);
                    throw new InvalidOperationException(
                        "RMA-135 governor did not recover from the controlled suspension after " +
                        RecoveryObservationBudget.ToString(CultureInfo.InvariantCulture) +
                        " observations.");
                }
                WriteCheckpoint(
                    "governor_recovered",
                    "samples=" + recoverySamples.ToString(CultureInfo.InvariantCulture) +
                    " mode=" + recoveredDecision.Mode);

                int postRecoveryAttempts = 0;
                string firstPostRecoveryRefusal = string.Empty;
                Rma135CollectingSink successSink;
                LocalLlmGovernedGenerationResult recoveredGeneration;
                while (true)
                {
                    ++postRecoveryAttempts;
                    successSink = new Rma135CollectingSink();
                    WriteCheckpoint(
                        "post_recovery_generation_started",
                        "attempt=" + postRecoveryAttempts.ToString(CultureInfo.InvariantCulture) +
                        " Starting a new governed request without app/process restart; the cancelled request is not replayed.");
                    using (var successTimeout = new CancellationTokenSource(GenerationTimeout))
                    {
                        recoveredGeneration = await coordinator.GenerateAsync(
                            CreateRequest(
                                "rma135-recovered-success-" +
                                postRecoveryAttempts.ToString(CultureInfo.InvariantCulture),
                                SuccessPrompt),
                            successSink,
                            successTimeout.Token).ConfigureAwait(true);
                    }
                    WriteCheckpoint(
                        "post_recovery_generation_completed",
                        "attempt=" + postRecoveryAttempts.ToString(CultureInfo.InvariantCulture) +
                        " governed_status=" + recoveredGeneration.Status + " provider_status=" +
                        (recoveredGeneration.ProviderResult?.Status.ToString() ?? "none") +
                        " text_events=" + successSink.TextEventCount.ToString(CultureInfo.InvariantCulture));
                    if (recoveredGeneration.Succeeded)
                    {
                        break;
                    }
                    if (postRecoveryAttempts == 1)
                    {
                        firstPostRecoveryRefusal = recoveredGeneration.Status.ToString();
                    }
                    bool transientResourcePressure =
                        recoveredGeneration.Status ==
                            LocalLlmGovernedGenerationStatus.ResourceCancelledDuringGeneration ||
                        recoveredGeneration.Status ==
                            LocalLlmGovernedGenerationStatus.ResourceSuspendedBeforeStart;
                    if (!transientResourcePressure ||
                        postRecoveryAttempts >= PostRecoveryGenerationAttemptBudget)
                    {
                        break;
                    }
                    await Task.Delay(PostRecoveryRetryInterval).ConfigureAwait(true);
                }
                if (!recoveredGeneration.Succeeded)
                {
                    TryWriteCheckpoint(
                        "post_recovery_generation_exhausted",
                        "attempts=" + postRecoveryAttempts.ToString(CultureInfo.InvariantCulture) +
                        " first_refusal=" + firstPostRecoveryRefusal +
                        " last_status=" + recoveredGeneration.Status);
                }
                ValidateSuccessfulGeneration(
                    recoveredGeneration,
                    successSink,
                    "post-recovery governed generation after " +
                    postRecoveryAttempts.ToString(CultureInfo.InvariantCulture) + " attempt(s)");

                ReachySimulationTimingSnapshot finalWorker =
                    await WaitForTimingProgressAsync(
                        productionRuntime,
                        checked(afterInjection.TotalStepCount + 5UL)).ConfigureAwait(true);
                LocalLlmPhysicsBudgetState finalPhysics = realPhysics.Capture();
                LocalLlmResourceSnapshot finalResources = androidSignals.Capture(finalPhysics);
                WriteCheckpoint(
                    "final_observation_completed",
                    "steps=" + finalWorker.TotalStepCount.ToString(CultureInfo.InvariantCulture) +
                    " physics=" + finalPhysics + " thermal=" + finalResources.ThermalStatus +
                    " available_memory=" + finalResources.AvailableMemoryBytes.ToString(
                        CultureInfo.InvariantCulture));

                LocalLlmGenerationMetrics? generationMetrics = recoveredGeneration.ProviderResult?.Metrics;
                return new Rma135AcceptanceReport
                {
                    status = "running",
                    error = string.Empty,
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                    android_api_level = ReadApiLevel(),
                    reachy_llama_abi = checked((int)nativeAbi),
                    manifest_id = LocalLlmBehaviorContract.ManifestId,
                    model_id = LocalLlmBehaviorContract.ModelId,
                    artifact_sha256 = artifact.sha256,
                    artifact_bytes = artifact.bytes,
                    model_load_milliseconds = loadStopwatch.Elapsed.TotalMilliseconds,
                    total_memory_bytes = initialResources.TotalMemoryBytes,
                    initial_available_memory_bytes = initialResources.AvailableMemoryBytes,
                    final_available_memory_bytes = finalResources.AvailableMemoryBytes,
                    low_memory_threshold_bytes = initialResources.LowMemoryThresholdBytes,
                    android_low_memory_initial = initialResources.SystemReportsLowMemory,
                    android_low_memory_final = finalResources.SystemReportsLowMemory,
                    logical_processor_count = initialResources.LogicalProcessorCount,
                    thermal_status_initial = initialResources.ThermalStatus.ToString(),
                    thermal_status_final = finalResources.ThermalStatus.ToString(),
                    admission_mode = admission.Decision.Mode.ToString(),
                    admission_device_profile = admission.Decision.DeviceProfile.Kind.ToString(),
                    admission_reasons = admission.Decision.Reasons.ToString(),
                    effective_context_tokens = effectiveProfile.ContextTokens,
                    effective_batch_tokens = effectiveProfile.BatchTokens,
                    effective_micro_batch_tokens = effectiveProfile.MicroBatchTokens,
                    effective_threads = effectiveProfile.Threads,
                    effective_batch_threads = effectiveProfile.BatchThreads,
                    startup_physics_observations = startupPhysics.Observations,
                    startup_physics_exceeded_observations = startupPhysics.ExceededObservations,
                    startup_physics_state = startupPhysics.State.ToString(),
                    production_runtime_model_hash = productionRuntime.ModelHash,
                    post_load_available_memory_bytes = postLoadResources.AvailableMemoryBytes,
                    post_load_stabilization_observations = postLoadStabilizationObservations,
                    post_load_initial_mode = initialDecision.Mode.ToString(),
                    post_load_initial_reasons = initialDecision.Reasons.ToString(),
                    post_load_stabilized_mode = stabilizedDecision.Mode.ToString(),
                    post_load_stabilized_reasons = stabilizedDecision.Reasons.ToString(),
                    physics_fault_injection_kind = "controlled_one_shot_budget_exceeded",
                    physics_fault_injection_count = faultPhysics.InjectedCount,
                    physics_underlying_state_at_injection = faultPhysics.UnderlyingStateAtInjection.ToString(),
                    fault_injection_governed_status = injected.Status.ToString(),
                    fault_injection_provider_status = injected.ProviderResult?.Status.ToString() ?? "none",
                    worker_steps_before_injection = beforeInjection.TotalStepCount,
                    worker_steps_after_injection = afterInjection.TotalStepCount,
                    worker_deadline_misses_before_injection = beforeInjection.DeadlineMissCount,
                    worker_deadline_misses_after_injection = afterInjection.DeadlineMissCount,
                    worker_accumulated_lag_seconds_after_injection =
                        afterInjection.AccumulatedLagSeconds,
                    worker_last_step_microseconds_after_injection =
                        afterInjection.LastStepDurationSeconds * 1000000.0,
                    worker_max_step_microseconds_after_injection =
                        afterInjection.MaximumStepDurationSeconds * 1000000.0,
                    recovery_observations = recoverySamples,
                    recovery_mode = recoveredDecision.Mode.ToString(),
                    post_recovery_governed_status = recoveredGeneration.Status.ToString(),
                    post_recovery_provider_status =
                        recoveredGeneration.ProviderResult?.Status.ToString() ?? "none",
                    post_recovery_stream_text_events = successSink.TextEventCount,
                    post_recovery_prompt_tokens = generationMetrics?.PromptTokens ?? 0UL,
                    post_recovery_generated_tokens = generationMetrics?.GeneratedTokens ?? 0UL,
                    final_physics_budget_state = finalPhysics.ToString(),
                    final_worker_steps = finalWorker.TotalStepCount,
                    final_worker_deadline_misses = finalWorker.DeadlineMissCount,
                    network_fallback_used = false,
                    automatic_retry_used = false,
                    physics_timestep_modified = false,
                    json_repair_used = false,
                    report_contains_prompt_or_response_content = false,
                };
            }
            finally
            {
                TryWriteCheckpoint("cleanup_started", "Disposing RMA-135 physical acceptance resources.");
                if (provider != null)
                {
                    await provider.DisposeAsync().ConfigureAwait(true);
                    TryWriteCheckpoint("provider_disposed", "Managed local LLM provider disposed.");
                }
                TryWriteCheckpoint(
                    "production_physics_runtime_preserved",
                    "RMA-135 did not own or dispose the app's authoritative simulation worker.");
            }
        }

        private static async Task<ReachyProductionAuthoritativeRuntime> WaitForProductionRuntimeAsync()
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                ReachyProductionAuthoritativeRuntime? runtime =
                    UnityEngine.Object.FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>();
                if (runtime != null)
                {
                    if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                    {
                        throw new InvalidOperationException(
                            "The production authoritative runtime faulted before RMA-135 acceptance: " +
                            runtime.Fault);
                    }
                    if (runtime.Status == ReachyProductionRuntimeStatus.Running &&
                        runtime.SimulationRunState == ReachySimulationRunState.Running)
                    {
                        return runtime;
                    }
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for the app's production authoritative simulation runtime.");
        }

        private static async Task<Rma135PhysicsStartupStabilization> WaitForPhysicsBudgetAsync(
            ReachySimulationLocalLlmPhysicsBudgetSource source)
        {
            const int requiredConsecutiveAdmissible = 3;
            int consecutiveAdmissible = 0;
            int exceededObservations = 0;
            ulong minimumObservedStepCount = 5UL;
            for (int attempt = 1; attempt <= 100; ++attempt)
            {
                LocalLlmPhysicsBudgetState state = source.Capture();
                if (state == LocalLlmPhysicsBudgetState.Exceeded)
                {
                    ++exceededObservations;
                    consecutiveAdmissible = 0;
                }
                else if (state == LocalLlmPhysicsBudgetState.Healthy ||
                    state == LocalLlmPhysicsBudgetState.AtRisk)
                {
                    ++consecutiveAdmissible;
                    if (consecutiveAdmissible >= requiredConsecutiveAdmissible)
                    {
                        return new Rma135PhysicsStartupStabilization(
                            state,
                            attempt,
                            exceededObservations,
                            minimumObservedStepCount);
                    }
                }
                else
                {
                    consecutiveAdmissible = 0;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new InvalidOperationException(
                "The app's production authoritative simulation did not provide three consecutive admissible timing observations before local LLM acceptance; exceeded_observations=" +
                exceededObservations.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static async Task<ReachySimulationTimingSnapshot> WaitForTimingProgressAsync(
            IReachySimulationTimingSource timingSource,
            ulong minimumStepCount)
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                if (timingSource.SimulationRunState == ReachySimulationRunState.Faulted)
                {
                    throw new InvalidOperationException(
                        "The production authoritative simulation worker faulted during RMA-135 acceptance.");
                }
                if (timingSource.TryGetLatestTimingSnapshot(
                    out ReachySimulationTimingSnapshot timing) &&
                    timing.TotalStepCount >= minimumStepCount)
                {
                    return timing;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for production authoritative simulation progress to step " +
                minimumStepCount.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateSuccessfulGeneration(
            LocalLlmGovernedGenerationResult result,
            Rma135CollectingSink sink,
            string label)
        {
            if (!result.Succeeded || result.ProviderResult?.Intent == null)
            {
                throw new InvalidOperationException(
                    label + " did not produce a validated intent: governed_status=" + result.Status +
                    " provider_status=" + (result.ProviderResult?.Status.ToString() ?? "none") +
                    " detail=" + result.Detail);
            }
            if (!sink.TerminalValidated || sink.TextEventCount <= 0)
            {
                throw new InvalidOperationException(
                    label + " did not demonstrate ordered streaming followed by validated completion.");
            }
            if (sink.SawTrustedPartialOutput)
            {
                throw new InvalidOperationException(
                    label + " marked partial generated text as trusted/executable.");
            }
        }

        private static Rma135ArtifactVerification VerifyArtifact(string modelPath)
        {
            FileInfo file = new FileInfo(modelPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    "The exact RMA-133 selected model was not staged for RMA-135 acceptance.",
                    modelPath);
            }
            if (file.Length != LocalLlmBehaviorContract.ArtifactBytes)
            {
                throw new InvalidOperationException(
                    "RMA-135 staged model size mismatch: expected " +
                    LocalLlmBehaviorContract.ArtifactBytes + ", received " + file.Length + ".");
            }
            string hash;
            using (FileStream stream = new FileStream(
                modelPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; ++index)
                {
                    builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                hash = builder.ToString();
            }
            if (!string.Equals(
                hash,
                LocalLlmBehaviorContract.ArtifactSha256,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RMA-135 staged model SHA-256 mismatch: " + hash + ".");
            }
            return new Rma135ArtifactVerification { bytes = file.Length, sha256 = hash };
        }

        private static LocalModelManifest CreateSelectedManifest()
        {
            return new LocalModelManifest(
                1,
                new LocalModelIdentity(
                    LocalLlmBehaviorContract.ManifestId,
                    LocalLlmBehaviorContract.ModelId,
                    "Qwen3 0.6B Q4_K_M",
                    "q4_k_m-8e42d41",
                    new Uri("https://huggingface.co/Qwen/Qwen3-0.6B-GGUF"),
                    "8e42d41f70cb6c571f58c3f31bd9287b372d97cc",
                    "Apache-2.0",
                    false,
                    string.Empty),
                new LocalModelRuntimeRequirement("reachy_llama", 2, false),
                new LocalModelArtifact(
                    "qwen3/qwen3-0.6b-q4_k_m.gguf",
                    LocalLlmBehaviorContract.ArtifactBytes,
                    LocalLlmBehaviorContract.ArtifactSha256),
                new LocalModelGgufMetadata(
                    3,
                    "qwen3",
                    "Q4_K_M",
                    596049920L,
                    "gpt2",
                    "qwen2"),
                new LocalModelInferenceProfile(
                    40960,
                    "GGUF-embedded template is authoritative for RMA-134 generation.",
                    new[] { "<|im_end|>", "<|endoftext|>" },
                    new LocalModelMemoryEstimate(740380672L, 2048, 256),
                    4),
                new LocalModelDeviceCompatibility(
                    new[] { "arm64-v8a" },
                    26,
                    Array.Empty<string>(),
                    740380672L,
                    2));
        }

        private static LocalLlmGenerationRequest CreateRequest(string requestId, string prompt)
        {
            return new LocalLlmGenerationRequest(
                requestId,
                new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, prompt) });
        }
    }
}
