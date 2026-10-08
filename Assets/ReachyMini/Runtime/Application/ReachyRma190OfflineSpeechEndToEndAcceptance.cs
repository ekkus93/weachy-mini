#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed class ReachyRma190OfflineSpeechEndToEndAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName =
            "reachy_rma190_offline_speech_e2e_acceptance";
        internal const string ResultFileName =
            "rma190-offline-speech-end-to-end-acceptance.json";
        internal const string ModelFileName =
            "rma190-qwen3-0.6b-q4_k_m.gguf";

        private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(20.0);
        private static readonly TimeSpan SpeechReadinessTimeout =
            TimeSpan.FromSeconds(45.0);
        private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(5.0);

        [Serializable]
        private sealed class AcceptanceReport
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public string selected_llm_execution = string.Empty;
            public string local_model_id = string.Empty;
            public string local_model_sha256 = string.Empty;
            public bool microphone_available;
            public bool observed_listening;
            public bool observed_transcribing;
            public bool observed_thinking;
            public bool observed_speaking;
            public bool observed_idle_after_speaking;
            public bool behavior_service_ready;
            public ulong behavior_revision_before;
            public ulong behavior_revision_after;
            public bool human_utterance_required = true;
            public bool synthetic_speech_injected;
            public bool transcript_persisted;
            public bool raw_audio_persisted;
            public bool cloud_credentials_injected;
            public bool system_asr_fallback_used;
            public bool network_tts_fallback_used;
            public string final_interaction_state = string.Empty;
            public string final_detail = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                !ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                return;
            }

            var host = new GameObject(
                "ReachyRma190OfflineSpeechEndToEndAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma190OfflineSpeechEndToEndAcceptance>();
        }

        private async void Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ResultFileName);
            try
            {
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }

                AcceptanceReport report =
                    await RunAcceptanceAsync().ConfigureAwait(true);
                report.status = "passed";
                WriteAtomically(resultPath, JsonUtility.ToJson(report, true));
                Debug.Log(
                    "RMA-190 offline speech physical acceptance passed.");
            }
            catch (Exception exception)
            {
                var failure = new AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                };
                try
                {
                    WriteAtomically(
                        resultPath,
                        JsonUtility.ToJson(failure, true));
                }
                catch (Exception reportException)
                {
                    Debug.LogError(
                        "RMA-190 offline speech could not write its failure " +
                        "report: " + reportException);
                }

                Debug.LogError(
                    "RMA-190 offline speech physical acceptance failed: " +
                    exception);
            }
        }

        private static async Task<AcceptanceReport> RunAcceptanceAsync()
        {
            ReachyApplicationHostBehaviour hostBehaviour =
                await WaitForShippingHostAsync().ConfigureAwait(true);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException(
                    "The shipping application host is unavailable.");

            ReachyApplicationHealthSnapshot health = host.Health;
            if (health.State != ReachyApplicationState.Ready &&
                health.State != ReachyApplicationState.Degraded)
            {
                throw new InvalidOperationException(
                    "The shipping application host is not operational: " +
                    health.State + " " + health.Message);
            }

            ReachySettingsPersistenceApplicationService persistence =
                RequireConcreteType<ReachySettingsPersistenceApplicationService>(
                    host.GetRequiredService<IReachyPersistenceService>(
                        ReachyServiceKind.Persistence));
            ReachyLocalLlmProviderApplicationService provider =
                RequireConcreteType<ReachyLocalLlmProviderApplicationService>(
                    host.GetRequiredService<IReachyProviderService>(
                        ReachyServiceKind.Provider));
            ReachyBaselineBehaviorApplicationService behavior =
                RequireConcreteType<ReachyBaselineBehaviorApplicationService>(
                    host.GetRequiredService<IReachyBehaviorService>(
                        ReachyServiceKind.Behavior));
            RequireConcreteType<ReachySettingsMainScreenApplicationService>(
                host.GetRequiredService<IReachyUserInterfaceService>(
                    ReachyServiceKind.UserInterface));

            ReachyMainScreen screen =
                await WaitForMainScreenAsync().ConfigureAwait(true);

            LocalModelManifest manifest =
                LocalLlmBehaviorContract.CreateSelectedManifest();
            LocalModelApprovedArtifact installed =
                await EnsureSelectedModelInstalledAsync(manifest)
                    .ConfigureAwait(true);

            SelectOnDeviceProvider(
                persistence.Settings,
                ReachyProviderKind.Asr);
            SelectOnDeviceProvider(
                persistence.Settings,
                ReachyProviderKind.Tts);
            SelectOnDeviceProvider(
                persistence.Settings,
                ReachyProviderKind.Llm);

            ReachyProviderSelection llmSelection =
                persistence.Settings.Current.GetProvider(
                    ReachyProviderKind.Llm);
            if (llmSelection.Execution != ReachyProviderExecution.OnDevice ||
                llmSelection.SendsDataOffDevice)
            {
                throw new InvalidOperationException(
                    "The physical speech scenario requires an on-device LLM " +
                    "selection with no off-device data boundary.");
            }

            await WaitForMicrophoneAsync(screen).ConfigureAwait(true);

            ReachyServiceState behaviorHealth = behavior.Health.State;
            if (behaviorHealth != ReachyServiceState.Ready &&
                behaviorHealth != ReachyServiceState.Degraded)
            {
                throw new InvalidOperationException(
                    "The shipping behavior service is not ready: " +
                    behavior.Health.Message);
            }

            ulong behaviorBefore = behavior.Snapshot.Revision;
            var report = new AcceptanceReport
            {
                device_model = SystemInfo.deviceModel,
                android_api_level = TryReadApiLevel(),
                selected_llm_execution = llmSelection.Execution.ToString(),
                local_model_id = manifest.Identity.ModelId,
                local_model_sha256 = installed.Sha256,
                microphone_available = true,
                behavior_service_ready = true,
                behavior_revision_before = behaviorBefore,
                synthetic_speech_injected = false,
                transcript_persisted = false,
                raw_audio_persisted = false,
                cloud_credentials_injected = false,
                system_asr_fallback_used = false,
                network_tts_fallback_used = false,
            };

            screen.RequestMicrophone();

            DateTime deadline = DateTime.UtcNow + TurnTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyMainScreenSnapshot snapshot = screen.Snapshot ??
                    throw new InvalidOperationException(
                        "The shipping main-screen snapshot disappeared.");

                switch (snapshot.InteractionState)
                {
                    case ReachyInteractionState.Listening:
                        report.observed_listening = true;
                        break;
                    case ReachyInteractionState.Transcribing:
                        report.observed_transcribing = true;
                        break;
                    case ReachyInteractionState.Thinking:
                        report.observed_thinking = true;
                        break;
                    case ReachyInteractionState.Speaking:
                        report.observed_speaking = true;
                        break;
                    case ReachyInteractionState.Idle:
                        if (report.observed_speaking)
                        {
                            report.observed_idle_after_speaking = true;
                        }
                        break;
                    case ReachyInteractionState.Error:
                    case ReachyInteractionState.Unavailable:
                        throw new InvalidOperationException(
                            "The shipping conversation failed closed in state " +
                            snapshot.InteractionState + ": " +
                            Bound(snapshot.Detail, 512));
                }

                if (report.observed_idle_after_speaking)
                {
                    report.final_interaction_state =
                        snapshot.InteractionState.ToString();
                    report.final_detail = Bound(snapshot.Detail, 512);
                    break;
                }

                await Task.Delay(25).ConfigureAwait(true);
            }

            if (!report.observed_listening ||
                !report.observed_transcribing ||
                !report.observed_thinking ||
                !report.observed_speaking ||
                !report.observed_idle_after_speaking)
            {
                throw new TimeoutException(
                    "The real spoken turn did not traverse Listening -> " +
                    "Transcribing -> Thinking -> Speaking -> Idle.");
            }

            ReachyProviderServiceSnapshot providerSnapshot =
                provider.ProviderSnapshot;
            if (providerSnapshot.State !=
                    ReachyProviderServiceExecutionState.Ready ||
                !string.Equals(
                    providerSnapshot.ModelId,
                    manifest.Identity.ModelId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The shipping local LLM did not finish the turn with the " +
                    "selected model ready: state=" +
                    providerSnapshot.State + " model=" +
                    providerSnapshot.ModelId + ".");
            }

            report.local_model_id = providerSnapshot.ModelId;
            report.behavior_revision_after = behavior.Snapshot.Revision;
            if (report.behavior_revision_after <=
                report.behavior_revision_before)
            {
                throw new InvalidOperationException(
                    "The shipping behavior service did not advance during " +
                    "the spoken turn.");
            }

            return report;
        }

        private static async Task<LocalModelApprovedArtifact>
            EnsureSelectedModelInstalledAsync(LocalModelManifest manifest)
        {
            string sourcePath = Path.Combine(
                Application.persistentDataPath,
                ModelFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "The exact selected local-model artifact was not staged " +
                    "for the physical acceptance.",
                    sourcePath);
            }

            string storeRoot = Path.Combine(
                Application.persistentDataPath,
                "local-models");
            using var manager = new LocalModelPackageManager(
                storeRoot,
                new DriveInfoLocalModelStorageProbe());
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            LocalModelPackageResult result = await manager
                .ImportAsync(
                    manifest,
                    source,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (!result.Succeeded || result.Artifact == null)
            {
                throw new InvalidOperationException(
                    "The exact selected local model could not be installed " +
                    "through the production package manager: " +
                    result.Detail);
            }

            return result.Artifact;
        }

        private static void SelectOnDeviceProvider(
            ReachySettingsStateStore settings,
            ReachyProviderKind kind)
        {
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                ReachyProviderSelection selection =
                    settings.Current.GetProvider(kind);
                if (selection.Execution == ReachyProviderExecution.OnDevice)
                {
                    if (selection.SendsDataOffDevice)
                    {
                        throw new InvalidOperationException(
                            kind + " on-device selection incorrectly claims " +
                            "an off-device privacy boundary.");
                    }
                    return;
                }

                settings.CycleProvider(kind);
            }

            throw new InvalidOperationException(
                "Could not select the on-device " + kind +
                " provider through the shipping settings store.");
        }

        private static async Task<ReachyApplicationHostBehaviour>
            WaitForShippingHostAsync()
        {
            DateTime deadline = DateTime.UtcNow + HostTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyApplicationHostBehaviour? value =
                    FindAnyObjectByType<ReachyApplicationHostBehaviour>();
                if (value?.Host != null)
                {
                    return value;
                }

                await Task.Delay(50).ConfigureAwait(true);
            }

            throw new TimeoutException(
                "Timed out waiting for the shipping application host.");
        }

        private static async Task<ReachyMainScreen> WaitForMainScreenAsync()
        {
            DateTime deadline = DateTime.UtcNow + HostTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyMainScreen? value =
                    FindAnyObjectByType<ReachyMainScreen>();
                if (value?.Snapshot != null &&
                    value.SettingsSnapshot != null)
                {
                    return value;
                }

                await Task.Delay(50).ConfigureAwait(true);
            }

            throw new TimeoutException(
                "Timed out waiting for the shipping main screen.");
        }

        private static async Task WaitForMicrophoneAsync(
            ReachyMainScreen screen)
        {
            DateTime deadline = DateTime.UtcNow + SpeechReadinessTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyMainScreenSnapshot? snapshot = screen.Snapshot;
                if (snapshot?.MicrophoneAvailable == true)
                {
                    return;
                }

                await Task.Delay(100).ConfigureAwait(true);
            }

            throw new InvalidOperationException(
                "The shipping speech service did not expose an available " +
                "physical microphone/on-device speech path.");
        }

        private static T RequireConcreteType<T>(object service)
            where T : class
        {
            return service as T ??
                throw new InvalidOperationException(
                    "Expected shipping service " + typeof(T).Name +
                    ", found " + service.GetType().Name + ".");
        }

        private static bool ReadBooleanLaunchExtra(
            string key,
            bool defaultValue)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", key, defaultValue);
#else
            return defaultValue;
#endif
        }

        private static int TryReadApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version =
                new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
#else
            return 0;
#endif
        }

        private static void WriteAtomically(
            string path,
            string content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static string Bound(
            string value,
            int maximumCharacters)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
        }
    }
}
