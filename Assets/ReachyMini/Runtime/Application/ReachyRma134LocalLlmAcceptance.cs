#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Language;
using ReachyMini.LocalModels;
using ReachyMini.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma134LocalLlmAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra = "reachy_rma134_acceptance";
        public const string ResultFileName = "rma134-local-llm-provider-state.json";
        public const string InputModelFileName = "rma134-selected-model-input.gguf";
        public const string ManagedStoreDirectoryName = "rma134-managed-models";
        public const string ObjectName = "ReachyRma134LocalLlmAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsAcceptanceRequestedFromLaunchIntent() ||
                Object.FindAnyObjectByType<ReachyRma134LocalLlmAcceptance>() != null)
            {
                return;
            }
            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<ReachyRma134LocalLlmAcceptance>();
        }

        public static bool IsAcceptanceRequestedFromLaunchIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>(
                    "getBooleanExtra",
                    AcceptanceLaunchExtra,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect the RMA-134 acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyRma134LocalLlmAcceptance : MonoBehaviour
    {
        private const string ManifestResourcePath =
            "ReachyMiniRuntime/rma133_selected_local_llm_manifest_json";
        private const string SystemPromptResourcePath =
            "LocalLlm/Rma133SystemPromptV4";
        private const string GrammarResourcePath =
            "LocalLlm/Rma133BehaviorOutputV1.gbnf";
        private static readonly TimeSpan RuntimeReadyTimeout = TimeSpan.FromSeconds(45.0);
        private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(5.0);

        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma134LocalLlmAcceptanceBootstrap.ResultFileName);
            DeleteIfPresent(resultPath);
            Task<Report> task = RunAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Report report;
            if (task.IsFaulted)
            {
                Exception exception = task.Exception?.GetBaseException() ??
                    new InvalidOperationException(
                        "RMA-134 acceptance failed without an exception.");
                report = Report.Failure(exception.Message);
                Debug.LogError(
                    "RMA-134 local LLM acceptance failed: " + exception.Message,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure("RMA-134 local LLM acceptance was cancelled.");
            }
            else
            {
                report = task.Result;
            }

            WriteAtomically(resultPath, JsonUtility.ToJson(report, prettyPrint: true));
            Destroy(gameObject);
        }

        private static async Task<Report> RunAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ReachyProductionAuthoritativeRuntime simulation =
                await WaitForSimulationAsync();
            ulong simulationStepsBefore = simulation.WorkerStepCount;
            if (simulation.Status != ReachyProductionRuntimeStatus.Running ||
                simulationStepsBefore == 0UL)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation was not running before local LLM inference.");
            }

            TextAsset manifestAsset = RequireResource(ManifestResourcePath);
            TextAsset promptAsset = RequireResource(SystemPromptResourcePath);
            TextAsset grammarAsset = RequireResource(GrammarResourcePath);
            string chatTemplate = ReadExactChatTemplate(manifestAsset.text);
            LocalModelManifest manifest =
                Rma133SelectedLocalLlmProfile.CreateManifest(chatTemplate);

            string inputPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma134LocalLlmAcceptanceBootstrap.InputModelFileName);
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException(
                    "The exact selected Qwen3 acceptance artifact was not staged.",
                    inputPath);
            }

            string storeRoot = Path.Combine(
                Application.persistentDataPath,
                ReachyRma134LocalLlmAcceptanceBootstrap.ManagedStoreDirectoryName);
            Directory.CreateDirectory(storeRoot);
            LocalModelApprovedArtifact approved;
            using (var manager = new LocalModelPackageManager(
                storeRoot,
                new DriveInfoLocalModelStorageProbe()))
            await using (var source = new FileStream(
                inputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                LocalModelPackagePolicy.CopyBufferBytes,
                useAsync: true))
            {
                LocalModelPackageResult imported = await manager.ImportAsync(
                        manifest,
                        source,
                        CancellationToken.None)
                    ;
                if (!imported.Succeeded || imported.Artifact == null)
                {
                    throw new InvalidOperationException(
                        "RMA-132 package import rejected the selected model: " +
                        imported.Failure + ": " + imported.Detail);
                }
                approved = imported.Artifact;
            }
            DeleteIfPresent(inputPath);

            var runtimeFactory = new ReachyLlamaNativeRuntimeFactory();
            LocalLlmProviderConfiguration configuration =
                Rma133SelectedLocalLlmProfile.CreateProviderConfiguration(
                    promptAsset.text,
                    grammarAsset.text);
            await using var provider = new ReachyLocalLlmProvider(
                runtimeFactory,
                approved,
                manifest,
                configuration);

            var stopwatch = Stopwatch.StartNew();
            LocalLlmOperationResult load = await provider.LoadAsync(CancellationToken.None)
                ;
            if (!load.Succeeded)
            {
                throw new InvalidOperationException(
                    "Selected local LLM failed to load: " + load.Failure + ": " + load.Detail);
            }

            GenerationObservation first = await ObserveCompletionAsync(
                    provider,
                    "rma134-first",
                    "Greet me briefly and naturally.")
                ;
            GenerationObservation cancelled = await ObserveCancellationAsync(provider)
                ;
            LocalLlmOperationResult reset = await provider.ResetConversationAsync(
                    CancellationToken.None)
                ;
            if (!reset.Succeeded)
            {
                throw new InvalidOperationException(
                    "Local LLM reset failed after cancellation: " + reset.Failure + ": " + reset.Detail);
            }
            GenerationObservation reused = await ObserveCompletionAsync(
                    provider,
                    "rma134-reuse",
                    "Reply with a short friendly acknowledgement.")
                ;
            stopwatch.Stop();

            ulong simulationStepsAfter = simulation.WorkerStepCount;
            if (simulation.Status != ReachyProductionRuntimeStatus.Running)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation stopped running during local LLM acceptance: " +
                    simulation.Status + ": " + simulation.Fault);
            }
            if (simulationStepsAfter <= simulationStepsBefore)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation made no progress while local LLM inference was active.");
            }
            if (provider.Availability.State != LocalLlmProviderState.Ready)
            {
                throw new InvalidOperationException(
                    "Local LLM provider did not return to Ready after reset/reuse: " +
                    provider.Availability.State + ".");
            }

            return Report.Success(
                runtimeFactory.AbiVersion,
                approved.ManifestId,
                approved.ModelId,
                approved.FileSizeBytes,
                approved.Sha256,
                provider.Descriptor.RequiresNetwork,
                first.DeltaCount,
                first.IntentSpeech,
                cancelled.DeltaCount,
                reset.Succeeded,
                reused.DeltaCount,
                reused.IntentSpeech,
                simulationStepsBefore,
                simulationStepsAfter,
                stopwatch.Elapsed.TotalSeconds);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-134 physical acceptance requires an Android player build.");
#endif
        }

        private static async Task<ReachyProductionAuthoritativeRuntime> WaitForSimulationAsync()
        {
            DateTime deadline = DateTime.UtcNow + RuntimeReadyTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyProductionAuthoritativeRuntime? runtime =
                    Object.FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>();
                if (runtime != null &&
                    runtime.Status == ReachyProductionRuntimeStatus.Running &&
                    runtime.WorkerStepCount > 0UL)
                {
                    return runtime;
                }
                if (runtime != null && runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    throw new InvalidOperationException(
                        "Authoritative simulation faulted before RMA-134 acceptance: " +
                        runtime.Fault);
                }
                await Task.Delay(100);
            }
            throw new TimeoutException(
                "Authoritative simulation did not become ready for RMA-134 acceptance.");
        }

        private static async Task<GenerationObservation> ObserveCompletionAsync(
            ReachyLocalLlmProvider provider,
            string requestId,
            string text)
        {
            int deltas = 0;
            LocalLlmBehaviorIntent? intent = null;
            await foreach (LocalLlmEvent item in provider.GenerateAsync(
                new LocalLlmRequest(requestId, text, GenerationTimeout),
                CancellationToken.None))
            {
                if (item.Kind == LocalLlmEventKind.OutputDelta)
                {
                    ++deltas;
                    continue;
                }
                if (item.Kind != LocalLlmEventKind.Completed || item.Intent == null)
                {
                    throw new InvalidOperationException(
                        "Local LLM completion request failed: " +
                        item.Failure + ": " + item.Detail);
                }
                intent = item.Intent;
            }
            if (deltas <= 0 || intent == null)
            {
                throw new InvalidOperationException(
                    "Local LLM completion did not stream text and validated intent.");
            }
            return new GenerationObservation(deltas, intent.Speech);
        }

        private static async Task<GenerationObservation> ObserveCancellationAsync(
            ReachyLocalLlmProvider provider)
        {
            using var cancellation = new CancellationTokenSource();
            int deltas = 0;
            LocalLlmEvent? terminal = null;
            await foreach (LocalLlmEvent item in provider.GenerateAsync(
                new LocalLlmRequest(
                    "rma134-cancel",
                    "Give me a short friendly greeting.",
                    GenerationTimeout),
                cancellation.Token))
            {
                if (item.Kind == LocalLlmEventKind.OutputDelta)
                {
                    ++deltas;
                    cancellation.Cancel();
                    continue;
                }
                terminal = item;
            }
            if (deltas <= 0 || terminal == null ||
                terminal.Kind != LocalLlmEventKind.Cancelled ||
                terminal.Failure != LocalLlmFailure.Cancelled)
            {
                throw new InvalidOperationException(
                    "Local LLM cancellation did not produce a streamed delta followed by explicit cancellation.");
            }
            return new GenerationObservation(deltas, string.Empty);
        }

        private static TextAsset RequireResource(string path)
        {
            TextAsset? asset = Resources.Load<TextAsset>(path);
            if (asset == null || string.IsNullOrEmpty(asset.text))
            {
                throw new InvalidOperationException(
                    "Required RMA-134 Unity resource is missing: " + path);
            }
            return asset;
        }

        private static string ReadExactChatTemplate(string manifestJson)
        {
            ManifestProjection? projection = JsonUtility.FromJson<ManifestProjection>(manifestJson);
            string? chatTemplate = projection?.inference?.chat_template;
            if (string.IsNullOrWhiteSpace(chatTemplate))
            {
                throw new InvalidOperationException(
                    "The selected RMA-133 manifest resource does not contain its exact chat template.");
            }
            return chatTemplate;
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                throw new IOException("Could not remove RMA-134 acceptance file: " + path, exception);
            }
        }

        private static void WriteAtomically(string path, string text)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        [Serializable]
        private sealed class ManifestProjection
        {
            public InferenceProjection? inference;
        }

        [Serializable]
        private sealed class InferenceProjection
        {
            public string? chat_template;
        }

        private sealed class GenerationObservation
        {
            public GenerationObservation(int deltaCount, string intentSpeech)
            {
                DeltaCount = deltaCount;
                IntentSpeech = intentSpeech;
            }

            public int DeltaCount { get; }

            public string IntentSpeech { get; }
        }

        [Serializable]
        private sealed class Report
        {
            public string status = string.Empty;
            public string detail = string.Empty;
            public bool acceptance_enabled;
            public uint runtime_abi_version;
            public string manifest_id = string.Empty;
            public string model_id = string.Empty;
            public long model_file_size_bytes;
            public string model_sha256 = string.Empty;
            public bool provider_requires_network;
            public bool constrained_only;
            public int first_delta_count;
            public string first_speech = string.Empty;
            public int cancellation_delta_count;
            public bool cancellation_observed;
            public bool reset_succeeded;
            public int reuse_delta_count;
            public string reuse_speech = string.Empty;
            public bool reuse_completed;
            public ulong simulation_steps_before;
            public ulong simulation_steps_after;
            public ulong simulation_step_delta;
            public bool simulation_remained_running;
            public double acceptance_elapsed_seconds;

            public static Report Success(
                uint runtimeAbiVersion,
                string manifestId,
                string modelId,
                long modelFileSizeBytes,
                string modelSha256,
                bool providerRequiresNetwork,
                int firstDeltaCount,
                string firstSpeech,
                int cancellationDeltaCount,
                bool resetSucceeded,
                int reuseDeltaCount,
                string reuseSpeech,
                ulong simulationStepsBefore,
                ulong simulationStepsAfter,
                double elapsedSeconds)
            {
                return new Report
                {
                    status = "passed",
                    detail = "RMA-134 physical local LLM provider acceptance passed.",
                    acceptance_enabled = true,
                    runtime_abi_version = runtimeAbiVersion,
                    manifest_id = manifestId,
                    model_id = modelId,
                    model_file_size_bytes = modelFileSizeBytes,
                    model_sha256 = modelSha256,
                    provider_requires_network = providerRequiresNetwork,
                    constrained_only = true,
                    first_delta_count = firstDeltaCount,
                    first_speech = firstSpeech,
                    cancellation_delta_count = cancellationDeltaCount,
                    cancellation_observed = true,
                    reset_succeeded = resetSucceeded,
                    reuse_delta_count = reuseDeltaCount,
                    reuse_speech = reuseSpeech,
                    reuse_completed = true,
                    simulation_steps_before = simulationStepsBefore,
                    simulation_steps_after = simulationStepsAfter,
                    simulation_step_delta = simulationStepsAfter - simulationStepsBefore,
                    simulation_remained_running = true,
                    acceptance_elapsed_seconds = elapsedSeconds,
                };
            }

            public static Report Failure(string detail)
            {
                return new Report
                {
                    status = "failed",
                    detail = string.IsNullOrWhiteSpace(detail)
                        ? "RMA-134 physical local LLM provider acceptance failed without diagnostics."
                        : detail,
                    acceptance_enabled = true,
                    provider_requires_network = false,
                    constrained_only = true,
                };
            }
        }
    }
}
