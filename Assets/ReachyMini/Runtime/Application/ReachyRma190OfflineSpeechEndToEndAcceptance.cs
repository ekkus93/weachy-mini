#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using ReachyMini.Providers;
using ReachyMini.Speech;
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
        internal const string ReadyFileName =
            "rma190-offline-speech-end-to-end-ready.json";
        internal const string StartFileName =
            "rma190-offline-speech-end-to-end-start.txt";
        internal const string ListeningFileName =
            "rma190-offline-speech-end-to-end-listening.json";
        internal const string RemoteOllamaLaunchExtraName =
            "reachy_rma190_remote_ollama_speech_diagnostic";
        internal const string RemoteOllamaModelLaunchExtraName =
            "reachy_rma190_remote_ollama_model_id";
        internal const string ModelFileName =
            "rma190-qwen3-0.6b-q4_k_m.gguf";
        private const string RemoteOllamaBaseUrl =
            "http://127.0.0.1:11434";
        private const string RemoteOllamaDefaultModelId =
            "devstral-small-2:24b";
        private const string RemoteOllamaDummyApiKey =
            "ollama-local-diagnostic-no-secret";

        private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(20.0);
        private static readonly TimeSpan SpeechReadinessTimeout =
            TimeSpan.FromSeconds(45.0);
        private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(5.0);
        private static string currentStage = "bootstrap";
        private static string currentFailureCode = string.Empty;
        private static long currentAvailableStorageBytes = -1L;
        private static string currentInteractionState = string.Empty;
        private static AcceptanceReport? currentReport;

        [Serializable]
        private sealed class AcceptanceReport
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string failure_stage = string.Empty;
            public string failure_code = string.Empty;
            public string last_interaction_state = string.Empty;
            public long available_storage_bytes = -1L;
            public string device_model = string.Empty;
            public int android_api_level;
            public string selected_llm_execution = string.Empty;
            public string local_model_id = string.Empty;
            public string local_model_sha256 = string.Empty;
            public bool remote_ollama_diagnostic;
            public string remote_ollama_endpoint = string.Empty;
            public string remote_ollama_model_id = string.Empty;
            public bool remote_ollama_uses_adb_reverse;
            public bool remote_ollama_dummy_bearer_used;
            public string remote_ollama_failure_detail = string.Empty;
            public string remote_ollama_tts_failure_detail = string.Empty;
            public bool microphone_available;
            public string asr_preflight_state = string.Empty;
            public bool recognizer_ready;
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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                (!ReadBooleanLaunchExtra(LaunchExtraName, false) &&
                 !ReadBooleanLaunchExtra(RemoteOllamaLaunchExtraName, false)))
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
                AcceptanceReport failure = currentReport ?? new AcceptanceReport
                {
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                };
                failure.status = "failed";
                // Never persist exception messages or stack traces: they may
                // include a private recognizer result or conversation text.
                failure.error = exception.GetType().Name;
                failure.failure_stage = currentStage;
                failure.failure_code = currentFailureCode;
                failure.last_interaction_state = currentInteractionState;
                failure.available_storage_bytes = currentAvailableStorageBytes;
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
                        "report (" + reportException.GetType().Name + ").");
                }

                Debug.LogError(
                    "RMA-190 offline speech physical acceptance failed (" +
                    exception.GetType().Name + ").");
            }
        }

        private static async Task<AcceptanceReport> RunAcceptanceAsync()
        {
            bool remoteOllamaDiagnostic = ReadBooleanLaunchExtra(
                RemoteOllamaLaunchExtraName,
                false);
            if (remoteOllamaDiagnostic &&
                ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                throw new InvalidOperationException(
                    "Offline acceptance and remote Ollama diagnostic modes " +
                    "cannot run together.");
            }

            currentStage = "wait_for_shipping_host";
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
            ReachySettingsMainScreenApplicationService screenService =
                RequireConcreteType<ReachySettingsMainScreenApplicationService>(
                host.GetRequiredService<IReachyUserInterfaceService>(
                    ReachyServiceKind.UserInterface));

            currentStage = "wait_for_main_screen";
            ReachyMainScreen screen =
                await WaitForMainScreenAsync().ConfigureAwait(true);

            LocalModelManifest? manifest = null;
            LocalModelApprovedArtifact? installed = null;
            string remoteOllamaModelId = string.Empty;
            if (!remoteOllamaDiagnostic)
            {
                currentStage = "install_selected_local_model";
                manifest = LocalLlmBehaviorContract.CreateSelectedManifest();
                installed = await EnsureSelectedModelInstalledAsync(manifest)
                    .ConfigureAwait(true);
            }

            currentStage = "select_on_device_providers";
            SelectOnDeviceProvider(
                persistence.Settings,
                ReachyProviderKind.Asr);
            SelectOnDeviceProvider(
                persistence.Settings,
                ReachyProviderKind.Tts);
            if (remoteOllamaDiagnostic)
            {
                currentStage = "configure_remote_ollama_diagnostic";
                remoteOllamaModelId = ConfigureRemoteOllamaDiagnostic(
                    persistence.Settings);
            }
            else
            {
                SelectOnDeviceProvider(
                    persistence.Settings,
                    ReachyProviderKind.Llm);
            }

            ReachyProviderSelection llmSelection =
                persistence.Settings.Current.GetProvider(
                    ReachyProviderKind.Llm);
            if (remoteOllamaDiagnostic
                ? llmSelection.Execution != ReachyProviderExecution.Cloud ||
                    !llmSelection.SendsDataOffDevice
                : llmSelection.Execution != ReachyProviderExecution.OnDevice ||
                    llmSelection.SendsDataOffDevice)
            {
                throw new InvalidOperationException(
                    remoteOllamaDiagnostic
                        ? "The remote diagnostic requires an explicitly selected " +
                            "network-backed LLM provider."
                        : "The physical speech scenario requires an on-device LLM " +
                            "selection with no off-device data boundary.");
            }

            currentReport = new AcceptanceReport
            {
                device_model = SystemInfo.deviceModel,
                android_api_level = TryReadApiLevel(),
                selected_llm_execution = llmSelection.Execution.ToString(),
                local_model_id = manifest?.Identity.ModelId ?? string.Empty,
                local_model_sha256 = installed?.Sha256 ?? string.Empty,
                available_storage_bytes = currentAvailableStorageBytes,
                remote_ollama_diagnostic = remoteOllamaDiagnostic,
                remote_ollama_endpoint = remoteOllamaDiagnostic
                    ? RemoteOllamaBaseUrl
                    : string.Empty,
                remote_ollama_model_id = remoteOllamaModelId,
                remote_ollama_uses_adb_reverse = remoteOllamaDiagnostic,
                remote_ollama_dummy_bearer_used = remoteOllamaDiagnostic,
                synthetic_speech_injected = false,
                transcript_persisted = false,
                raw_audio_persisted = false,
                cloud_credentials_injected = false,
            };

            currentStage = "probe_explicit_on_device_asr";
            var asrProvider = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                "rma190-explicit-on-device-asr-preflight",
                "en-US",
                SpeechReadinessTimeout);
            SpeechProviderAvailability asrAvailability;
            try
            {
                asrAvailability = await asrProvider.CheckAvailabilityAsync(
                    new AsrOptions("en-US", requestPartialResults: false),
                    CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                await asrProvider.DisposeAsync().ConfigureAwait(true);
            }
            currentReport.asr_preflight_state = asrAvailability.State.ToString();
            if (!asrAvailability.IsAvailable)
            {
                currentFailureCode =
                    "android_on_device_asr_availability_" +
                    asrAvailability.State.ToString().ToLowerInvariant();
                throw new InvalidOperationException(
                    "The selected explicit on-device ASR is not ready.");
            }

            currentStage = "wait_for_microphone";
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
            AcceptanceReport report = currentReport ??
                throw new InvalidOperationException(
                    "The physical speech acceptance report was not initialized.");
            report.microphone_available = true;
            report.behavior_service_ready = true;
            report.behavior_revision_before = behaviorBefore;
            report.system_asr_fallback_used = false;
            report.network_tts_fallback_used = false;

            currentStage = "wait_for_manual_start";
            WriteAtomically(
                Path.Combine(Application.persistentDataPath, ReadyFileName),
                "{\"setup_ready_for_manual_start\":true}");
            await WaitForManualStartAsync().ConfigureAwait(true);

            currentStage = "request_microphone";
            screen.RequestMicrophone();
            currentStage = "wait_for_listening";
            await WaitForListeningAsync(screen, screenService).ConfigureAwait(true);
            currentStage = "real_spoken_turn";
            report.observed_listening = true;
            report.recognizer_ready = true;
            currentInteractionState = ReachyInteractionState.Listening.ToString();
            WriteAtomically(
                Path.Combine(Application.persistentDataPath, ListeningFileName),
                "{\"listening_for_real_utterance\":true}");

            DateTime deadline = DateTime.UtcNow + TurnTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyMainScreenSnapshot snapshot = screen.Snapshot ??
                    throw new InvalidOperationException(
                        "The shipping main-screen snapshot disappeared.");
                currentInteractionState = snapshot.InteractionState.ToString();
                report.observed_listening |=
                    screenService.ObservedConversationListening;
                report.observed_transcribing |=
                    screenService.ObservedConversationTranscribing;
                report.observed_thinking |=
                    screenService.ObservedConversationThinking;
                report.observed_speaking |=
                    screenService.ObservedConversationSpeaking;
                report.observed_idle_after_speaking |=
                    screenService.ObservedConversationIdleAfterSpeaking;
                if (snapshot.InteractionState == ReachyInteractionState.Error ||
                    snapshot.InteractionState == ReachyInteractionState.Unavailable)
                {
                    currentFailureCode = string.IsNullOrEmpty(
                            screenService.LastConversationFailureCode)
                        ? "conversation-error-state"
                        : screenService.LastConversationFailureCode;
                    if (remoteOllamaDiagnostic &&
                        string.Equals(
                            currentFailureCode,
                            "llm-http-failure",
                            StringComparison.Ordinal))
                    {
                        report.remote_ollama_failure_detail =
                            BoundRemoteDiagnosticDetail(snapshot.Detail);
                    }
                    else if (remoteOllamaDiagnostic &&
                        string.Equals(
                            currentFailureCode,
                            "tts-playback-failed",
                            StringComparison.Ordinal))
                    {
                        report.remote_ollama_tts_failure_detail =
                            BoundRemoteDiagnosticDetail(snapshot.Detail);
                    }
                }

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
                            snapshot.InteractionState + ".");
                }

                if (report.observed_idle_after_speaking)
                {
                    report.final_interaction_state =
                        snapshot.InteractionState.ToString();
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
            currentStage = "verify_provider_and_behavior";
            if (providerSnapshot.ExecutionState !=
                    ReachyProviderServiceExecutionState.Ready ||
                !string.Equals(
                    providerSnapshot.ActiveModelId,
                    remoteOllamaDiagnostic
                        ? ReachyLocalLlmProviderApplicationService.CloudLlmProfileProviderId
                        : manifest!.Identity.ModelId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The selected LLM provider did not finish the turn ready: " +
                    "state=" +
                    providerSnapshot.ExecutionState + " model=" +
                    providerSnapshot.ActiveModelId + ".");
            }

            if (!remoteOllamaDiagnostic)
            {
                report.local_model_id = providerSnapshot.ActiveModelId;
            }
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
            Directory.CreateDirectory(storeRoot);
            currentStage = "probe_model_storage";
            var storageProbe = new ReachyPlatformLocalModelStorageProbe();
            currentAvailableStorageBytes =
                storageProbe.GetAvailableBytes(storeRoot);

            currentStage = "construct_model_package_manager";
            using var manager = new LocalModelPackageManager(
                storeRoot,
                storageProbe);
            currentStage = "open_selected_model_source";
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            currentStage = "import_selected_model";
            LocalModelPackageResult result = await manager
                .ImportAsync(
                    manifest,
                    source,
                    CancellationToken.None)
                .ConfigureAwait(true);
            currentStage = "validate_imported_model";
            if (!result.Succeeded || result.Artifact == null)
            {
                currentFailureCode = result.Failure.ToString();
                throw new InvalidOperationException(
                    "The exact selected local model could not be installed " +
                    "through the production package manager: " +
                    result.Detail);
            }

            return result.Artifact;
        }

        private static string ConfigureRemoteOllamaDiagnostic(
            ReachySettingsStateStore settings)
        {
            string modelId = ReadStringLaunchExtra(
                RemoteOllamaModelLaunchExtraName,
                RemoteOllamaDefaultModelId);
            if (string.IsNullOrWhiteSpace(modelId) ||
                modelId.Length > 160 ||
                !string.Equals(modelId, modelId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The remote Ollama model identifier is invalid.");
            }
            for (int index = 0; index < modelId.Length; ++index)
            {
                char value = modelId[index];
                if (!(char.IsLetterOrDigit(value) ||
                      value == '-' || value == '_' || value == '.' ||
                      value == ':' || value == '/'))
                {
                    throw new InvalidOperationException(
                        "The remote Ollama model identifier contains unsupported characters.");
                }
            }

            var credentials = new ReachyCloudLlmCredentialCoordinator();
            credentials.Initialize();
            string profileStatus = credentials.SaveProfile(
                RemoteOllamaBaseUrl,
                ReachyProviderEndpointStyle.ChatCompletions,
                modelId);
            if (credentials.CurrentProfile == null)
            {
                throw new InvalidOperationException(
                    "Could not save the loopback Ollama profile: " + profileStatus);
            }

            string keyStatus = credentials.SaveApiKey(RemoteOllamaDummyApiKey);
            if (!credentials.HasApiKey)
            {
                throw new InvalidOperationException(
                    "Could not configure the Ollama dummy bearer credential: " +
                    keyStatus);
            }

            string authorizationStatus = credentials.GrantAuthorization();
            if (!credentials.IsAuthorized)
            {
                throw new InvalidOperationException(
                    "Could not authorize the explicit remote Ollama diagnostic: " +
                    authorizationStatus);
            }

            for (int attempt = 0; attempt < 3; ++attempt)
            {
                ReachyProviderSelection selection = settings.Current.GetProvider(
                    ReachyProviderKind.Llm);
                if (selection.Execution == ReachyProviderExecution.Cloud &&
                    selection.SendsDataOffDevice)
                {
                    return modelId;
                }
                settings.CycleProvider(ReachyProviderKind.Llm);
            }

            throw new InvalidOperationException(
                "Could not explicitly select the remote Ollama LLM preference.");
        }

        private static string BoundRemoteDiagnosticDetail(string detail)
        {
            const int MaximumDiagnosticCharacters = 512;
            if (string.IsNullOrWhiteSpace(detail))
            {
                return string.Empty;
            }
            return detail.Length <= MaximumDiagnosticCharacters
                ? detail
                : detail.Substring(0, MaximumDiagnosticCharacters);
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

        private static async Task WaitForListeningAsync(
            ReachyMainScreen screen,
            ReachySettingsMainScreenApplicationService screenService)
        {
            DateTime deadline = DateTime.UtcNow + SpeechReadinessTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyMainScreenSnapshot? snapshot = screen.Snapshot;
                if (screenService.ObservedConversationRecognizerReady &&
                    snapshot?.InteractionState == ReachyInteractionState.Listening)
                {
                    return;
                }
                if (snapshot?.InteractionState == ReachyInteractionState.Error ||
                    snapshot?.InteractionState == ReachyInteractionState.Unavailable)
                {
                    currentInteractionState =
                        snapshot.InteractionState.ToString();
                    currentFailureCode =
                        screenService.LastConversationFailureCode;
                    throw new InvalidOperationException(
                        "The shipping speech service did not enter Listening state.");
                }
                await Task.Delay(50).ConfigureAwait(true);
            }

            throw new TimeoutException(
                "The on-device recognizer did not become ready to listen.");
        }

        private static async Task WaitForManualStartAsync()
        {
            string startPath = Path.Combine(
                Application.persistentDataPath,
                StartFileName);
            while (true)
            {
                if (File.Exists(startPath))
                {
                    File.Delete(startPath);
                    return;
                }
                await Task.Delay(50).ConfigureAwait(true);
            }
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

        private static string ReadStringLaunchExtra(
            string key,
            string defaultValue)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>("getIntent");
            string? result = intent.Call<string>("getStringExtra", key);
            return string.IsNullOrWhiteSpace(result) ? defaultValue : result;
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

    }
}
