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
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed class ReachyRma134LocalLlmAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName = "reachy_rma134_acceptance";
        internal const string ResultFileName = "rma134-local-llm-acceptance.json";
        internal const string ModelFileName = "rma134-qwen3-0.6b-q4_k_m.gguf";

        private const string SimulationModelResourcePath = "ReachyMiniRuntime/reachy_mini_mjb";
        private const double PhysicsStepSeconds = 0.002;
        private const double PhysicsP95BudgetMicroseconds = 2000.0;
        private const int MinimumConcurrentPhysicsSteps = 250;
        private const int MaximumConcurrentPhysicsSteps = 10000;
        private const string SuccessPrompt =
            "The person says hello and asks you to greet them warmly. No gaze target is requested.";

        private static string bootstrapError = string.Empty;
        private static bool unhandledFailure;
        private static string unhandledFailureMessage = string.Empty;

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
                bootstrapError = "Failed to read the RMA-134 launch extra: " + exception.Message;
            }
            if (!requested)
            {
                return;
            }
            GameObject host = new GameObject("ReachyRma134LocalLlmAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma134LocalLlmAcceptance>();
        }

        private async void Start()
        {
            UnityEngine.Application.logMessageReceivedThreaded += HandleLogMessage;
            string resultPath = Path.Combine(UnityEngine.Application.persistentDataPath, ResultFileName);
            try
            {
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                if (!string.IsNullOrEmpty(bootstrapError))
                {
                    throw new InvalidOperationException(bootstrapError);
                }
                Rma134AcceptanceReport report = await RunAcceptanceAsync().ConfigureAwait(true);
                if (unhandledFailure)
                {
                    throw new InvalidOperationException(
                        "An unhandled Unity exception occurred during RMA-134 acceptance: " + unhandledFailureMessage);
                }
                report.status = "passed";
                WriteReport(resultPath, report);
                Debug.Log("RMA-134 local LLM physical acceptance passed.");
            }
            catch (Exception exception)
            {
                Rma134AcceptanceReport failure = new Rma134AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                };
                try
                {
                    WriteReport(resultPath, failure);
                }
                catch (Exception reportException)
                {
                    Debug.LogError("RMA-134 could not write its failure report: " + reportException);
                }
                Debug.LogError("RMA-134 local LLM physical acceptance failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceivedThreaded -= HandleLogMessage;
            }
        }

        private static async Task<Rma134AcceptanceReport> RunAcceptanceAsync()
        {
            string modelPath = Path.Combine(UnityEngine.Application.persistentDataPath, ModelFileName);
            ArtifactVerification artifact = await Task.Run(() => VerifyArtifact(modelPath)).ConfigureAwait(true);
            LocalModelManifest manifest = CreateSelectedManifest();
            LocalModelApprovedArtifact approvedArtifact = new LocalModelApprovedArtifact(
                LocalLlmBehaviorContract.ManifestId,
                LocalLlmBehaviorContract.ModelId,
                modelPath,
                LocalLlmBehaviorContract.ArtifactBytes,
                LocalLlmBehaviorContract.ArtifactSha256);
            LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();
            double initialBatteryTemperature = ReadBatteryTemperatureCelsius();
            long initialRssBytes = ReadSelfRssBytes();
            uint nativeAbi = NativeReachyLlama.AbiVersion();
            if (nativeAbi != 2U)
            {
                throw new InvalidOperationException(
                    "Physical acceptance loaded reachy_llama ABI " + nativeAbi + " instead of ABI 2.");
            }

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

            LocalLlmProvider provider = creation.Provider;
            ReachySimSession? simulationSession = null;
            try
            {
                ReachySimSession activeSimulationSession = CreateSimulationSession();
                simulationSession = activeSimulationSession;
                CollectingSink firstSink = new CollectingSink();
                using CancellationTokenSource physicsCancellation = new CancellationTokenSource();
                Task<PhysicsTimingReport> physicsTask = Task.Run(
                    () => RunPhysicsLoop(activeSimulationSession, physicsCancellation.Token));
                LocalLlmGenerationResult first = await provider.GenerateAsync(
                    CreateRequest("rma134-success-1", SuccessPrompt),
                    firstSink,
                    CancellationToken.None).ConfigureAwait(true);
                physicsCancellation.Cancel();
                PhysicsTimingReport physics = await physicsTask.ConfigureAwait(true);
                ValidateSuccessfulGeneration(first, firstSink, "initial generation");
                ValidatePhysicsTiming(physics);

                using CancellationTokenSource generationCancellation = new CancellationTokenSource();
                CancelOnFirstTextSink cancellationSink = new CancelOnFirstTextSink(generationCancellation);
                LocalLlmGenerationResult cancelled = await provider.GenerateAsync(
                    CreateRequest("rma134-cancel", "Please give a friendly short acknowledgment."),
                    cancellationSink,
                    generationCancellation.Token).ConfigureAwait(true);
                if (cancelled.Status != LocalLlmGenerationStatus.Cancelled)
                {
                    throw new InvalidOperationException(
                        "Managed cancellation did not terminate as Cancelled: " + cancelled.Status);
                }

                ulong previousEpoch = provider.ConversationEpoch;
                CollectingSink resetSink = new CollectingSink();
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

                LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(true);
                if (!reload.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Explicit in-process local LLM reload failed: status=" + reload.Status +
                        " native=" + reload.NativeStatus + " detail=" + reload.Detail);
                }

                CollectingSink secondSink = new CollectingSink();
                LocalLlmGenerationResult second = await provider.GenerateAsync(
                    CreateRequest("rma134-success-2", SuccessPrompt),
                    secondSink,
                    CancellationToken.None).ConfigureAwait(true);
                ValidateSuccessfulGeneration(second, secondSink, "post-reload generation");

                double finalBatteryTemperature = ReadBatteryTemperatureCelsius();
                long finalRssBytes = ReadSelfRssBytes();
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
                simulationSession?.Dispose();
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static void ValidateSuccessfulGeneration(LocalLlmGenerationResult result, CollectingSink sink, string label)
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

        private static void ValidatePhysicsTiming(PhysicsTimingReport physics)
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

        private static PhysicsTimingReport RunPhysicsLoop(ReachySimSession session, CancellationToken cancellationToken)
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
            return new PhysicsTimingReport
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

        private static ArtifactVerification VerifyArtifact(string modelPath)
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

        private static double ReadBatteryTemperatureCelsius()
        {
            using AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent");
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            string action = intentClass.GetStatic<string>("ACTION_BATTERY_CHANGED");
            using AndroidJavaObject filter = new AndroidJavaObject("android.content.IntentFilter", action);
            AndroidJavaObject? battery = activity.Call<AndroidJavaObject>("registerReceiver", (object?)null, filter);
            if (battery == null)
            {
                throw new InvalidOperationException(
                    "Android did not return ACTION_BATTERY_CHANGED data for RMA-134 acceptance.");
            }
            using (battery)
            {
                int temperatureTenths = battery.Call<int>("getIntExtra", "temperature", int.MinValue);
                if (temperatureTenths == int.MinValue)
                {
                    throw new InvalidOperationException(
                        "Android battery temperature is unavailable for RMA-134 acceptance.");
                }
                return temperatureTenths / 10.0;
            }
        }

        private static long ReadSelfRssBytes()
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long kibibytes))
                {
                    return checked(kibibytes * 1024L);
                }
                throw new InvalidOperationException("RMA-134 could not parse VmRSS from /proc/self/status.");
            }
            throw new InvalidOperationException("RMA-134 could not find VmRSS in /proc/self/status.");
        }

        private static bool ReadBooleanLaunchExtra(string name, bool defaultValue)
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, defaultValue);
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

        private static void WriteReport(string path, Rma134AcceptanceReport report)
        {
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters ? value : value.Substring(0, maximumCharacters);
        }

        private sealed class CollectingSink : ILocalLlmStreamSink
        {
            public int TextEventCount { get; private set; }
            public int TextUtf8Bytes { get; private set; }
            public bool TerminalValidated { get; private set; }
            public bool SawTrustedPartialOutput { get; private set; }

            public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (streamEvent.Type == LocalLlmStreamEventType.Text)
                {
                    ++TextEventCount;
                    TextUtf8Bytes = checked(TextUtf8Bytes + Encoding.UTF8.GetByteCount(streamEvent.Text));
                    SawTrustedPartialOutput |= streamEvent.IsTrustedExecutableOutput;
                }
                else if (streamEvent.Type == LocalLlmStreamEventType.Completed)
                {
                    TerminalValidated = true;
                }
                return default;
            }
        }

        private sealed class CancelOnFirstTextSink : ILocalLlmStreamSink
        {
            private readonly CancellationTokenSource cancellation;
            internal CancelOnFirstTextSink(CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation;
            }
            public int TextEventCount { get; private set; }
            public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
            {
                if (streamEvent.Type == LocalLlmStreamEventType.Text)
                {
                    ++TextEventCount;
                    cancellation.Cancel();
                }
                return default;
            }
        }

        [Serializable]
        private sealed class Rma134AcceptanceReport
        {
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public string operating_system = string.Empty;
            public int reachy_llama_abi;
            public string manifest_id = string.Empty;
            public string model_id = string.Empty;
            public string artifact_sha256 = string.Empty;
            public long artifact_bytes;
            public double load_milliseconds;
            public string initial_generation_status = string.Empty;
            public int initial_stream_text_events;
            public int initial_stream_utf8_bytes;
            public string initial_prompt_tokens = string.Empty;
            public string initial_generated_tokens = string.Empty;
            public string initial_speech = string.Empty;
            public string cancellation_status = string.Empty;
            public int cancellation_text_events_before_cancel;
            public string reset_status = string.Empty;
            public string reset_epoch_before = string.Empty;
            public string reset_epoch_after = string.Empty;
            public string reload_status = string.Empty;
            public string post_reload_generation_status = string.Empty;
            public string post_reload_speech = string.Empty;
            public int physics_steps;
            public double physics_p50_microseconds;
            public double physics_p95_microseconds;
            public double physics_max_microseconds;
            public int physics_deadline_misses;
            public double physics_p95_budget_microseconds;
            public long initial_rss_bytes;
            public long final_rss_bytes;
            public double initial_battery_temperature_c;
            public double final_battery_temperature_c;
            public bool adaptive_resource_changes_applied;
            public bool network_fallback_used;
            public bool json_repair_used;
        }

        private sealed class ArtifactVerification
        {
            internal long bytes;
            internal string sha256 = string.Empty;
        }

        private sealed class PhysicsTimingReport
        {
            internal int steps;
            internal double p50_microseconds;
            internal double p95_microseconds;
            internal double max_microseconds;
            internal int deadline_misses;
        }
    }
}
