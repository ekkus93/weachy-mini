#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Providers;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma190RearCameraVlmEndToEndAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma190_rear_camera_vlm_e2e_acceptance";
        public const string FixtureBaseUrlExtra =
            "reachy_rma190_rear_camera_vlm_fixture_base_url";
        public const string ResultFileName =
            "rma190-rear-camera-vlm-end-to-end-acceptance.json";
        private const string ObjectName =
            "ReachyRma190RearCameraVlmEndToEndAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsRequested() ||
                Object.FindAnyObjectByType<
                    ReachyRma190RearCameraVlmEndToEndAcceptance>() != null)
            {
                return;
            }

            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<
                ReachyRma190RearCameraVlmEndToEndAcceptance>();
        }

        private static bool IsRequested()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>(
                        "currentActivity");
                using AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>(
                    "getBooleanExtra",
                    AcceptanceLaunchExtra,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect the RMA-190 rear-camera/VLM " +
                    "acceptance launch extra: " + exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyRma190RearCameraVlmEndToEndAcceptance :
        MonoBehaviour
    {
        private const int OverallTimeoutMilliseconds = 130_000;
        private const int ReadyPollMilliseconds = 25;
        private const string FixtureProfileId =
            "rma190-rear-camera-deterministic-fixture";
        private const string FixtureModelId = "rma190-vlm-fixture";
        private const string FixtureApiKey =
            "rma190-fixture-credential-not-a-live-secret";
        private const string VisualQuestion =
            "What is visible behind Reachy?";
        private const string ExpectedFixtureResponse =
            "rma190-rear-camera-fixture-observed";
        private static string currentStage = "not-started";

        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma190RearCameraVlmEndToEndAcceptanceBootstrap
                    .ResultFileName);
            DeleteIfPresent(resultPath);

            SetStage("starting");
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
                        "RMA-190 rear-camera/VLM acceptance failed without " +
                        "an exception.");
                report = Report.Failure(
                    exception.ToString(),
                    currentStage);
                Debug.LogError(
                    "RMA-190 rear-camera/VLM end-to-end acceptance failed: " +
                    exception,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-190 rear-camera/VLM acceptance was cancelled.",
                    currentStage);
            }
            else
            {
                report = task.Result;
            }

            WriteAtomically(
                resultPath,
                JsonUtility.ToJson(report, prettyPrint: true));
            Destroy(gameObject);
        }

        private static async Task<Report> RunAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var timeout = new CancellationTokenSource(
                OverallTimeoutMilliseconds);
            CancellationToken cancellationToken = timeout.Token;

            SetStage("read-fixture-endpoint");
            string fixtureBaseUrl = ReadStringLaunchExtra(
                ReachyRma190RearCameraVlmEndToEndAcceptanceBootstrap
                    .FixtureBaseUrlExtra);
            if (string.IsNullOrWhiteSpace(fixtureBaseUrl))
            {
                throw new InvalidOperationException(
                    "The rear-camera/VLM acceptance requires an explicitly " +
                    "supplied fixture base URL.");
            }

            SetStage("wait-shipping-host");
            ReachyApplicationHostBehaviour hostBehaviour =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForShippingHostAsync(cancellationToken);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException(
                    "The shipping application host is unavailable.");

            SetStage("resolve-shipping-services");
            IReachyPerceptionService perceptionService =
                host.GetRequiredService<IReachyPerceptionService>(
                    ReachyServiceKind.Perception);
            IReachyPersistenceService persistenceService =
                host.GetRequiredService<IReachyPersistenceService>(
                    ReachyServiceKind.Persistence);
            if (perceptionService is not
                ReachyAndroidPerceptionApplicationService perception)
            {
                throw new InvalidOperationException(
                    "The rear-camera/VLM acceptance requires the shipping " +
                    "Android perception service.");
            }
            if (persistenceService is not
                ReachySettingsPersistenceApplicationService persistence)
            {
                throw new InvalidOperationException(
                    "The rear-camera/VLM acceptance requires the shipping " +
                    "durable-settings service.");
            }

            ReachyProductionAuthoritativeRuntime runtime =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForRuntimeAsync(cancellationToken);
            ReachyAndroidCameraDiscovery discovery =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidCameraDiscovery>(
                        cancellationToken,
                        "camera discovery");
            ReachyAndroidCameraAcquisition acquisition =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidCameraAcquisition>(
                        cancellationToken,
                        "camera acquisition");
            ReachyAndroidCameraTextureBridge bridge =
                acquisition.GetComponent<ReachyAndroidCameraTextureBridge>() ??
                throw new InvalidOperationException(
                    "The shipping camera acquisition has no texture bridge.");
            ReachyAndroidPerceptionDriver driver =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidPerceptionDriver>(
                        cancellationToken,
                        "perception driver");

            IReachyApplicationInterruptionParticipant interruption =
                perception as IReachyApplicationInterruptionParticipant ??
                throw new InvalidOperationException(
                    "The shipping perception service cannot be suspended " +
                    "for deterministic physical acceptance.");

            ReachyCameraCalibrationProfile[] originalProfiles =
                ReachyRma190CameraEndToEndAcceptance.CopyProfiles(
                    persistence.CameraCalibrations.State.Current);
            bool cameraWasActive = acquisition.State.Current.IsActive;
            ReachyCameraFacing previousFacing = acquisition.PreferredFacing;
            bool perceptionPaused = false;
            var coordinator = new ReachyCloudVlmCredentialCoordinator();

            try
            {
                SetStage("configure-fixture-provider");
                coordinator.Initialize();
                string profileResult = coordinator.SaveProfile(
                    fixtureBaseUrl,
                    ReachyProviderEndpointStyle.ChatCompletions,
                    FixtureModelId);
                if (coordinator.CurrentProfile == null)
                {
                    throw new InvalidOperationException(
                        "The fixture VLM profile was not persisted: " +
                        profileResult);
                }
                string keyResult = coordinator.SaveApiKey(FixtureApiKey);
                if (!coordinator.HasApiKey)
                {
                    throw new InvalidOperationException(
                        "The fixture VLM credential was not stored through " +
                        "the Android Keystore path: " + keyResult);
                }
                string authorizationResult =
                    coordinator.GrantAuthorization();
                if (!coordinator.IsAuthorized)
                {
                    throw new InvalidOperationException(
                        "The fixture VLM provider was not explicitly " +
                        "authorized: " + authorizationResult);
                }

                SetStage("pause-continuous-perception");
                interruption.PauseForApplicationInterruption();
                perceptionPaused = true;

                SetStage("refresh-camera-capabilities");
                discovery.RefreshPermissionAndCapabilities();
                ReachyCameraCapabilitySnapshot capabilities =
                    discovery.State.Current;
                if (capabilities.Permission !=
                    ReachyCameraPermissionState.Granted)
                {
                    throw new InvalidOperationException(
                        "Rear-camera/VLM acceptance requires granted camera " +
                        "permission; state=" + capabilities.Permission + ".");
                }
                if (!HasAvailableRearCamera(capabilities))
                {
                    throw new InvalidOperationException(
                        "No available rear camera with YUV analysis support " +
                        "was reported by the shipping discovery service.");
                }

                SetStage("switch-to-rear-camera");
                if (acquisition.State.Current.IsActive)
                {
                    acquisition.StopAcquisition();
                    await ReachyRma190CameraEndToEndAcceptance
                        .WaitForCameraStoppedAsync(
                            acquisition,
                            cancellationToken);
                }
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ReachyCameraTextureFrameDescriptor descriptor =
                    await WaitForRearTextureAsync(
                        acquisition,
                        bridge,
                        cancellationToken);

                SetStage("install-deterministic-calibration-fixture");
                ReachySimAuthoritativeStateFrame state =
                    await ReachyRma190CameraEndToEndAcceptance
                        .WaitForAuthoritativeStateAsync(
                            runtime,
                            0UL,
                            cancellationToken);
                ReachySimBodyPoseSnapshot cameraPose =
                    ReachyRma190CameraEndToEndAcceptance.FindCameraPose(
                        state);
                ReachyCameraCalibrationProfile fixture =
                    ReachyRma190CameraEndToEndAcceptance
                        .CreateDeterministicFixture(
                            descriptor,
                            cameraPose,
                            ReachyDeviceCameraFacing.Rear,
                            FixtureProfileId,
                            mirrorHorizontally: false,
                            "Deterministic RMA-190 rear-camera/VLM " +
                            "reprojection fixture; not a measured device " +
                            "calibration.");
                persistence.CameraCalibrations.State.ReplaceAll(
                    new[] { fixture });

                SetStage("capture-real-rear-transformed-frame");
                ReachyVisionFrame frame =
                    await ReachyRma190CameraEndToEndAcceptance
                        .CaptureAcceptanceFrameAsync(
                            driver,
                            cancellationToken);
                try
                {
                    ValidateRearFrame(
                        frame,
                        descriptor.CameraId,
                        descriptor.SessionId);

                    SetStage("ask-visual-question-through-production-vlm");
                    VisionLanguageResult result =
                        await perception.AnalyzeSceneAsync(
                            frame,
                            VisualQuestion,
                            "rma190-rear-camera-vlm",
                            cancellationToken);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "The production VLM path failed (" +
                            result.Status + "): " + result.Diagnostic);
                    }
                    if (!string.Equals(
                            result.Text,
                            ExpectedFixtureResponse,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "The production VLM path returned an unexpected " +
                            "fixture response.");
                    }

                    SetStage("completed");
                    return Report.Success(
                        descriptor,
                        frame,
                        fixture,
                        result.Text);
                }
                finally
                {
                    await frame.DisposeAsync();
                }
            }
            finally
            {
                persistence.CameraCalibrations.State.ReplaceAll(
                    originalProfiles);
                try
                {
                    if (coordinator.IsAuthorized)
                    {
                        _ = coordinator.RevokeAuthorization();
                    }
                }
                catch (Exception)
                {
                    // Preserve the primary acceptance outcome. A fresh
                    // pm-clear is performed before each physical run.
                }

                try
                {
                    if (acquisition.State.Current.IsActive)
                    {
                        acquisition.StopAcquisition();
                        using var cleanupTimeout =
                            new CancellationTokenSource(5_000);
                        await ReachyRma190CameraEndToEndAcceptance
                            .WaitForCameraStoppedAsync(
                                acquisition,
                                cleanupTimeout.Token);
                    }
                    if (cameraWasActive)
                    {
                        acquisition.StartPreferred(
                            previousFacing ==
                                ReachyCameraFacing.Unconfigured
                                ? ReachyCameraFacing.Rear
                                : previousFacing);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "RMA-190 rear-camera/VLM acceptance could not " +
                        "restore the prior camera session: " +
                        exception.Message);
                }

                if (perceptionPaused)
                {
                    interruption.ResumeAfterApplicationInterruption();
                }
            }
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-190 rear-camera/VLM acceptance requires an Android " +
                "player.");
#endif
        }

        private static bool HasAvailableRearCamera(
            ReachyCameraCapabilitySnapshot capabilities)
        {
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (camera.Facing == ReachyDeviceCameraFacing.Rear &&
                    camera.Availability ==
                        ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<ReachyCameraTextureFrameDescriptor>
            WaitForRearTextureAsync(
                ReachyAndroidCameraAcquisition acquisition,
                ReachyAndroidCameraTextureBridge bridge,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                acquisition.RefreshNow();
                ReachyCameraAcquisitionSnapshot acquisitionState =
                    acquisition.State.Current;
                if (acquisitionState.State ==
                        ReachyCameraAcquisitionState.Faulted ||
                    acquisitionState.State ==
                        ReachyCameraAcquisitionState.Unavailable ||
                    acquisitionState.State ==
                        ReachyCameraAcquisitionState.PermissionRevoked)
                {
                    throw new InvalidOperationException(
                        "Rear camera acquisition failed: " +
                        acquisitionState.State + " / " +
                        acquisitionState.Message);
                }

                ReachyCameraTextureFrameDescriptor? descriptor =
                    bridge.Current.Frame;
                if (acquisitionState.State ==
                        ReachyCameraAcquisitionState.Running &&
                    bridge.Current.HasTexture &&
                    descriptor != null &&
                    descriptor.LensFacing ==
                        ReachyDeviceCameraFacing.Rear &&
                    descriptor.SessionId == acquisitionState.SessionId)
                {
                    return descriptor;
                }

                await Task.Delay(
                    ReadyPollMilliseconds,
                    cancellationToken);
            }
        }

        private static void ValidateRearFrame(
            ReachyVisionFrame frame,
            string cameraId,
            ulong sessionId)
        {
            if (frame.Origin !=
                    VisionFrameOrigin.TransformedReachyEye ||
                !frame.IsObservationEligible ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(
                    VisionResourceKind.ValidityMask))
            {
                throw new InvalidOperationException(
                    "The rear camera did not produce an " +
                    "observation-eligible transformed Reachy-eye frame.");
            }
            if (!string.Equals(
                    frame.Identity.CameraId,
                    cameraId,
                    StringComparison.Ordinal) ||
                frame.Identity.SourceSessionId != sessionId ||
                frame.Identity.SourceSequence == 0UL ||
                frame.Identity.AuthoritativeSequence == 0UL)
            {
                throw new InvalidOperationException(
                    "The transformed frame does not belong to the active " +
                    "real rear-camera session and authoritative state.");
            }
        }

        private static string ReadStringLaunchExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<string>("getStringExtra", name) ??
                string.Empty;
#else
            return string.Empty;
#endif
        }

        private static void SetStage(string stage)
        {
            currentStage = stage;
            Debug.Log(
                "RMA-190 rear-camera/VLM acceptance stage: " +
                stage + ".");
        }

        [Serializable]
        private sealed class Report
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string stage = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public bool real_rear_camera;
            public bool transformed_reachy_eye_frame;
            public bool deterministic_calibration_fixture;
            public bool measured_calibration_claimed;
            public string calibration_provenance = string.Empty;
            public string camera_id = string.Empty;
            public ulong camera_session_id;
            public ulong source_sequence;
            public ulong authoritative_sequence;
            public uint continuity_id;
            public double coverage_fraction;
            public bool external_vlm_fixture;
            public bool fixture_is_live_cloud_evidence;
            public bool production_vlm_transport_and_encoder;
            public bool synthetic_camera_frame_injected;
            public bool raw_camera_payload_persisted;
            public string vlm_response = string.Empty;

            public static Report Success(
                ReachyCameraTextureFrameDescriptor descriptor,
                ReachyVisionFrame frame,
                ReachyCameraCalibrationProfile fixture,
                string response)
            {
                return new Report
                {
                    status = "passed",
                    stage = "completed",
                    device_model = SystemInfo.deviceModel,
                    android_api_level = ReadApiLevel(),
                    real_rear_camera =
                        descriptor.LensFacing ==
                            ReachyDeviceCameraFacing.Rear,
                    transformed_reachy_eye_frame =
                        frame.Origin ==
                            VisionFrameOrigin.TransformedReachyEye,
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance =
                        fixture.Provenance.ToString(),
                    camera_id = descriptor.CameraId,
                    camera_session_id = descriptor.SessionId,
                    source_sequence = frame.Identity.SourceSequence,
                    authoritative_sequence =
                        frame.Identity.AuthoritativeSequence,
                    continuity_id = frame.Identity.ContinuityId,
                    coverage_fraction = frame.Coverage.Fraction,
                    external_vlm_fixture = true,
                    fixture_is_live_cloud_evidence = false,
                    production_vlm_transport_and_encoder = true,
                    synthetic_camera_frame_injected = false,
                    raw_camera_payload_persisted = false,
                    vlm_response = response,
                };
            }

            public static Report Failure(
                string error,
                string stage)
            {
                return new Report
                {
                    status = "failed",
                    error = Bound(error, 4096),
                    stage = Bound(stage, 256),
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance =
                        ReachyCameraCalibrationProvenance
                            .UncalibratedEstimate.ToString(),
                    external_vlm_fixture = true,
                    fixture_is_live_cloud_evidence = false,
                    production_vlm_transport_and_encoder = true,
                    synthetic_camera_frame_injected = false,
                    raw_camera_payload_persisted = false,
                };
            }
        }

        private static int ReadApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version =
                new AndroidJavaClass("android.os.Build$VERSION");
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
            catch (Exception)
            {
                return 0;
            }
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            if (File.Exists(path + ".tmp"))
            {
                File.Delete(path + ".tmp");
            }
        }

        private static void WriteAtomically(
            string path,
            string content)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "The rear-camera/VLM result path has no parent.");
            Directory.CreateDirectory(directory);
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
            string bounded = string.IsNullOrWhiteSpace(value)
                ? "RMA-190 rear-camera/VLM acceptance failed without " +
                    "diagnostics."
                : value;
            return bounded.Length <= maximumCharacters
                ? bounded
                : bounded.Substring(0, maximumCharacters);
        }
    }
}
