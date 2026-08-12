#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.LocalModels;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma134LocalLlmAcceptance
    {
        private static async Task<Rma134AcceptanceReport> RunAcceptanceAsync()
        {
            string modelPath = Path.Combine(UnityEngine.Application.persistentDataPath, ModelFileName);
            WriteCheckpoint("artifact_verification_started", "Verifying exact staged GGUF artifact.");
            Rma134ArtifactVerification artifact = await Task.Run(() => VerifyArtifact(modelPath)).ConfigureAwait(true);
            WriteCheckpoint(
                "artifact_verified",
                "bytes=" + artifact.bytes.ToString(CultureInfo.InvariantCulture) + " sha256=" + artifact.sha256);

            LocalModelManifest manifest = CreateSelectedManifest();
            LocalModelApprovedArtifact approvedArtifact = new LocalModelApprovedArtifact(
                LocalLlmBehaviorContract.ManifestId,
                LocalLlmBehaviorContract.ModelId,
                modelPath,
                LocalLlmBehaviorContract.ArtifactBytes,
                LocalLlmBehaviorContract.ArtifactSha256);
            LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();

            WriteCheckpoint("baseline_observation_started", "Reading battery temperature and process RSS.");
            double initialBatteryTemperature = ReadBatteryTemperatureCelsius();
            long initialRssBytes = ReadSelfRssBytes();
            WriteCheckpoint(
                "baseline_observation_completed",
                "rss_bytes=" + initialRssBytes.ToString(CultureInfo.InvariantCulture) +
                " battery_c=" + initialBatteryTemperature.ToString("F1", CultureInfo.InvariantCulture));

            WriteCheckpoint("abi_check_started", "Querying reachy_llama ABI version.");
            uint nativeAbi = NativeReachyLlama.AbiVersion();
            if (nativeAbi != 2U)
            {
                throw new InvalidOperationException(
                    "Physical acceptance loaded reachy_llama ABI " + nativeAbi + " instead of ABI 2.");
            }
            WriteCheckpoint("abi_verified", "reachy_llama ABI=2.");

            WriteCheckpoint("model_load_started", "Creating managed provider with frozen RMA-133 V6 profile.");
            Stopwatch loadStopwatch = Stopwatch.StartNew();
            LocalLlmProviderCreationResult creation = await LocalLlmProvider.CreateAsync(
                manifest,
                approvedArtifact,
                profile,
                CancellationToken.None).ConfigureAwait(true);
            loadStopwatch.Stop();
            if (!creation.Succeeded || creation.Provider == null)
            {
                throw new InvalidOperationException(
                    "Managed local LLM provider creation failed: status=" + creation.Status +
                    " native=" + creation.NativeStatus + " detail=" + creation.Detail);
            }
            WriteCheckpoint(
                "model_loaded",
                "load_ms=" + loadStopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));

            LocalLlmProvider provider = creation.Provider;
            ReachySimSession? simulationSession = null;
            try
            {
                WriteCheckpoint("physics_session_started", "Creating native physics coexistence session.");
                ReachySimSession activeSimulationSession = CreateSimulationSession();
                simulationSession = activeSimulationSession;
                WriteCheckpoint("physics_session_ready", "Native physics coexistence session created.");

                Rma134CollectingSink firstSink = new Rma134CollectingSink();
                using CancellationTokenSource physicsCancellation = new CancellationTokenSource();
                Task<Rma134PhysicsTimingReport> physicsTask = Task.Run(
                    () => RunPhysicsLoop(activeSimulationSession, physicsCancellation.Token));
                WriteCheckpoint("initial_generation_started", "Starting constrained initial behavior generation.");
                LocalLlmGenerationResult first = await provider.GenerateAsync(
                    CreateRequest("rma134-success-1", SuccessPrompt),
                    firstSink,
                    CancellationToken.None).ConfigureAwait(true);
                WriteCheckpoint(
                    "initial_generation_completed",
                    "status=" + first.Status + " text_events=" +
                    firstSink.TextEventCount.ToString(CultureInfo.InvariantCulture));

                physicsCancellation.Cancel();
                Rma134PhysicsTimingReport physics = await physicsTask.ConfigureAwait(true);
                WriteCheckpoint(
                    "physics_completed",
                    "steps=" + physics.steps.ToString(CultureInfo.InvariantCulture) +
                    " p95_us=" + physics.p95_microseconds.ToString("F3", CultureInfo.InvariantCulture));
                ValidateSuccessfulGeneration(first, firstSink, "initial generation");
                ValidatePhysicsTiming(physics);
                WriteCheckpoint("initial_generation_validated", "Initial intent and physics gates passed.");

                WriteCheckpoint("cancellation_started", "Starting cancellation acceptance generation.");
                using CancellationTokenSource generationCancellation = new CancellationTokenSource();
                Rma134CancelOnFirstTextSink cancellationSink = new Rma134CancelOnFirstTextSink(generationCancellation);
                LocalLlmGenerationResult cancelled = await provider.GenerateAsync(
                    CreateRequest("rma134-cancel", "Please give a friendly short acknowledgment."),
                    cancellationSink,
                    generationCancellation.Token).ConfigureAwait(true);
                if (cancelled.Status != LocalLlmGenerationStatus.Cancelled)
                {
                    throw new InvalidOperationException(
                        "Managed cancellation did not terminate as Cancelled: " + cancelled.Status);
                }
                WriteCheckpoint(
                    "cancellation_completed",
                    "status=" + cancelled.Status + " text_events=" +
                    cancellationSink.TextEventCount.ToString(CultureInfo.InvariantCulture));

                WriteCheckpoint("reset_started", "Starting conversation reset supersession acceptance.");
                ulong previousEpoch = provider.ConversationEpoch;
                Rma134CollectingSink resetSink = new Rma134CollectingSink();
                Task<LocalLlmGenerationResult> resetTask = provider.GenerateAsync(
                    CreateRequest("rma134-reset", "Please acknowledge this message briefly."),
                    resetSink,
                    CancellationToken.None);
                ulong newEpoch = provider.ResetConversation();
                LocalLlmGenerationResult resetResult = await resetTask.ConfigureAwait(true);
                if (newEpoch == previousEpoch || resetResult.Status != LocalLlmGenerationStatus.Superseded)
                {
                    throw new InvalidOperationException(
                        "Conversation reset did not rotate the epoch and supersede the active generation: " +
                        resetResult.Status);
                }
                if (resetSink.TerminalValidated)
                {
                    throw new InvalidOperationException(
                        "A superseded pre-reset generation emitted a validated-success terminal event.");
                }
                WriteCheckpoint(
                    "reset_completed",
                    "status=" + resetResult.Status + " epoch_before=" +
                    previousEpoch.ToString(CultureInfo.InvariantCulture) + " epoch_after=" +
                    newEpoch.ToString(CultureInfo.InvariantCulture));

                WriteCheckpoint("reload_started", "Starting explicit in-process provider reload.");
                LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(true);
                if (!reload.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Explicit in-process local LLM reload failed: status=" + reload.Status +
                        " native=" + reload.NativeStatus + " detail=" + reload.Detail);
                }
                WriteCheckpoint("reload_completed", "status=" + reload.Status);

                Rma134CollectingSink secondSink = new Rma134CollectingSink();
                WriteCheckpoint("post_reload_generation_started", "Starting constrained post-reload generation.");
                LocalLlmGenerationResult second = await provider.GenerateAsync(
                    CreateRequest("rma134-success-2", SuccessPrompt),
                    secondSink,
                    CancellationToken.None).ConfigureAwait(true);
                WriteCheckpoint(
                    "post_reload_generation_completed",
                    "status=" + second.Status + " text_events=" +
                    secondSink.TextEventCount.ToString(CultureInfo.InvariantCulture));
                ValidateSuccessfulGeneration(second, secondSink, "post-reload generation");
                WriteCheckpoint("post_reload_generation_validated", "Post-reload intent gate passed.");

                double finalBatteryTemperature = ReadBatteryTemperatureCelsius();
                long finalRssBytes = ReadSelfRssBytes();
                WriteCheckpoint(
                    "final_observation_completed",
                    "rss_bytes=" + finalRssBytes.ToString(CultureInfo.InvariantCulture) +
                    " battery_c=" + finalBatteryTemperature.ToString("F1", CultureInfo.InvariantCulture));
                WriteCheckpoint("acceptance_body_completed", "All behavioral acceptance operations completed.");
                return new Rma134AcceptanceReport
                {
                    status = "running",
                    error = string.Empty,
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                    reachy_llama_abi = checked((int)nativeAbi),
                    manifest_id = LocalLlmBehaviorContract.ManifestId,
                    model_id = LocalLlmBehaviorContract.ModelId,
                    artifact_sha256 = artifact.sha256,
                    artifact_bytes = artifact.bytes,
                    load_milliseconds = loadStopwatch.Elapsed.TotalMilliseconds,
                    initial_generation_status = first.Status.ToString(),
                    initial_stream_text_events = firstSink.TextEventCount,
                    initial_stream_utf8_bytes = firstSink.TextUtf8Bytes,
                    initial_prompt_tokens = MetricsValue(first.Metrics, true).ToString(CultureInfo.InvariantCulture),
                    initial_generated_tokens = MetricsValue(first.Metrics, false).ToString(CultureInfo.InvariantCulture),
                    initial_speech = first.Intent?.Speech ?? string.Empty,
                    cancellation_status = cancelled.Status.ToString(),
                    cancellation_text_events_before_cancel = cancellationSink.TextEventCount,
                    reset_status = resetResult.Status.ToString(),
                    reset_epoch_before = previousEpoch.ToString(CultureInfo.InvariantCulture),
                    reset_epoch_after = newEpoch.ToString(CultureInfo.InvariantCulture),
                    reload_status = reload.Status.ToString(),
                    post_reload_generation_status = second.Status.ToString(),
                    post_reload_speech = second.Intent?.Speech ?? string.Empty,
                    physics_steps = physics.steps,
                    physics_p50_microseconds = physics.p50_microseconds,
                    physics_p95_microseconds = physics.p95_microseconds,
                    physics_max_microseconds = physics.max_microseconds,
                    physics_deadline_misses = physics.deadline_misses,
                    physics_p95_budget_microseconds = PhysicsP95BudgetMicroseconds,
                    initial_rss_bytes = initialRssBytes,
                    final_rss_bytes = finalRssBytes,
                    initial_battery_temperature_c = initialBatteryTemperature,
                    final_battery_temperature_c = finalBatteryTemperature,
                    adaptive_resource_changes_applied = false,
                    network_fallback_used = false,
                    json_repair_used = false,
                };
            }
            finally
            {
                TryWriteCheckpoint("cleanup_started", "Disposing physical acceptance resources.");
                simulationSession?.Dispose();
                TryWriteCheckpoint("simulation_disposed", "Physics session disposed.");
                TryWriteCheckpoint("provider_dispose_started", "Disposing managed local LLM provider.");
                await provider.DisposeAsync().ConfigureAwait(true);
                TryWriteCheckpoint("provider_disposed", "Managed local LLM provider disposed.");
            }
        }

        private static void ValidateSuccessfulGeneration(
            LocalLlmGenerationResult result, Rma134CollectingSink sink, string label)
        {
            if (!result.Succeeded || result.Intent == null)
            {
                throw new InvalidOperationException(
                    label + " did not produce a validated intent: status=" + result.Status +
                    " native=" + result.NativeStatus + " detail=" + result.Detail);
            }
            if (!sink.TerminalValidated || sink.TextEventCount <= 0)
            {
                throw new InvalidOperationException(
                    label + " did not demonstrate ordered streaming followed by validated completion.");
            }
            if (sink.SawTrustedPartialOutput)
            {
                throw new InvalidOperationException(label + " marked partial generated text as trusted/executable.");
            }
        }

        private static void ValidatePhysicsTiming(Rma134PhysicsTimingReport physics)
        {
            if (physics.steps < MinimumConcurrentPhysicsSteps)
            {
                throw new InvalidOperationException(
                    "Concurrent physics probe completed only " + physics.steps +
                    " steps; at least " + MinimumConcurrentPhysicsSteps + " are required.");
            }
            if (physics.p95_microseconds > PhysicsP95BudgetMicroseconds)
            {
                throw new InvalidOperationException(
                    "Concurrent physics p95 " + physics.p95_microseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    " us exceeded the 2 ms/500 Hz native-step budget.");
            }
        }

        private static ReachySimSession CreateSimulationSession()
        {
            TextAsset? modelAsset = Resources.Load<TextAsset>(SimulationModelResourcePath);
            if (modelAsset == null || modelAsset.bytes.Length == 0)
            {
                throw new InvalidOperationException(
                    "The staged production MJB is unavailable for the RMA-134 physics coexistence probe.");
            }
            ReachySimCreateResult create = ReachySimSession.Create(modelAsset.bytes);
            if (!create.IsSuccess || create.Session == null)
            {
                throw new InvalidOperationException(
                    "RMA-134 could not create the native physics coexistence session: " +
                    create.Error.Code + ": " + create.Error.Message);
            }
            return create.Session;
        }

        private static Rma134PhysicsTimingReport RunPhysicsLoop(
            ReachySimSession session, CancellationToken cancellationToken)
        {
            List<double> durations = new List<double>(2048);
            long frequency = Stopwatch.Frequency;
            long periodTicks = Math.Max(1L, (long)Math.Round(PhysicsStepSeconds * frequency));
            long nextDeadline = Stopwatch.GetTimestamp();
            int deadlineMisses = 0;
            while (!cancellationToken.IsCancellationRequested && durations.Count < MaximumConcurrentPhysicsSteps)
            {
                nextDeadline += periodTicks;
                long before = Stopwatch.GetTimestamp();
                ReachySimOperationResult step = session.Step(1U);
                long after = Stopwatch.GetTimestamp();
                if (!step.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "Concurrent physics step failed: " + step.Error.Code + ": " + step.Error.Message);
                }
                durations.Add((after - before) * 1000000.0 / frequency);
                if (after > nextDeadline)
                {
                    ++deadlineMisses;
                    nextDeadline = after;
                }
                WaitUntil(nextDeadline, cancellationToken);
            }
            if (durations.Count == 0)
            {
                throw new InvalidOperationException("Concurrent physics probe produced no timing samples.");
            }
            durations.Sort();
            return new Rma134PhysicsTimingReport
            {
                steps = durations.Count,
                p50_microseconds = Percentile(durations, 0.50),
                p95_microseconds = Percentile(durations, 0.95),
                max_microseconds = durations[durations.Count - 1],
                deadline_misses = deadlineMisses,
            };
        }

        private static void WaitUntil(long deadlineTicks, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                long remaining = deadlineTicks - Stopwatch.GetTimestamp();
                if (remaining <= 0)
                {
                    return;
                }
                double remainingMilliseconds = remaining * 1000.0 / Stopwatch.Frequency;
                if (remainingMilliseconds > 1.5)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(64);
                }
            }
        }

        private static double Percentile(List<double> sorted, double percentile)
        {
            int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(sorted.Count - 1, index));
            return sorted[index];
        }

        private static Rma134ArtifactVerification VerifyArtifact(string modelPath)
        {
            FileInfo file = new FileInfo(modelPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    "The exact RMA-133 selected model was not staged for RMA-134 acceptance.", modelPath);
            }
            if (file.Length != LocalLlmBehaviorContract.ArtifactBytes)
            {
                throw new InvalidOperationException(
                    "RMA-134 staged model size mismatch: expected " + LocalLlmBehaviorContract.ArtifactBytes +
                    ", received " + file.Length + ".");
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
            if (!string.Equals(hash, LocalLlmBehaviorContract.ArtifactSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("RMA-134 staged model SHA-256 mismatch: " + hash + ".");
            }
            return new Rma134ArtifactVerification { bytes = file.Length, sha256 = hash };
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
                new LocalModelGgufMetadata(3, "qwen3", "Q4_K_M", 596049920L, "gpt2", "qwen2"),
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

        private static ulong MetricsValue(LocalLlmGenerationMetrics? metrics, bool prompt)
        {
            if (metrics == null)
            {
                return 0UL;
            }
            return prompt ? metrics.PromptTokens : metrics.GeneratedTokens;
        }
    }
}
