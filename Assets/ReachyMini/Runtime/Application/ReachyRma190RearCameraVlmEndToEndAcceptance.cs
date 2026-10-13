#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Performance;
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
    public sealed partial class ReachyRma190RearCameraVlmEndToEndAcceptance :
        MonoBehaviour
    {
        private const int OverallTimeoutMilliseconds = 130_000;
        private const int ReadyPollMilliseconds = 25;
        private const int AdmissionObservationMilliseconds = 100;
        private const int AdmissionTimeoutMilliseconds = 60_000;
        // RMA-181 already requires three consecutive recovery samples before
        // CurrentDecision can become VLM-allowed again. Requiring another
        // three admitted decisions here would add acceptance-only hysteresis
        // that shipping code does not impose. One real policy-admitted sample
        // is sufficient; the production VLM service rechecks admission at the
        // actual call and still fails closed if pressure returns.
        private const int RequiredStableAdmissionSamples = 1;
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
        // A fault must not erase verified physical-frame evidence gathered
        // earlier in this same acceptance invocation. No raw media is retained.
        private Report? observedFrameEvidence;

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
                    currentStage,
                    observedFrameEvidence);
                Debug.LogError(
                    "RMA-190 rear-camera/VLM end-to-end acceptance failed: " +
                    exception,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-190 rear-camera/VLM acceptance was cancelled.",
                    currentStage,
                    observedFrameEvidence);
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

        private async Task<Report> RunAsync()
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

            SetStage("verify-android-loopback-cleartext-policy");
            RequireLoopbackCleartextPolicy(fixtureBaseUrl);

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
            ReachyPriorityDegradationRuntimeDriver priorityDegradation =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyPriorityDegradationRuntimeDriver>(
                        cancellationToken,
                        "priority degradation runtime");

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

                SetStage("wait-production-vlm-admission");
                VlmAdmissionEvidence admission =
                    await WaitForStableVlmAdmissionAsync(
                        priorityDegradation,
                        cancellationToken);

                // Admission evidence is intentionally gathered before this
                // fresh capture. The production VLM service still performs
                // its own fail-closed admission check later, so pressure
                // returning after this point remains a visible failure.
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
                    // Only a successfully validated physical frame contributes
                    // camera/session/continuity evidence to a failed report.
                    observedFrameEvidence = Report.ObservedFrame(
                        descriptor,
                        frame,
                        fixture,
                        admission);

                    SetStage("ask-visual-question-through-production-vlm");
                    observedFrameEvidence.production_vlm_transport_and_encoder = true;
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
                        admission,
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

    }
}
