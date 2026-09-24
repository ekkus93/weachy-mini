#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma190CameraEndToEndAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma190_camera_e2e_acceptance";
        public const string ResultFileName =
            "rma190-camera-end-to-end-acceptance.json";
        public const string ObjectName =
            "ReachyRma190CameraEndToEndAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsRequested() ||
                Object.FindAnyObjectByType<ReachyRma190CameraEndToEndAcceptance>() != null)
            {
                return;
            }

            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<ReachyRma190CameraEndToEndAcceptance>();
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
                    "Could not inspect the RMA-190 camera acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class ReachyRma190CameraEndToEndAcceptance : MonoBehaviour
    {
        private const int OverallTimeoutMilliseconds = 135_000;
        private const int ReadyPollMilliseconds = 25;
        private const double MinimumHeadRotationRadians = 0.015;
        private const long PlanningTimestampNanoseconds = 1_900_000_190L;
        private const string FixtureProfileId =
            "rma190-front-camera-deterministic-fixture";

        private string activePhase = "not-started";

        private void MarkPhase(string phase)
        {
            activePhase = phase;
            Debug.Log("RMA-190 camera acceptance phase: " + phase);
        }

        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma190CameraEndToEndAcceptanceBootstrap.ResultFileName);
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
                        "RMA-190 camera acceptance failed without an exception.");
                report = Report.Failure(exception.ToString());
                Debug.LogError(
                    "RMA-190 camera end-to-end acceptance failed: " +
                    exception,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-190 camera end-to-end acceptance was cancelled during phase: " +
                    activePhase + ".");
                Debug.LogError(
                    "RMA-190 camera end-to-end acceptance was cancelled during phase: " +
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
            using var timeout = new CancellationTokenSource(
                OverallTimeoutMilliseconds);
            CancellationToken cancellationToken = timeout.Token;

            MarkPhase("waiting for shipping host");
            ReachyApplicationHostBehaviour hostBehaviour =
                await WaitForShippingHostAsync(cancellationToken);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance could not resolve the shipping host.");

            MarkPhase("resolving shipping services");
            IReachyBehaviorService behavior =
                host.GetRequiredService<IReachyBehaviorService>(
                    ReachyServiceKind.Behavior);
            IReachyPerceptionService perception =
                host.GetRequiredService<IReachyPerceptionService>(
                    ReachyServiceKind.Perception);
            IReachyPersistenceService persistence =
                host.GetRequiredService<IReachyPersistenceService>(
                    ReachyServiceKind.Persistence);
            if (behavior is not ReachyBaselineBehaviorApplicationService)
            {
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance requires the shipping baseline behavior service.");
            }
            if (perception is not ReachyAndroidPerceptionApplicationService)
            {
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance requires the shipping Android perception service.");
            }
            if (persistence is not ReachySettingsPersistenceApplicationService
                    settingsPersistence)
            {
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance requires the shipping durable-settings service.");
            }

            MarkPhase("waiting for production runtime");
            ReachyProductionAuthoritativeRuntime runtime =
                await WaitForRuntimeAsync(cancellationToken);
            MarkPhase("waiting for camera discovery object");
            ReachyAndroidCameraDiscovery discovery =
                await WaitForObjectAsync<ReachyAndroidCameraDiscovery>(
                    cancellationToken,
                    "camera discovery");
            MarkPhase("waiting for camera acquisition object");
            ReachyAndroidCameraAcquisition acquisition =
                await WaitForObjectAsync<ReachyAndroidCameraAcquisition>(
                    cancellationToken,
                    "camera acquisition");
            ReachyAndroidCameraTextureBridge bridge =
                acquisition.GetComponent<ReachyAndroidCameraTextureBridge>() ??
                throw new InvalidOperationException(
                    "The shipping camera acquisition has no texture bridge.");
            MarkPhase("waiting for perception driver object");
            ReachyAndroidPerceptionDriver driver =
                await WaitForObjectAsync<ReachyAndroidPerceptionDriver>(
                    cancellationToken,
                    "perception driver");

            IReachyApplicationInterruptionParticipant behaviorInterruption =
                behavior as IReachyApplicationInterruptionParticipant ??
                throw new InvalidOperationException(
                    "The shipping behavior service cannot be suspended for camera acceptance.");
            IReachyApplicationInterruptionParticipant perceptionInterruption =
                perception as IReachyApplicationInterruptionParticipant ??
                throw new InvalidOperationException(
                    "The shipping perception service cannot be suspended for deterministic capture.");

            ReachyCameraCalibrationProfile[] originalProfiles =
                CopyProfiles(settingsPersistence.CameraCalibrations.State.Current);
            bool cameraWasActive = acquisition.State.Current.IsActive;
            ReachyCameraFacing previousFacing = acquisition.PreferredFacing;
            bool behaviorPaused = false;
            bool perceptionPaused = false;

            try
            {
                MarkPhase("pausing services for deterministic capture");
                behaviorInterruption.PauseForApplicationInterruption();
                behaviorPaused = true;
                perceptionInterruption.PauseForApplicationInterruption();
                perceptionPaused = true;

                MarkPhase("refreshing camera permission and capabilities");
                discovery.RefreshPermissionAndCapabilities();
                ReachyCameraCapabilitySnapshot capabilities = discovery.State.Current;
                if (capabilities.Permission != ReachyCameraPermissionState.Granted)
                {
                    throw new InvalidOperationException(
                        "RMA-190 front-camera scenario requires granted Android camera permission; state=" +
                        capabilities.Permission + ".");
                }
                if (!HasAvailableFrontCamera(capabilities))
                {
                    throw new InvalidOperationException(
                        "RMA-190 front-camera scenario requires an available front camera with YUV analysis support.");
                }

                if (acquisition.State.Current.IsActive)
                {
                    MarkPhase("stopping prior camera acquisition");
                    acquisition.StopAcquisition();
                    await WaitForCameraStoppedAsync(acquisition, cancellationToken);
                }
                MarkPhase("starting front camera acquisition");
                acquisition.StartPreferred(ReachyCameraFacing.Front);
                MarkPhase("waiting for front camera texture");
                ReachyCameraTextureFrameDescriptor descriptor =
                    await WaitForFrontTextureAsync(
                        acquisition,
                        bridge,
                        cancellationToken);

                MarkPhase("waiting for baseline authoritative state");
                ReachySimAuthoritativeStateFrame baselineState =
                    await WaitForAuthoritativeStateAsync(
                        runtime,
                        minimumSequenceExclusive: 0UL,
                        cancellationToken);
                ReachySimBodyPoseSnapshot baselineCameraPose =
                    FindCameraPose(baselineState);
                MarkPhase("installing deterministic camera calibration fixture");
                ReachyCameraCalibrationProfile fixture =
                    CreateDeterministicFixture(
                        descriptor,
                        baselineCameraPose);
                settingsPersistence.CameraCalibrations.State.ReplaceAll(
                    new[] { fixture });

                MarkPhase("capturing baseline transformed front frame");
                ReachyVisionFrame baselineFrame =
                    await CaptureAcceptanceFrameAsync(
                        driver,
                        cancellationToken);
                FrameEvidence baselineEvidence;
                try
                {
                    MarkPhase("validating baseline transformed front frame");
                    baselineEvidence = RequireTransformedFrontFrame(
                        baselineFrame,
                        descriptor.CameraId,
                        descriptor.SessionId,
                        minimumSourceSequenceExclusive: 0UL,
                        minimumAuthoritativeSequenceExclusive: 0UL,
                        expectedContinuityId: baselineState.ContinuityId);
                }
                finally
                {
                    await baselineFrame.DisposeAsync();
                }

                MarkPhase("building deterministic head-rotation behavior plan");
                ReachyBehaviorTrajectoryPlan plan =
                    BuildCuriosityPlan(baselineState);
                var executor = new ReachyBehaviorTrajectoryExecutor(
                    new ReachyProductionBehaviorControllerTargetSink(runtime),
                    new ReachyBehaviorSystemTrajectoryDelay());
                MarkPhase("executing deterministic head-rotation behavior plan");
                Task<ReachyBehaviorTrajectoryExecutionResult> executionTask =
                    executor.ExecuteAsync(plan, cancellationToken);

                ReachyVisionFrame? rotatedFrame = null;
                ReachySimAuthoritativeStateFrame? rotatedState = null;
                double observedHeadRotation = 0.0;
                ulong lastSequence = baselineState.Sequence;
                MarkPhase("waiting for authoritative head rotation");
                while (!executionTask.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReachySimAuthoritativeStateFrame sample =
                        await WaitForAuthoritativeStateAsync(
                            runtime,
                            lastSequence,
                            cancellationToken);
                    lastSequence = sample.Sequence;
                    if (sample.ContinuityId != baselineState.ContinuityId)
                    {
                        throw new InvalidOperationException(
                            "RMA-190 camera continuity changed during head-rotation replay.");
                    }
                    ReachySimBodyPoseSnapshot sampleCameraPose =
                        FindCameraPose(sample);
                    observedHeadRotation = CameraRotationDeltaRadians(
                        baselineCameraPose,
                        sampleCameraPose);
                    if (observedHeadRotation < MinimumHeadRotationRadians)
                    {
                        continue;
                    }

                    MarkPhase("capturing rotated transformed front frame");
                    rotatedFrame = await CaptureAcceptanceFrameAsync(
                        driver,
                        cancellationToken);
                    rotatedState = sample;
                    break;
                }

                MarkPhase("waiting for behavior trajectory completion");
                ReachyBehaviorTrajectoryExecutionResult execution =
                    await executionTask;
                if (!execution.Completed ||
                    execution.SubmittedFrameCount != plan.Frames.Count)
                {
                    throw new InvalidOperationException(
                        "RMA-190 camera head-rotation plan did not complete: " +
                        execution.Status + " / " + execution.DiagnosticCode + ".");
                }
                if (rotatedFrame == null || rotatedState == null)
                {
                    throw new InvalidOperationException(
                        "RMA-190 camera scenario never observed sufficient authoritative head rotation before the deterministic plan completed.");
                }

                FrameEvidence rotatedEvidence;
                try
                {
                    MarkPhase("validating rotated transformed front frame");
                    rotatedEvidence = RequireTransformedFrontFrame(
                        rotatedFrame,
                        descriptor.CameraId,
                        descriptor.SessionId,
                        baselineEvidence.SourceSequence,
                        baselineEvidence.AuthoritativeSequence,
                        baselineState.ContinuityId);
                }
                finally
                {
                    await rotatedFrame.DisposeAsync();
                }

                if (rotatedEvidence.AuthoritativeSequence < rotatedState.Sequence)
                {
                    throw new InvalidOperationException(
                        "RMA-190 transformed frame did not incorporate the authoritative head-rotation state observed before capture.");
                }

                MarkPhase("returning successful camera acceptance report");
                return Report.Success(
                    descriptor,
                    baselineEvidence,
                    rotatedEvidence,
                    observedHeadRotation,
                    plan.Frames.Count,
                    execution.SubmittedFrameCount,
                    fixture);
            }
            finally
            {
                MarkPhase("restoring camera acceptance state");
                settingsPersistence.CameraCalibrations.State.ReplaceAll(
                    originalProfiles);
                try
                {
                    runtime.ResetNeutral();
                }
                catch (Exception)
                {
                    // Preserve the primary acceptance outcome. The runtime's
                    // own diagnostics remain authoritative for reset failures.
                }

                try
                {
                    if (acquisition.State.Current.IsActive)
                    {
                        acquisition.StopAcquisition();
                        using var cleanupTimeout =
                            new CancellationTokenSource(5_000);
                        await WaitForCameraStoppedAsync(
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
                        "RMA-190 camera acceptance could not restore the prior camera session: " +
                        exception.Message);
                }

                if (perceptionPaused)
                {
                    perceptionInterruption.ResumeAfterApplicationInterruption();
                }
                if (behaviorPaused)
                {
                    behaviorInterruption.ResumeAfterApplicationInterruption();
                }
            }
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-190 front-camera end-to-end acceptance requires an Android player.");
#endif
        }

    }
}
