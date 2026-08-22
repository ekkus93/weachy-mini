#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Providers;
using ReachyMini.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    // Exploratory, not a permanent CI gate: measures whether offloading LLM
    // inference to a local-network endpoint (a real Ollama server reached via
    // `adb reverse`, standing in for a genuine cloud provider) -- instead of
    // running it on-device -- changes this phone's thermal profile relative
    // to RMA-135's documented on-device-LLM baseline
    // (docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md).
    // Activated via a launch-intent boolean extra, mirroring the RMA-134/135
    // acceptance harness pattern. Reuses the already-running production
    // ReachyProductionAuthoritativeRuntime (never a second simulation worker,
    // per RMA-135's own invariant) and the already-built
    // ReachyCloudLlmCredentialCoordinator / ReachyLocalLlmProviderApplicationService
    // cloud LLM path verbatim -- no new provider/transport code here, this
    // only drives the existing ones under sustained load. Thermal readings
    // themselves are captured externally via `adb shell dumpsys thermalservice`
    // by the accompanying shell script, correlated against this component's
    // checkpoint timestamps.
    internal sealed class ReachyRma195CloudLlmThermalProbe : MonoBehaviour
    {
        internal const string LaunchExtraName = "reachy_rma195_cloud_llm_thermal_probe";
        internal const string ResultFileName = "rma195-cloud-llm-thermal-probe.json";
        internal const string CheckpointFilePrefix = "rma195-cloud-llm-thermal-checkpoint-";

        private const string CloudLlmBaseUrl = "http://127.0.0.1:11434";
        private const string CloudLlmModelId = "llama3.2:3b";
        private const string DummyApiKey = "not-needed-ollama-ignores-auth";
        private const int GenerationDurationSeconds = 45;
        private const string Prompt = "Nod to greet the person.";

        private static int checkpointSequence;
        private static Stopwatch? checkpointStopwatch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android)
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
                Debug.LogError("RMA-195 thermal probe: failed to read launch extra: " + exception.Message);
                return;
            }
            if (!requested)
            {
                return;
            }

            InitializeCheckpointRun();
            TryWriteCheckpoint("bootstrap_started", "Launch extra accepted.");
            GameObject host = new GameObject("ReachyRma195CloudLlmThermalProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma195CloudLlmThermalProbe>();
            TryWriteCheckpoint("bootstrap_component_installed", "Probe MonoBehaviour installed.");
        }

        private async void Start()
        {
            string resultPath = Path.Combine(Application.persistentDataPath, ResultFileName);
            try
            {
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                TryWriteCheckpoint("start_entered", "Probe Start entered.");

                ReachyProductionAuthoritativeRuntime runtime = await WaitForProductionRuntimeAsync();
                TryWriteCheckpoint(
                    "production_runtime_found",
                    "status=" + runtime.Status);

                ReachyCloudLlmCredentialCoordinator coordinator = new ReachyCloudLlmCredentialCoordinator();
                coordinator.Initialize();
                string profileResult = coordinator.SaveProfile(
                    CloudLlmBaseUrl,
                    ReachyProviderEndpointStyle.ChatCompletions,
                    CloudLlmModelId);
                TryWriteCheckpoint("cloud_llm_profile_saved", profileResult);
                if (coordinator.CurrentProfile == null)
                {
                    WriteFailureReport(resultPath, "profile_save_failed: " + profileResult);
                    return;
                }

                string apiKeyResult = coordinator.SaveApiKey(DummyApiKey);
                TryWriteCheckpoint("cloud_llm_api_key_saved", apiKeyResult);
                if (!coordinator.HasApiKey)
                {
                    WriteFailureReport(resultPath, "api_key_save_failed: " + apiKeyResult);
                    return;
                }

                string authorizeResult = coordinator.GrantAuthorization();
                TryWriteCheckpoint("cloud_llm_authorized", authorizeResult);
                if (!coordinator.IsAuthorized)
                {
                    WriteFailureReport(resultPath, "authorization_failed: " + authorizeResult);
                    return;
                }

                ReachySettingsStateStore settings = new ReachySettingsStateStore();
                ReachyLocalLlmProviderApplicationService service =
                    new ReachyLocalLlmProviderApplicationService(settings, runtime);
                try
                {
                    service.Initialize();
                    TryWriteCheckpoint("provider_service_initialized", "Health=" + service.Health.State);

                    int attempts = 0;
                    int succeeded = 0;
                    int failed = 0;
                    string lastStatus = string.Empty;
                    string lastDetail = string.Empty;
                    Stopwatch generationWindow = Stopwatch.StartNew();
                    while (generationWindow.Elapsed.TotalSeconds < GenerationDurationSeconds)
                    {
                        ++attempts;
                        ReachyLlmGenerationRequest request = new ReachyLlmGenerationRequest(
                            "thermal-probe-" + attempts.ToString(CultureInfo.InvariantCulture),
                            new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, Prompt) });
                        ReachyLlmGenerationResult result = await service.GenerateAsync(
                            request,
                            CancellationToken.None);
                        lastStatus = result.Status.ToString();
                        lastDetail = result.Detail;
                        if (result.Status == ReachyLlmGenerationStatus.BehaviorIntentValid ||
                            result.Status == ReachyLlmGenerationStatus.BehaviorIntentInvalid)
                        {
                            // Either counts as "the cloud round trip worked": a real
                            // HTTP request reached a real server and returned a real
                            // response. Whether the small model's text happened to
                            // satisfy RMA-151's strict schema is a separate, unrelated
                            // question from whether this attempt exercised the phone's
                            // network+physics thermal load, which is all this probe
                            // measures.
                            ++succeeded;
                        }
                        else
                        {
                            ++failed;
                        }
                        TryWriteCheckpoint(
                            "generation_attempt_completed",
                            "attempt=" + attempts + " status=" + lastStatus);
                    }
                    generationWindow.Stop();

                    Rma195ThermalProbeReport report = new Rma195ThermalProbeReport
                    {
                        schema_version = 1,
                        status = "completed",
                        device_model = SystemInfo.deviceModel,
                        cloud_llm_base_url = CloudLlmBaseUrl,
                        cloud_llm_model_id = CloudLlmModelId,
                        generation_window_seconds = generationWindow.Elapsed.TotalSeconds,
                        generation_attempts = attempts,
                        generation_succeeded_schema_valid = succeeded,
                        generation_failed_or_invalid = failed,
                        last_status = lastStatus,
                        last_detail = Bound(lastDetail, 1024),
                    };
                    WriteReport(resultPath, report);
                    TryWriteCheckpoint(
                        "probe_completed",
                        "attempts=" + attempts + " succeeded=" + succeeded + " failed=" + failed);
                }
                finally
                {
                    service.Dispose();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("RMA-195 thermal probe failed: " + exception);
                WriteFailureReport(resultPath, "unhandled_exception: " + exception.Message);
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
                    return runtime;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(200.0)).ConfigureAwait(true);
            }
            throw new InvalidOperationException(
                "RMA-195 thermal probe: production authoritative runtime never appeared.");
        }

        private static void WriteFailureReport(string resultPath, string detail)
        {
            Rma195ThermalProbeReport report = new Rma195ThermalProbeReport
            {
                schema_version = 1,
                status = "failed",
                device_model = SystemInfo.deviceModel,
                cloud_llm_base_url = CloudLlmBaseUrl,
                cloud_llm_model_id = CloudLlmModelId,
                generation_window_seconds = 0.0,
                generation_attempts = 0,
                generation_succeeded_schema_valid = 0,
                generation_failed_or_invalid = 0,
                last_status = "failed",
                last_detail = Bound(detail, 1024),
            };
            WriteReport(resultPath, report);
            TryWriteCheckpoint("probe_failed", Bound(detail, 512));
        }

        private static bool ReadBooleanLaunchExtra(string name, bool defaultValue)
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, defaultValue);
        }

        private static void InitializeCheckpointRun()
        {
            checkpointSequence = 0;
            checkpointStopwatch = Stopwatch.StartNew();
            string directory = Application.persistentDataPath;
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

        private static void TryWriteCheckpoint(string stage, string detail)
        {
            try
            {
                if (checkpointStopwatch == null)
                {
                    checkpointStopwatch = Stopwatch.StartNew();
                }
                int sequence = Interlocked.Increment(ref checkpointSequence);
                Rma195ThermalProbeCheckpoint checkpoint = new Rma195ThermalProbeCheckpoint
                {
                    schema_version = 1,
                    sequence = sequence,
                    stage = stage,
                    elapsed_milliseconds = checkpointStopwatch.Elapsed.TotalMilliseconds,
                    device_model = SystemInfo.deviceModel,
                    detail = Bound(detail, 1024),
                };
                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string finalPath = Path.Combine(
                    directory,
                    CheckpointFilePrefix + sequence.ToString("D3", CultureInfo.InvariantCulture) + ".json");
                string temporaryPath = finalPath + ".tmp";
                string json = JsonUtility.ToJson(checkpoint, true) + "\n";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }
                File.Move(temporaryPath, finalPath);
                Debug.Log("RMA-195 thermal probe checkpoint sequence=" + sequence +
                    " stage=" + stage + " elapsed_ms=" + checkpoint.elapsed_milliseconds);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "RMA-195 thermal probe could not publish checkpoint '" + stage + "': " +
                        Bound(exception.Message, 1024));
            }
        }

        private static void WriteReport(string path, Rma195ThermalProbeReport report)
        {
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters ? value : value.Substring(0, maximumCharacters);
        }

        [Serializable]
        private sealed class Rma195ThermalProbeCheckpoint
        {
            public int schema_version;
            public int sequence;
            public string stage = string.Empty;
            public double elapsed_milliseconds;
            public string device_model = string.Empty;
            public string detail = string.Empty;
        }

        [Serializable]
        private sealed class Rma195ThermalProbeReport
        {
            public int schema_version;
            public string status = string.Empty;
            public string device_model = string.Empty;
            public string cloud_llm_base_url = string.Empty;
            public string cloud_llm_model_id = string.Empty;
            public double generation_window_seconds;
            public int generation_attempts;
            public int generation_succeeded_schema_valid;
            public int generation_failed_or_invalid;
            public string last_status = string.Empty;
            public string last_detail = string.Empty;
        }
    }
}
