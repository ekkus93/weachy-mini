#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma190RearVisualEndToEndAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma190_rear_visual_e2e_acceptance";
        public const string ResultFileName =
            "rma190-rear-visual-end-to-end-acceptance.json";
        public const string ObjectName =
            "ReachyRma190RearVisualEndToEndAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsRequested() ||
                Object.FindAnyObjectByType<ReachyRma190RearVisualEndToEndAcceptance>() != null)
            {
                return;
            }

            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<ReachyRma190RearVisualEndToEndAcceptance>();
        }

        private static bool IsRequested()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
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
                    "Could not inspect the RMA-190 rear-visual launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyRma190RearVisualEndToEndAcceptance : MonoBehaviour
    {
        private const int OverallTimeoutMilliseconds = 135_000;
        private const int ReadyPollMilliseconds = 25;
        private const string FixtureProfileId =
            "rma190-rear-camera-deterministic-fixture";
        private const string VisualPrompt =
            "What is visible in this rear-camera scene? Answer briefly.";
        private const string RequestId = "rma190-rear-visual-question";

        private string activePhase = "not-started";

        private void MarkPhase(string phase)
        {
            activePhase = phase;
            Debug.Log("RMA-190 rear-visual acceptance phase: " + phase);
        }

        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma190RearVisualEndToEndAcceptanceBootstrap.ResultFileName);
            MarkPhase("initializing result file");
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
                        "RMA-190 rear-visual acceptance failed without an exception.");
                report = Report.Failure(exception.ToString());
                Debug.LogError(
                    "RMA-190 rear-visual acceptance failed: " + exception,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-190 rear-visual acceptance was cancelled during phase: " +
                    activePhase + ".");
                Debug.LogError(
                    "RMA-190 rear-visual acceptance was cancelled during phase: " +
                    activePhase,
                    this);
            }
            else
            {
                report = task.Result;
            }

            MarkPhase("writing result file");
            WriteAtomically(
                resultPath,
                JsonUtility.ToJson(report, prettyPrint: true));
            Destroy(gameObject);
        }

        private async Task<Report> RunAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var timeout =
                new CancellationTokenSource(OverallTimeoutMilliseconds);
            CancellationToken cancellationToken = timeout.Token;
            string deviceModel = SystemInfo.deviceModel;
            int androidApiLevel = ReadApiLevel();

            MarkPhase("waiting for shipping host");
            ReachyApplicationHostBehaviour hostBehaviour =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForShippingHostAsync(cancellationToken);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException(
                    "RMA-190 rear-visual acceptance could not resolve the shipping host.");

            MarkPhase("resolving shipping services");
            IReachyPerceptionService perception =
                host.GetRequiredService<IReachyPerceptionService>(
                    ReachyServiceKind.Perception);
            IReachyPersistenceService persistence =
                host.GetRequiredService<IReachyPersistenceService>(
                    ReachyServiceKind.Persistence);
            if (perception is not ReachyAndroidPerceptionApplicationService
                    androidPerception)
            {
                throw new InvalidOperationException(
                    "RMA-190 rear-visual acceptance requires the shipping Android perception service.");
            }
            if (perception is not ICloudVlmProviderCapability vlmCapability)
            {
                throw new InvalidOperationException(
                    "RMA-190 rear-visual acceptance requires the shipping optional VLM capability.");
            }
            if (persistence is not ReachySettingsPersistenceApplicationService
                    settingsPersistence)
            {
                throw new InvalidOperationException(
                    "RMA-190 rear-visual acceptance requires the shipping durable-settings service.");
            }

            MarkPhase("waiting for production runtime");
            ReachyProductionAuthoritativeRuntime runtime =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForRuntimeAsync(cancellationToken);
            MarkPhase("waiting for camera discovery object");
            ReachyAndroidCameraDiscovery discovery =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidCameraDiscovery>(
                        cancellationToken,
                        "camera discovery");
            MarkPhase("waiting for camera acquisition object");
            ReachyAndroidCameraAcquisition acquisition =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidCameraAcquisition>(
                        cancellationToken,
                        "camera acquisition");
            ReachyAndroidCameraTextureBridge bridge =
                acquisition.GetComponent<ReachyAndroidCameraTextureBridge>() ??
                throw new InvalidOperationException(
                    "The shipping camera acquisition has no texture bridge.");
            MarkPhase("waiting for perception driver object");
            ReachyAndroidPerceptionDriver driver =
                await ReachyRma190CameraEndToEndAcceptance
                    .WaitForObjectAsync<ReachyAndroidPerceptionDriver>(
                        cancellationToken,
                        "perception driver");
            IReachyApplicationInterruptionParticipant perceptionInterruption =
                androidPerception;

            ReachyCameraCalibrationProfile[] originalProfiles =
                ReachyRma190CameraEndToEndAcceptance.CopyProfiles(
                    settingsPersistence.CameraCalibrations.State.Current);
            bool cameraWasActive = acquisition.State.Current.IsActive;
            ReachyCameraFacing previousFacing = acquisition.PreferredFacing;
            bool perceptionPaused = false;

            try
            {
                MarkPhase("pausing perception for deterministic rear capture");
                perceptionInterruption.PauseForApplicationInterruption();
                perceptionPaused = true;

                MarkPhase("refreshing camera permission and capabilities");
                discovery.RefreshPermissionAndCapabilities();
                ReachyCameraCapabilitySnapshot capabilities =
                    discovery.State.Current;
                if (capabilities.Permission != ReachyCameraPermissionState.Granted)
                {
                    throw new InvalidOperationException(
                        "RMA-190 rear-camera scenario requires granted Android camera permission; state=" +
                        capabilities.Permission + ".");
                }
                if (!HasAvailableRearCamera(capabilities))
                {
                    throw new InvalidOperationException(
                        "RMA-190 rear-camera scenario requires an available rear camera with YUV analysis support.");
                }

                if (acquisition.State.Current.IsActive)
                {
                    MarkPhase("stopping prior camera acquisition");
                    acquisition.StopAcquisition();
                    await ReachyRma190CameraEndToEndAcceptance
                        .WaitForCameraStoppedAsync(
                            acquisition,
                            cancellationToken);
                }

                MarkPhase("starting rear camera acquisition");
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                MarkPhase("waiting for rear camera texture");
                ReachyCameraTextureFrameDescriptor descriptor =
                    await WaitForRearTextureAsync(
                        acquisition,
                        bridge,
                        cancellationToken);

                MarkPhase("waiting for baseline authoritative state");
                ReachySimAuthoritativeStateFrame baselineState =
                    await ReachyRma190CameraEndToEndAcceptance
                        .WaitForAuthoritativeStateAsync(
                            runtime,
                            minimumSequenceExclusive: 0UL,
                            cancellationToken);
                ReachySimBodyPoseSnapshot baselineCameraPose =
                    ReachyRma190CameraEndToEndAcceptance
                        .FindCameraPose(baselineState);
                MarkPhase("installing deterministic rear camera calibration fixture");
                ReachyCameraCalibrationProfile fixture =
                    ReachyRma190CameraEndToEndAcceptance
                        .CreateDeterministicFixture(
                            descriptor,
                            baselineCameraPose,
                            ReachyDeviceCameraFacing.Rear,
                            FixtureProfileId,
                            mirrorHorizontally: false,
                            "Deterministic RMA-190 rear-camera reprojection fixture; not a measured device calibration.");
                settingsPersistence.CameraCalibrations.State.ReplaceAll(
                    new[] { fixture });

                MarkPhase("capturing transformed rear frame");
                ReachyVisionFrame frame =
                    await CaptureRearAcceptanceFrameAsync(
                        driver,
                        cancellationToken);
                try
                {
                    MarkPhase("validating transformed rear frame");
                    ValidateRearFrame(
                        frame,
                        descriptor,
                        baselineState.ContinuityId);

                    MarkPhase("running optional VLM visual question");
                    VisionLanguageResult vlm = await vlmCapability.AnalyzeSceneAsync(
                        frame,
                        VisualPrompt,
                        RequestId,
                        cancellationToken);
                    if (vlm.Status != VisionOperationStatus.Succeeded &&
                        vlm.Status != VisionOperationStatus.Unavailable)
                    {
                        throw new InvalidOperationException(
                            "RMA-190 optional rear-camera VLM path failed: " +
                            vlm.Status + " / " + vlm.Diagnostic);
                    }
                    if (vlm.Succeeded &&
                        string.IsNullOrWhiteSpace(vlm.Text))
                    {
                        throw new InvalidOperationException(
                            "RMA-190 rear-camera VLM reported success without response text.");
                    }

                    MarkPhase("returning successful rear-visual report");
                    return Report.Success(
                        descriptor,
                        frame,
                        fixture,
                        vlm,
                        deviceModel,
                        androidApiLevel);
                }
                finally
                {
                    await frame.DisposeAsync();
                }
            }
            finally
            {
                settingsPersistence.CameraCalibrations.State.ReplaceAll(
                    originalProfiles);
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
                            previousFacing == ReachyCameraFacing.Unconfigured
                                ? ReachyCameraFacing.Rear
                                : previousFacing);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "RMA-190 rear-visual acceptance could not restore the prior camera session: " +
                        exception.Message);
                }

                if (perceptionPaused)
                {
                    perceptionInterruption.ResumeAfterApplicationInterruption();
                }
            }
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-190 rear-camera visual acceptance requires an Android player.");
#endif
        }

        private static bool HasAvailableRearCamera(
            ReachyCameraCapabilitySnapshot capabilities)
        {
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (camera.Facing == ReachyDeviceCameraFacing.Rear &&
                    camera.Availability == ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<ReachyVisionFrame> CaptureRearAcceptanceFrameAsync(
            ReachyAndroidPerceptionDriver driver,
            CancellationToken cancellationToken)
        {
            string phasePrefix = activePhase;
            string lastDiagnostic = string.Empty;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyVisionFrame? frame =
                    await driver.CaptureFrameForAcceptanceAsync(cancellationToken);
                if (frame != null)
                {
                    return frame;
                }

                ReachyPerceptionServiceSnapshot snapshot = driver.Snapshot;
                string diagnostic =
                    phasePrefix + "; last perception state=" +
                    snapshot.ExecutionState + "; message=" +
                    snapshot.StatusMessage + "; revision=" +
                    snapshot.Revision;
                if (!string.Equals(
                        diagnostic,
                        lastDiagnostic,
                        StringComparison.Ordinal))
                {
                    MarkPhase(diagnostic);
                    lastDiagnostic = diagnostic;
                }

                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
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
                ReachyCameraAcquisitionSnapshot state =
                    acquisition.State.Current;
                if (state.State == ReachyCameraAcquisitionState.Faulted ||
                    state.State == ReachyCameraAcquisitionState.Unavailable ||
                    state.State == ReachyCameraAcquisitionState.PermissionRevoked)
                {
                    throw new InvalidOperationException(
                        "Rear camera acquisition failed: " +
                        state.State + " / " + state.Message);
                }

                ReachyCameraTextureFrameDescriptor? frame =
                    bridge.Current.Frame;
                if (state.State == ReachyCameraAcquisitionState.Running &&
                    bridge.Current.HasTexture &&
                    frame != null &&
                    frame.LensFacing == ReachyDeviceCameraFacing.Rear &&
                    frame.SessionId == state.SessionId)
                {
                    return frame;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        private static void ValidateRearFrame(
            ReachyVisionFrame frame,
            ReachyCameraTextureFrameDescriptor descriptor,
            uint expectedContinuityId)
        {
            if (frame.Origin != VisionFrameOrigin.TransformedReachyEye ||
                !frame.IsObservationEligible ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(VisionResourceKind.ValidityMask))
            {
                throw new InvalidOperationException(
                    "RMA-190 rear-camera capture did not produce an observation-eligible TransformedReachyEye frame with a validity mask.");
            }
            if (!string.Equals(
                    frame.Identity.CameraId,
                    descriptor.CameraId,
                    StringComparison.Ordinal) ||
                frame.Identity.SourceSessionId != descriptor.SessionId ||
                frame.Identity.SourceSequence == 0UL ||
                frame.Identity.AuthoritativeSequence == 0UL ||
                frame.Identity.ContinuityId != expectedContinuityId)
            {
                throw new InvalidOperationException(
                    "RMA-190 rear transformed-frame identity does not match the active rear-camera session and authoritative continuity.");
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public bool real_rear_camera;
            public bool transformed_reachy_eye_frame;
            public bool validity_mask_present;
            public bool deterministic_calibration_fixture;
            public bool measured_calibration_claimed;
            public string calibration_provenance = string.Empty;
            public string camera_id = string.Empty;
            public ulong camera_session_id;
            public ulong source_sequence;
            public ulong authoritative_sequence;
            public uint continuity_id;
            public double coverage_fraction;
            public string coverage_state = string.Empty;
            public string visual_question = string.Empty;
            public string vlm_status = string.Empty;
            public bool vlm_succeeded;
            public bool vlm_optional_unavailable;
            public int vlm_response_characters;
            public bool synthetic_camera_frame_injected;
            public bool cloud_credentials_injected;

            public static Report Success(
                ReachyCameraTextureFrameDescriptor descriptor,
                ReachyVisionFrame frame,
                ReachyCameraCalibrationProfile fixture,
                VisionLanguageResult vlm,
                string deviceModel,
                int androidApiLevel)
            {
                return new Report
                {
                    status = "passed",
                    device_model = deviceModel,
                    android_api_level = androidApiLevel,
                    real_rear_camera =
                        descriptor.LensFacing == ReachyDeviceCameraFacing.Rear,
                    transformed_reachy_eye_frame =
                        frame.Origin == VisionFrameOrigin.TransformedReachyEye,
                    validity_mask_present =
                        frame.Resources.HasResource(VisionResourceKind.ValidityMask),
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance = fixture.Provenance.ToString(),
                    camera_id = descriptor.CameraId,
                    camera_session_id = descriptor.SessionId,
                    source_sequence = frame.Identity.SourceSequence,
                    authoritative_sequence =
                        frame.Identity.AuthoritativeSequence,
                    continuity_id = frame.Identity.ContinuityId,
                    coverage_fraction = frame.Coverage.Fraction,
                    coverage_state = frame.Coverage.State.ToString(),
                    visual_question = VisualPrompt,
                    vlm_status = vlm.Status.ToString(),
                    vlm_succeeded = vlm.Succeeded,
                    vlm_optional_unavailable =
                        vlm.Status == VisionOperationStatus.Unavailable,
                    vlm_response_characters =
                        vlm.Text?.Length ?? 0,
                    synthetic_camera_frame_injected = false,
                    cloud_credentials_injected = false,
                };
            }

            public static Report Failure(string error)
            {
                return new Report
                {
                    status = "failed",
                    error = Bound(error, 4096),
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance =
                        ReachyCameraCalibrationProvenance
                            .UncalibratedEstimate.ToString(),
                    synthetic_camera_frame_injected = false,
                    cloud_credentials_injected = false,
                };
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

        private static void WriteAtomically(string path, string content)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "RMA-190 rear-visual result path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string bounded = string.IsNullOrWhiteSpace(value)
                ? "RMA-190 rear-visual acceptance failed without diagnostics."
                : value;
            return bounded.Length <= maximumCharacters
                ? bounded
                : bounded.Substring(0, maximumCharacters);
        }
    }
}
