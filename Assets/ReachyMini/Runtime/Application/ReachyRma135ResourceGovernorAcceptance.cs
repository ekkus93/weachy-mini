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
using ReachyMini.Simulation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed class ReachyRma135ResourceGovernorAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName = "reachy_rma135_resource_governor_acceptance";
        internal const string ResultFileName = "rma135-resource-governor-acceptance.json";
        internal const string ModelFileName = "rma135-qwen3-0.6b-q4_k_m.gguf";
        internal const string CheckpointFilePrefix = "rma135-resource-governor-checkpoint-";

        private const string SimulationModelResourcePath = "ReachyMiniRuntime/reachy_mini_mjb";
        private const string SuccessPrompt =
            "The person says hello and asks you to greet them warmly. No gaze target is requested.";
        private static readonly TimeSpan WorkerControlTimeout = TimeSpan.FromSeconds(10.0);
        private static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(180.0);
        private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(25.0);

        private static string bootstrapError = string.Empty;
        private static bool unhandledFailure;
        private static string unhandledFailureMessage = string.Empty;
        private static int checkpointSequence;
        private static Stopwatch? checkpointStopwatch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            bool requested;
            try
            {
                requested = ReadBooleanLaunchExtra(LaunchExtraName, false);
            }
            catch (Exception exception)
            {
                requested = true;
                bootstrapError = "Failed to read the RMA-135 launch extra: " + exception.Message;
            }
            if (!requested)
            {
                return;
            }

            try
            {
                InitializeCheckpointRun();
                WriteCheckpoint("bootstrap_started", "RMA-135 launch extra accepted.");
            }
            catch (Exception exception)
            {
                bootstrapError = "Failed to initialize RMA-135 checkpoints: " + exception.Message;
                Debug.LogError(bootstrapError);
            }

            GameObject host = new GameObject("ReachyRma135ResourceGovernorAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma135ResourceGovernorAcceptance>();
            TryWriteCheckpoint("bootstrap_component_installed", "Acceptance MonoBehaviour installed.");
        }

        private async void Start()
        {
            UnityEngine.Application.logMessageReceivedThreaded += HandleLogMessage;
            string resultPath = Path.Combine(UnityEngine.Application.persistentDataPath, ResultFileName);
            try
            {
                WriteCheckpoint("start_entered", "RMA-135 physical acceptance Start entered.");
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                if (!string.IsNullOrEmpty(bootstrapError))
                {
                    throw new InvalidOperationException(bootstrapError);
                }

                Rma135AcceptanceReport report = await RunAcceptanceAsync().ConfigureAwait(true);
                if (unhandledFailure)
                {
                    throw new InvalidOperationException(
                        "An unhandled Unity exception occurred during RMA-135 acceptance: " +
                        unhandledFailureMessage);
                }
                report.status = "passed";
                WriteReport(resultPath, report);
                WriteCheckpoint("passed", "Final RMA-135 acceptance report committed.");
                Debug.Log("RMA-135 resource governor physical acceptance passed.");
            }
            catch (Exception exception)
            {
                TryWriteCheckpoint("failed", Bound(exception.Message, 1024));
                Rma135AcceptanceReport failure = new Rma135AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                    android_api_level = TryReadApiLevel(),
                };
                try
                {
                    WriteReport(resultPath, failure);
                }
                catch (Exception reportException)
                {
                    Debug.LogError("RMA-135 could not write its failure report: " + reportException);
                }
                Debug.LogError("RMA-135 resource governor physical acceptance failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceivedThreaded -= HandleLogMessage;
            }
        }

        private static async Task<Rma135AcceptanceReport> RunAcceptanceAsync()
        {
            string modelPath = Path.Combine(UnityEngine.Application.persistentDataPath, ModelFileName);
            WriteCheckpoint("artifact_verification_started", "Verifying exact RMA-133 selected GGUF artifact.");
            ArtifactVerification artifact = await Task.Run(() => VerifyArtifact(modelPath)).ConfigureAwait(true);
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

            ReachySimulationWorker? worker = null;
            LocalLlmProvider? provider = null;
            using var androidSignals = new ReachyAndroidLocalLlmResourceSignalSource();
            try
            {
                WriteCheckpoint(
                    "physics_worker_started",
                    "Creating production-shape MuJoCo session, authoritative state reader, and ReachySimulationWorker.");
                worker = CreateAndStartSimulationWorker();
                var realPhysics = new ReachySimulationLocalLlmPhysicsBudgetSource(worker);
                LocalLlmPhysicsBudgetState readyPhysics = await WaitForPhysicsBudgetAsync(realPhysics)
                    .ConfigureAwait(true);
                ReachyPublishedSimulationSnapshot workerBeforeAdmission =
                    await WaitForWorkerProgressAsync(worker, 5UL).ConfigureAwait(true);
                WriteCheckpoint(
                    "physics_worker_ready",
                    "state=" + readyPhysics + " steps=" +
                    workerBeforeAdmission.Timing.TotalStepCount.ToString(CultureInfo.InvariantCulture));

                LocalLlmResourceSnapshot initialResources = androidSignals.Capture(readyPhysics);
                LocalLlmExecutionProfile baselineProfile =
                    LocalLlmExecutionProfile.CreateRma133V6Baseline();
                var governor = new LocalLlmResourceGovernor();
                LocalLlmProviderAdmissionResult admission =
                    LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                        baselineProfile,
                        governor,
                        androidSignals,
                        realPhysics);
                if (!admission.Succeeded || admission.Decision == null ||
                    admission.EffectiveProfile == null)
                {
                    throw new InvalidOperationException(
                        "RMA-135 provider admission refused local inference: status=" +
                        admission.Status + " detail=" + admission.Detail);
                }
                LocalLlmExecutionProfile effectiveProfile = admission.EffectiveProfile;
                WriteCheckpoint(
                    "resource_admission_ready",
                    "mode=" + admission.Decision.Mode + " device=" +
                    admission.Decision.DeviceProfile.Kind + " thermal=" +
                    initialResources.ThermalStatus + " ctx=" +
                    effectiveProfile.ContextTokens.ToString(CultureInfo.InvariantCulture) +
                    " threads=" + effectiveProfile.Threads.ToString(CultureInfo.InvariantCulture));

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

                var faultPhysics = new FaultInjectingPhysicsBudgetSource(realPhysics);
                var coordinator = new LocalLlmGovernedGenerationCoordinator(
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
                while (postLoadStabilizationObservations < 12)
                {
                    if (postLoadStabilizationObservations > 0)
                    {
                        await Task.Delay(30).ConfigureAwait(true);
                    }
                    ++postLoadStabilizationObservations;
                    LocalLlmGovernorDecision observed = coordinator.EvaluateCurrentBudget();
                    postLoadInitialDecision ??= observed;
                    if (observed.InferenceAllowed && observed.EffectiveProfile != null &&
                        LocalLlmGovernedGenerationCoordinator.ProfileFitsWithin(
                            provider.ExecutionProfile, observed.EffectiveProfile))
                    {
                        postLoadStabilizedDecision = observed;
                        break;
                    }
                }
                if (postLoadInitialDecision == null || postLoadStabilizedDecision == null)
                {
                    throw new InvalidOperationException(
                        "The real post-model-load physics/resource envelope did not recover enough to admit the already-loaded provider after " +
                        postLoadStabilizationObservations.ToString(CultureInfo.InvariantCulture) +
                        " observations. No generation request was started.");
                }
                LocalLlmResourceSnapshot postLoadResources = androidSignals.Capture(
                    LocalLlmPhysicsBudgetState.Healthy);
                WriteCheckpoint(
                    "post_load_stabilized",
                    "samples=" + postLoadStabilizationObservations.ToString(CultureInfo.InvariantCulture) +
                    " initial_mode=" + postLoadInitialDecision.Mode +
                    " initial_reasons=" + postLoadInitialDecision.Reasons +
                    " final_mode=" + postLoadStabilizedDecision.Mode +
                    " final_reasons=" + postLoadStabilizedDecision.Reasons +
                    " available_memory=" + postLoadResources.AvailableMemoryBytes.ToString(
                        CultureInfo.InvariantCulture));

                ReachyPublishedSimulationSnapshot beforeInjection =
                    await WaitForWorkerProgressAsync(
                        worker,
                        checked(workerBeforeAdmission.Timing.TotalStepCount + 5UL)).ConfigureAwait(true);
                faultPhysics.ArmOneShotExceededAfterPassThrough();
                var cancellationSink = new CollectingSink();
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

                ReachyPublishedSimulationSnapshot afterInjection =
                    await WaitForWorkerProgressAsync(
                        worker,
                        checked(beforeInjection.Timing.TotalStepCount + 5UL)).ConfigureAwait(true);
                if (afterInjection.Timing.TotalStepCount <= beforeInjection.Timing.TotalStepCount)
                {
                    throw new InvalidOperationException(
                        "The authoritative simulation worker did not advance across LLM cancellation.");
                }
                WriteCheckpoint(
                    "physics_continuity_verified",
                    "worker_steps_before=" + beforeInjection.Timing.TotalStepCount.ToString(
                        CultureInfo.InvariantCulture) + " worker_steps_after=" +
                    afterInjection.Timing.TotalStepCount.ToString(CultureInfo.InvariantCulture));

                int recoverySamples = 0;
                LocalLlmGovernorDecision? recoveredDecision = null;
                WriteCheckpoint(
                    "governor_recovery_started",
                    "Waiting for explicit healthy observations after injected suspension; no request is replayed.");
                while (recoverySamples < 8)
                {
                    await Task.Delay(30).ConfigureAwait(true);
                    ++recoverySamples;
                    LocalLlmGovernorDecision decision = coordinator.EvaluateCurrentBudget();
                    if (decision.Mode != LocalLlmGovernorMode.Suspended)
                    {
                        recoveredDecision = decision;
                        break;
                    }
                }
                if (recoveredDecision == null)
                {
                    throw new InvalidOperationException(
                        "RMA-135 governor did not recover from the controlled suspension after eight observations.");
                }
                WriteCheckpoint(
                    "governor_recovered",
                    "samples=" + recoverySamples.ToString(CultureInfo.InvariantCulture) +
                    " mode=" + recoveredDecision.Mode);

                var successSink = new CollectingSink();
                WriteCheckpoint(
                    "post_recovery_generation_started",
                    "Starting a new governed request without app/process restart; the cancelled request is not replayed.");
                LocalLlmGovernedGenerationResult recoveredGeneration;
                using (var successTimeout = new CancellationTokenSource(GenerationTimeout))
                {
                    recoveredGeneration = await coordinator.GenerateAsync(
                        CreateRequest("rma135-recovered-success", SuccessPrompt),
                        successSink,
                        successTimeout.Token).ConfigureAwait(true);
                }
                WriteCheckpoint(
                    "post_recovery_generation_completed",
                    "governed_status=" + recoveredGeneration.Status + " provider_status=" +
                    (recoveredGeneration.ProviderResult?.Status.ToString() ?? "none") +
                    " text_events=" + successSink.TextEventCount.ToString(CultureInfo.InvariantCulture));
                ValidateSuccessfulGeneration(
                    recoveredGeneration,
                    successSink,
                    "post-recovery governed generation");

                ReachyPublishedSimulationSnapshot finalWorker =
                    await WaitForWorkerProgressAsync(
                        worker,
                        checked(afterInjection.Timing.TotalStepCount + 5UL)).ConfigureAwait(true);
                LocalLlmPhysicsBudgetState finalPhysics = realPhysics.Capture();
                LocalLlmResourceSnapshot finalResources = androidSignals.Capture(finalPhysics);
                WriteCheckpoint(
                    "final_observation_completed",
                    "steps=" + finalWorker.Timing.TotalStepCount.ToString(CultureInfo.InvariantCulture) +
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
                    post_load_available_memory_bytes = postLoadResources.AvailableMemoryBytes,
                    post_load_stabilization_observations = postLoadStabilizationObservations,
                    post_load_initial_mode = postLoadInitialDecision.Mode.ToString(),
                    post_load_initial_reasons = postLoadInitialDecision.Reasons.ToString(),
                    post_load_stabilized_mode = postLoadStabilizedDecision.Mode.ToString(),
                    post_load_stabilized_reasons = postLoadStabilizedDecision.Reasons.ToString(),
                    physics_fault_injection_kind = "controlled_one_shot_budget_exceeded",
                    physics_fault_injection_count = faultPhysics.InjectedCount,
                    physics_underlying_state_at_injection = faultPhysics.UnderlyingStateAtInjection.ToString(),
                    fault_injection_governed_status = injected.Status.ToString(),
                    fault_injection_provider_status = injected.ProviderResult?.Status.ToString() ?? "none",
                    worker_steps_before_injection = beforeInjection.Timing.TotalStepCount,
                    worker_steps_after_injection = afterInjection.Timing.TotalStepCount,
                    worker_deadline_misses_before_injection = beforeInjection.Timing.DeadlineMissCount,
                    worker_deadline_misses_after_injection = afterInjection.Timing.DeadlineMissCount,
                    worker_accumulated_lag_seconds_after_injection =
                        afterInjection.Timing.AccumulatedLagSeconds,
                    worker_last_step_microseconds_after_injection =
                        afterInjection.Timing.LastStepDurationSeconds * 1000000.0,
                    worker_max_step_microseconds_after_injection =
                        afterInjection.Timing.MaximumStepDurationSeconds * 1000000.0,
                    recovery_observations = recoverySamples,
                    recovery_mode = recoveredDecision.Mode.ToString(),
                    post_recovery_governed_status = recoveredGeneration.Status.ToString(),
                    post_recovery_provider_status =
                        recoveredGeneration.ProviderResult?.Status.ToString() ?? "none",
                    post_recovery_stream_text_events = successSink.TextEventCount,
                    post_recovery_prompt_tokens = generationMetrics?.PromptTokens ?? 0UL,
                    post_recovery_generated_tokens = generationMetrics?.GeneratedTokens ?? 0UL,
                    final_physics_budget_state = finalPhysics.ToString(),
                    final_worker_steps = finalWorker.Timing.TotalStepCount,
                    final_worker_deadline_misses = finalWorker.Timing.DeadlineMissCount,
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
                if (worker != null)
                {
                    worker.Dispose();
                    TryWriteCheckpoint(
                        "worker_disposed",
                        "Authoritative simulation worker, state reader, and native session disposed by the worker owner.");
                }
            }
        }

        private static ReachySimulationWorker CreateAndStartSimulationWorker()
        {
            TextAsset? modelAsset = Resources.Load<TextAsset>(SimulationModelResourcePath);
            if (modelAsset == null || modelAsset.bytes.Length == 0)
            {
                throw new InvalidOperationException(
                    "The staged production MJB is unavailable for RMA-135 physical acceptance.");
            }
            ReachySimCreateResult create = ReachySimSession.Create(modelAsset.bytes);
            if (!create.IsSuccess || create.Session == null)
            {
                throw new InvalidOperationException(
                    "RMA-135 could not create the production-shape native simulation session: " +
                    create.Error.Code + ": " + create.Error.Message);
            }
            ReachySimSession simulationSession = create.Session;
            ReachySimAuthoritativeStateReader? reader = null;
            ReachySimulationWorker? worker = null;
            try
            {
                reader = new ReachySimAuthoritativeStateReader(simulationSession);
                worker = new ReachySimulationWorker(simulationSession, reader);
                reader = null;
                ReachySimulationControlResult start = worker.Start(WorkerControlTimeout);
                if (!start.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "RMA-135 authoritative simulation worker failed to start: " +
                        start.Error.Code + ": " + start.Error.Message);
                }
                return worker;
            }
            catch
            {
                if (worker != null)
                {
                    worker.Dispose();
                }
                else
                {
                    reader?.Dispose();
                    simulationSession.Dispose();
                }
                throw;
            }
        }

        private static async Task<LocalLlmPhysicsBudgetState> WaitForPhysicsBudgetAsync(
            ReachySimulationLocalLlmPhysicsBudgetSource source)
        {
            for (int attempt = 0; attempt < 100; ++attempt)
            {
                LocalLlmPhysicsBudgetState state = source.Capture();
                if (state == LocalLlmPhysicsBudgetState.Healthy ||
                    state == LocalLlmPhysicsBudgetState.AtRisk)
                {
                    return state;
                }
                if (state == LocalLlmPhysicsBudgetState.Exceeded)
                {
                    throw new InvalidOperationException(
                        "The real authoritative simulation exceeded its timing budget before LLM acceptance began.");
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for authoritative simulation physics timing to become available.");
        }

        private static async Task<ReachyPublishedSimulationSnapshot> WaitForWorkerProgressAsync(
            ReachySimulationWorker worker,
            ulong minimumStepCount)
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                if (worker.State == ReachySimulationRunState.Faulted)
                {
                    ReachySimulationFault? fault = worker.Fault;
                    throw new InvalidOperationException(
                        fault == null
                            ? "The RMA-135 simulation worker faulted without diagnostics."
                            : "RMA-135 simulation worker faulted in " + fault.Operation + ": " +
                              fault.Error.Code + ": " + fault.Error.Message);
                }
                if (worker.TryGetLatestSnapshot(out ReachyPublishedSimulationSnapshot snapshot) &&
                    snapshot.Timing.TotalStepCount >= minimumStepCount)
                {
                    return snapshot;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for authoritative simulation worker progress to step " +
                minimumStepCount.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateSuccessfulGeneration(
            LocalLlmGovernedGenerationResult result,
            CollectingSink sink,
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

        private static ArtifactVerification VerifyArtifact(string modelPath)
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
            return new ArtifactVerification { bytes = file.Length, sha256 = hash };
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

        private static bool ReadBooleanLaunchExtra(string name, bool defaultValue)
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, defaultValue);
        }

        private static int ReadApiLevel()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }

        private static int TryReadApiLevel()
        {
            try
            {
                return ReadApiLevel();
            }
            catch
            {
                return 0;
            }
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception)
            {
                return;
            }
            unhandledFailure = true;
            unhandledFailureMessage = Bound(condition + "\n" + stackTrace, 2048);
        }

        private static void InitializeCheckpointRun()
        {
            checkpointSequence = 0;
            checkpointStopwatch = Stopwatch.StartNew();
            string directory = UnityEngine.Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            foreach (string path in Directory.GetFiles(directory, CheckpointFilePrefix + "*.json"))
            {
                File.Delete(path);
            }
            foreach (string path in Directory.GetFiles(directory, CheckpointFilePrefix + "*.tmp"))
            {
                File.Delete(path);
            }
        }

        private static void WriteCheckpoint(string stage, string detail)
        {
            if (checkpointStopwatch == null)
            {
                checkpointStopwatch = Stopwatch.StartNew();
            }
            int sequence = Interlocked.Increment(ref checkpointSequence);
            var checkpoint = new Rma135AcceptanceCheckpoint
            {
                schema_version = 1,
                sequence = sequence,
                stage = stage,
                elapsed_milliseconds = checkpointStopwatch.Elapsed.TotalMilliseconds,
                managed_thread_id = Thread.CurrentThread.ManagedThreadId,
                device_model = SystemInfo.deviceModel,
                detail = Bound(detail, 1024),
            };
            string directory = UnityEngine.Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            string finalPath = Path.Combine(
                directory,
                CheckpointFilePrefix + sequence.ToString("D3", CultureInfo.InvariantCulture) + ".json");
            string temporaryPath = finalPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonUtility.ToJson(checkpoint, true) + "\n",
                new UTF8Encoding(false));
            if (File.Exists(finalPath))
            {
                File.Delete(temporaryPath);
                throw new IOException(
                    "RMA-135 checkpoint destination already exists: " + finalPath);
            }
            File.Move(temporaryPath, finalPath);
            Debug.Log(
                "RMA-135 checkpoint " + sequence.ToString(CultureInfo.InvariantCulture) +
                " stage=" + stage + " elapsed_ms=" +
                checkpoint.elapsed_milliseconds.ToString("F3", CultureInfo.InvariantCulture));
        }

        private static void TryWriteCheckpoint(string stage, string detail)
        {
            try
            {
                WriteCheckpoint(stage, detail);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "RMA-135 could not publish checkpoint '" + stage + "': " +
                    Bound(exception.Message, 1024));
            }
        }

        private static void WriteReport(string path, Rma135AcceptanceReport report)
        {
            File.WriteAllText(
                path,
                JsonUtility.ToJson(report, true) + "\n",
                new UTF8Encoding(false));
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
        }

        private sealed class FaultInjectingPhysicsBudgetSource : ILocalLlmPhysicsBudgetSource
        {
            private readonly object gate = new object();
            private readonly ILocalLlmPhysicsBudgetSource inner;
            private int passThroughCapturesBeforeInjection = -1;

            internal FaultInjectingPhysicsBudgetSource(ILocalLlmPhysicsBudgetSource inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            internal int InjectedCount { get; private set; }
            internal LocalLlmPhysicsBudgetState UnderlyingStateAtInjection { get; private set; } =
                LocalLlmPhysicsBudgetState.Unavailable;

            internal void ArmOneShotExceededAfterPassThrough()
            {
                lock (gate)
                {
                    if (passThroughCapturesBeforeInjection >= 0)
                    {
                        throw new InvalidOperationException(
                            "RMA-135 physics fault injection is already armed.");
                    }
                    passThroughCapturesBeforeInjection = 1;
                }
            }

            public LocalLlmPhysicsBudgetState Capture()
            {
                LocalLlmPhysicsBudgetState real = inner.Capture();
                lock (gate)
                {
                    if (passThroughCapturesBeforeInjection < 0)
                    {
                        return real;
                    }
                    if (passThroughCapturesBeforeInjection > 0)
                    {
                        --passThroughCapturesBeforeInjection;
                        return real;
                    }
                    passThroughCapturesBeforeInjection = -1;
                    UnderlyingStateAtInjection = real;
                    ++InjectedCount;
                    return LocalLlmPhysicsBudgetState.Exceeded;
                }
            }
        }

        private sealed class CollectingSink : ILocalLlmStreamSink
        {
            internal int TextEventCount { get; private set; }
            internal bool TerminalValidated { get; private set; }
            internal bool SawTrustedPartialOutput { get; private set; }

            public ValueTask OnEventAsync(
                LocalLlmStreamEvent streamEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (streamEvent.Type == LocalLlmStreamEventType.Text)
                {
                    ++TextEventCount;
                    SawTrustedPartialOutput |= streamEvent.IsTrustedExecutableOutput;
                }
                else if (streamEvent.Type == LocalLlmStreamEventType.Completed)
                {
                    TerminalValidated = true;
                }
                return default;
            }
        }

        [Serializable]
        private sealed class Rma135AcceptanceCheckpoint
        {
            public int schema_version;
            public int sequence;
            public string stage = string.Empty;
            public double elapsed_milliseconds;
            public int managed_thread_id;
            public string device_model = string.Empty;
            public string detail = string.Empty;
        }

        [Serializable]
        private sealed class Rma135AcceptanceReport
        {
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public string operating_system = string.Empty;
            public int android_api_level;
            public int reachy_llama_abi;
            public string manifest_id = string.Empty;
            public string model_id = string.Empty;
            public string artifact_sha256 = string.Empty;
            public long artifact_bytes;
            public double model_load_milliseconds;
            public long total_memory_bytes;
            public long initial_available_memory_bytes;
            public long final_available_memory_bytes;
            public long low_memory_threshold_bytes;
            public bool android_low_memory_initial;
            public bool android_low_memory_final;
            public int logical_processor_count;
            public string thermal_status_initial = string.Empty;
            public string thermal_status_final = string.Empty;
            public string admission_mode = string.Empty;
            public string admission_device_profile = string.Empty;
            public string admission_reasons = string.Empty;
            public int effective_context_tokens;
            public int effective_batch_tokens;
            public int effective_micro_batch_tokens;
            public int effective_threads;
            public int effective_batch_threads;
            public long post_load_available_memory_bytes;
            public int post_load_stabilization_observations;
            public string post_load_initial_mode = string.Empty;
            public string post_load_initial_reasons = string.Empty;
            public string post_load_stabilized_mode = string.Empty;
            public string post_load_stabilized_reasons = string.Empty;
            public string physics_fault_injection_kind = string.Empty;
            public int physics_fault_injection_count;
            public string physics_underlying_state_at_injection = string.Empty;
            public string fault_injection_governed_status = string.Empty;
            public string fault_injection_provider_status = string.Empty;
            public ulong worker_steps_before_injection;
            public ulong worker_steps_after_injection;
            public ulong worker_deadline_misses_before_injection;
            public ulong worker_deadline_misses_after_injection;
            public double worker_accumulated_lag_seconds_after_injection;
            public double worker_last_step_microseconds_after_injection;
            public double worker_max_step_microseconds_after_injection;
            public int recovery_observations;
            public string recovery_mode = string.Empty;
            public string post_recovery_governed_status = string.Empty;
            public string post_recovery_provider_status = string.Empty;
            public int post_recovery_stream_text_events;
            public ulong post_recovery_prompt_tokens;
            public ulong post_recovery_generated_tokens;
            public string final_physics_budget_state = string.Empty;
            public ulong final_worker_steps;
            public ulong final_worker_deadline_misses;
            public bool network_fallback_used;
            public bool automatic_retry_used;
            public bool physics_timestep_modified;
            public bool json_repair_used;
            public bool report_contains_prompt_or_response_content;
        }

        private sealed class ArtifactVerification
        {
            internal long bytes;
            internal string sha256 = string.Empty;
        }
    }
}
