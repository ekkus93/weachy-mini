#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Behavior;
using ReachyMini.Perception;
using ReachyMini.Providers;
using ReachyMini.Speech;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    // RMA-195 closure acceptance: exercises the shipping application host and
    // the hardware-backed capabilities that an unattended Android runner can
    // prove without fabricating user speech, cloud credentials, or a local
    // model install. Human-utterance ASR -> LLM -> behavior -> TTS remains a
    // separate RMA-125/RMA-190 end-to-end scenario; this gate proves the
    // shipping composition exposes the real components that scenario uses.
    internal sealed class ReachyRma195ApplicationCompositionAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName =
            "reachy_rma195_application_composition_acceptance";
        internal const string ResultFileName =
            "rma195-application-composition-acceptance.json";

        private const string LanguageTag = "en-US";
        private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(15.0);
        private static readonly TimeSpan TtsTimeout = TimeSpan.FromSeconds(30.0);

        [Serializable]
        private sealed class AcceptanceReport
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public string application_state = string.Empty;
            public string simulation_service = string.Empty;
            public string camera_service = string.Empty;
            public string audio_service = string.Empty;
            public string provider_service = string.Empty;
            public string perception_service = string.Empty;
            public string behavior_service = string.Empty;
            public string persistence_service = string.Empty;
            public string ui_service = string.Empty;
            public bool local_llm_capability;
            public bool cloud_llm_capability;
            public bool cloud_vlm_capability;
            public bool full_behavior_intent_capability;
            public bool full_behavior_intent_accepted;
            public string behavior_health = string.Empty;
            public string camera_permission = string.Empty;
            public int available_camera_count;
            public bool front_camera_available;
            public bool rear_camera_available;
            public string on_device_asr_state = string.Empty;
            public string on_device_asr_diagnostic = string.Empty;
            public string offline_tts_state = string.Empty;
            public string offline_tts_diagnostic = string.Empty;
            public string selected_tts_voice_id = string.Empty;
            public bool selected_tts_voice_installed;
            public bool selected_tts_voice_may_use_network;
            public bool tts_started;
            public bool tts_completed;
            public bool human_asr_transcript_injected;
            public bool cloud_credentials_injected;
            public bool local_model_injected;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                !ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                return;
            }

            var host = new GameObject("ReachyRma195ApplicationCompositionAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma195ApplicationCompositionAcceptance>();
        }

        private async void Start()
        {
            string resultPath = Path.Combine(Application.persistentDataPath, ResultFileName);
            try
            {
                AcceptanceReport report = await RunAcceptanceAsync().ConfigureAwait(true);
                report.status = "passed";
                WriteAtomically(resultPath, JsonUtility.ToJson(report, true));
                Debug.Log("RMA-195 application-composition physical acceptance passed.");
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
                    WriteAtomically(resultPath, JsonUtility.ToJson(failure, true));
                }
                catch (Exception reportException)
                {
                    Debug.LogError(
                        "RMA-195 could not write its failure report: " + reportException);
                }
                Debug.LogError(
                    "RMA-195 application-composition physical acceptance failed: " +
                    exception);
            }
        }

        private static async Task<AcceptanceReport> RunAcceptanceAsync()
        {
            ReachyApplicationHostBehaviour hostBehaviour =
                await WaitForShippingHostAsync().ConfigureAwait(true);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException("The shipping application host is unavailable.");
            ReachyApplicationHealthSnapshot health = host.Health;
            if (health.State != ReachyApplicationState.Ready &&
                health.State != ReachyApplicationState.Degraded)
            {
                throw new InvalidOperationException(
                    "The shipping application host is not operational: " +
                    health.State + " " + health.Message);
            }

            IReachySimulationService simulation =
                host.GetRequiredService<IReachySimulationService>(ReachyServiceKind.Simulation);
            IReachyCameraService camera =
                host.GetRequiredService<IReachyCameraService>(ReachyServiceKind.Camera);
            IReachyAudioService audio =
                host.GetRequiredService<IReachyAudioService>(ReachyServiceKind.Audio);
            IReachyProviderService provider =
                host.GetRequiredService<IReachyProviderService>(ReachyServiceKind.Provider);
            IReachyPerceptionService perception =
                host.GetRequiredService<IReachyPerceptionService>(ReachyServiceKind.Perception);
            IReachyBehaviorService behavior =
                host.GetRequiredService<IReachyBehaviorService>(ReachyServiceKind.Behavior);
            IReachyPersistenceService persistence =
                host.GetRequiredService<IReachyPersistenceService>(ReachyServiceKind.Persistence);
            IReachyUserInterfaceService ui =
                host.GetRequiredService<IReachyUserInterfaceService>(ReachyServiceKind.UserInterface);

            RequireConcreteType<ReachyProductionSimulationApplicationService>(simulation);
            ReachyDiscoveredCameraApplicationService shippingCamera =
                RequireConcreteType<ReachyDiscoveredCameraApplicationService>(camera);
            RequireConcreteType<ReachyAndroidSpeechCapabilityApplicationService>(audio);
            RequireConcreteType<ReachyLocalLlmProviderApplicationService>(provider);
            RequireConcreteType<ReachyAndroidPerceptionApplicationService>(perception);
            ReachyBaselineBehaviorApplicationService shippingBehavior =
                RequireConcreteType<ReachyBaselineBehaviorApplicationService>(behavior);
            RequireConcreteType<ReachySettingsPersistenceApplicationService>(persistence);
            RequireConcreteType<ReachySettingsMainScreenApplicationService>(ui);

            if (provider is not ILocalLlmProviderCapability ||
                provider is not ICloudLlmProviderCapability)
            {
                throw new InvalidOperationException(
                    "The shipping provider service does not expose both local and cloud LLM capabilities.");
            }
            if (perception is not ICloudVlmProviderCapability)
            {
                throw new InvalidOperationException(
                    "The shipping perception service does not expose the cloud VLM capability.");
            }
            if (behavior is not IReachyBehaviorIntentCapability behaviorIntents)
            {
                throw new InvalidOperationException(
                    "The shipping behavior service does not expose full RMA-151 intent execution.");
            }

            ReachyBehaviorIntent intent = ParsePhysicalBehaviorIntent();
            if (!behaviorIntents.TryExecuteIntent(intent, out string behaviorDiagnostic))
            {
                throw new InvalidOperationException(
                    "The shipping behavior service rejected a validated full intent: " +
                    behaviorDiagnostic);
            }
            await Task.Delay(1000).ConfigureAwait(true);
            if (shippingBehavior.Health.State == ReachyServiceState.Faulted)
            {
                throw new InvalidOperationException(
                    "The shipping behavior service faulted after full-intent dispatch: " +
                    shippingBehavior.Health.Message);
            }

            ReachyCameraCapabilitySnapshot cameraSnapshot = shippingCamera.Capabilities.Current;
            bool front = false;
            bool rear = false;
            for (int index = 0; index < cameraSnapshot.Cameras.Count; ++index)
            {
                ReachyCameraCapability capability = cameraSnapshot.Cameras[index];
                if (capability.Availability != ReachyCameraAvailabilityState.Available)
                {
                    continue;
                }
                front |= capability.Facing == ReachyDeviceCameraFacing.Front;
                rear |= capability.Facing == ReachyDeviceCameraFacing.Rear;
            }
            if (cameraSnapshot.Permission != ReachyCameraPermissionState.Granted ||
                !front || !rear)
            {
                throw new InvalidOperationException(
                    "The physical RMA-195 composition requires granted camera permission and " +
                    "available front/rear cameras; permission=" + cameraSnapshot.Permission +
                    " front=" + front + " rear=" + rear + ".");
            }

            SpeechProviderAvailability asr = await ProbeOnDeviceAsrAsync().ConfigureAwait(true);
            if (!asr.IsAvailable)
            {
                throw new InvalidOperationException(
                    "Explicit on-device ASR is not ready on the RMA-195 physical device: " +
                    asr.State + " " + asr.Diagnostic);
            }

            TtsPhysicalResult tts = await ExerciseOfflineTtsAsync().ConfigureAwait(true);
            if (!tts.Availability.IsAvailable || tts.Voice == null || !tts.Completed)
            {
                throw new InvalidOperationException(
                    "Offline TTS did not complete on the RMA-195 physical device: " +
                    tts.Availability.State + " " + tts.Availability.Diagnostic);
            }

            return new AcceptanceReport
            {
                status = "running",
                device_model = SystemInfo.deviceModel,
                android_api_level = ReadApiLevel(),
                application_state = health.State.ToString(),
                simulation_service = simulation.GetType().Name,
                camera_service = camera.GetType().Name,
                audio_service = audio.GetType().Name,
                provider_service = provider.GetType().Name,
                perception_service = perception.GetType().Name,
                behavior_service = behavior.GetType().Name,
                persistence_service = persistence.GetType().Name,
                ui_service = ui.GetType().Name,
                local_llm_capability = provider is ILocalLlmProviderCapability,
                cloud_llm_capability = provider is ICloudLlmProviderCapability,
                cloud_vlm_capability = perception is ICloudVlmProviderCapability,
                full_behavior_intent_capability = true,
                full_behavior_intent_accepted = true,
                behavior_health = shippingBehavior.Health.State.ToString(),
                camera_permission = cameraSnapshot.Permission.ToString(),
                available_camera_count = cameraSnapshot.AvailableCameraCount,
                front_camera_available = front,
                rear_camera_available = rear,
                on_device_asr_state = asr.State.ToString(),
                on_device_asr_diagnostic = Bound(asr.Diagnostic, 512),
                offline_tts_state = tts.Availability.State.ToString(),
                offline_tts_diagnostic = Bound(tts.Availability.Diagnostic, 512),
                selected_tts_voice_id = tts.Voice.VoiceId,
                selected_tts_voice_installed = tts.Voice.IsInstalled,
                selected_tts_voice_may_use_network = tts.Voice.MayUseNetwork,
                tts_started = tts.Started,
                tts_completed = tts.Completed,
                human_asr_transcript_injected = false,
                cloud_credentials_injected = false,
                local_model_injected = false,
            };
        }

        private static async Task<ReachyApplicationHostBehaviour> WaitForShippingHostAsync()
        {
            DateTime deadline = DateTime.UtcNow + HostTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ReachyApplicationHostBehaviour? host =
                    UnityEngine.Object.FindAnyObjectByType<ReachyApplicationHostBehaviour>();
                if (host?.Host != null)
                {
                    return host;
                }
                await Task.Delay(50).ConfigureAwait(true);
            }
            throw new TimeoutException("Timed out waiting for the shipping application host.");
        }

        private static TService RequireConcreteType<TService>(IReachyApplicationService service)
            where TService : class, IReachyApplicationService
        {
            return service as TService ?? throw new InvalidOperationException(
                "Expected shipping service " + typeof(TService).Name +
                " but composition provided " + service.GetType().Name + ".");
        }

        private static ReachyBehaviorIntent ParsePhysicalBehaviorIntent()
        {
            const string json =
                "{\"schema_version\":1," +
                "\"expression\":\"pleased\"," +
                "\"gesture\":\"nod\"," +
                "\"urgency\":\"normal\"," +
                "\"timing\":{" +
                    "\"start_delay_ms\":25," +
                    "\"maximum_duration_ms\":1200}}";
            ReachyBehaviorIntentValidationResult result =
                ReachyBehaviorIntentJsonParser.Validate(json);
            if (!result.Succeeded || result.Intent == null)
            {
                throw new InvalidOperationException(
                    "The built-in physical behavior-intent fixture is invalid: " + result.Detail);
            }
            return result.Intent;
        }

        private static async Task<SpeechProviderAvailability> ProbeOnDeviceAsrAsync()
        {
            IAsrProvider provider = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                "rma195-composition-asr",
                LanguageTag,
                TimeSpan.FromSeconds(30.0));
            try
            {
                return await provider.CheckAvailabilityAsync(
                    new AsrOptions(LanguageTag, requestPartialResults: false),
                    CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private sealed class TtsPhysicalResult
        {
            public TtsPhysicalResult(
                SpeechProviderAvailability availability,
                TtsVoice? voice,
                bool started,
                bool completed)
            {
                Availability = availability;
                Voice = voice;
                Started = started;
                Completed = completed;
            }

            public SpeechProviderAvailability Availability { get; }
            public TtsVoice? Voice { get; }
            public bool Started { get; }
            public bool Completed { get; }
        }

        private static async Task<TtsPhysicalResult> ExerciseOfflineTtsAsync()
        {
            ITtsProvider provider = ReachyAndroidOfflineTtsProviderFactory.Create(
                "rma195-composition-tts",
                LanguageTag);
            try
            {
                SpeechProviderAvailability availability =
                    await provider.CheckAvailabilityAsync(CancellationToken.None)
                        .ConfigureAwait(true);
                if (!availability.IsAvailable)
                {
                    return new TtsPhysicalResult(availability, null, false, false);
                }

                IReadOnlyList<TtsVoice> voices =
                    await provider.GetVoicesAsync(CancellationToken.None).ConfigureAwait(true);
                TtsVoice? voice = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                    voices,
                    LanguageTag);
                if (voice == null)
                {
                    return new TtsPhysicalResult(availability, null, false, false);
                }

                var selection = new SpeechProviderSelection(provider.Descriptor);
                var context = new SpeechOperationContext(
                    "rma195-composition-tts-1",
                    selection.Current,
                    TtsTimeout);
                var request = new TtsRequest(
                    context,
                    "Reachy Mini application composition test.",
                    voice.VoiceId);
                bool started = false;
                bool completed = false;
                using var timeout = new CancellationTokenSource(TtsTimeout);
                await foreach (TtsEvent ttsEvent in provider.SpeakAsync(
                    request,
                    timeout.Token))
                {
                    started |= ttsEvent.Kind == TtsEventKind.Started;
                    if (ttsEvent.Kind == TtsEventKind.Completed)
                    {
                        completed = true;
                        break;
                    }
                    if (ttsEvent.Kind == TtsEventKind.Failed)
                    {
                        throw new InvalidOperationException(
                            "Offline TTS failed during physical acceptance: " +
                            (ttsEvent.Error?.Diagnostic ?? "unknown"));
                    }
                    if (ttsEvent.Kind == TtsEventKind.Cancelled)
                    {
                        throw new InvalidOperationException(
                            "Offline TTS was cancelled during physical acceptance.");
                    }
                }
                return new TtsPhysicalResult(availability, voice, started, completed);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static int ReadApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
#else
            return 0;
#endif
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

        private static bool ReadBooleanLaunchExtra(string name, bool fallback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, fallback);
#else
            return fallback;
#endif
        }

        private static void WriteAtomically(string path, string json)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json + "\n", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporaryPath, path);
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string text = value ?? string.Empty;
            return text.Length <= maximumCharacters
                ? text
                : text.Substring(0, maximumCharacters);
        }
    }
}
