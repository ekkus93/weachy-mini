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
    public static class ReachyRma154VisualServoAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma154_acceptance";
        public const string ResultFileName =
            "rma154-visual-servo-state.json";
        public const string ObjectName =
            "ReachyRma154VisualServoAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsAcceptanceRequestedFromLaunchIntent() ||
                Object.FindAnyObjectByType<ReachyRma154VisualServoAcceptance>() != null)
            {
                return;
            }

            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<ReachyRma154VisualServoAcceptance>();
        }

        public static bool IsAcceptanceRequestedFromLaunchIntent()
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
                    "Could not inspect the RMA-154 acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class ReachyRma154VisualServoAcceptance : MonoBehaviour
    {
        private const int RuntimeWaitMilliseconds = 60_000;
        private const int OverallAcceptanceTimeoutMilliseconds = 45_000;
        private const int StateAdvanceDelayMilliseconds = 5;
        private const int ResetSettleSampleDelayMilliseconds = 20;
        private const int ResetSettleConsecutiveSamples = 8;
        private const int ImageWidth = 640;
        private const int ImageHeight = 480;
        private const double FocalLengthPixels = 320.0;
        private const double InitialTargetCenterXNormalized = 0.72;
        private const double InitialTargetCenterYNormalized = 0.50;
        private const double TargetWidthNormalized = 0.08;
        private const double TargetHeightNormalized = 0.08;
        private const double MotionThresholdRadians = 1.0e-5;
        private const double ResetSettleMaximumMotionRadians = 5.0e-4;
        private const double ResetSettleMaximumVelocityRadiansPerSecond = 2.0e-2;
        private const string CameraId = "rma154-synthetic-optical-target";
        private const string ProviderId = "rma154-acceptance-tracker";
        private const string LocalTrackId = "face-154";
        private const ulong SourceSessionId = 154UL;

        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma154VisualServoAcceptanceBootstrap.ResultFileName);
            DeleteIfPresent(resultPath);

            Task<Report> task = RunAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Report report;
            if (task.IsFaulted)
            {
                Exception exception =
                    task.Exception?.GetBaseException() ??
                    new InvalidOperationException(
                        "RMA-154 acceptance failed without an exception.");
                report = Report.Failure(exception.Message);
                Debug.LogError(
                    "RMA-154 visual-servo acceptance failed: " +
                    exception.Message,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-154 visual-servo acceptance was cancelled.");
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
            ReachyProductionAuthoritativeRuntime runtime =
                await WaitForRuntimeAsync();
            using var overallTimeout = new CancellationTokenSource(
                OverallAcceptanceTimeoutMilliseconds);
            CancellationToken cancellationToken = overallTimeout.Token;

            ReachySimAuthoritativeStateFrame beforeReset =
                await WaitForAuthoritativeStateAsync(
                        runtime,
                        minimumSequenceExclusive: 0UL,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            uint previousContinuityId = beforeReset.ContinuityId;

            ReachySimulationControlResult reset = runtime.ResetNeutral();
            if (!reset.IsSuccess)
            {
                throw new InvalidOperationException(
                    "RMA-154 neutral reset failed: " +
                    reset.Error.Code + ": " + reset.Error.Message);
            }

            ReachySimAuthoritativeStateFrame baseline =
                await WaitForResetStateAsync(
                        runtime,
                        previousContinuityId,
                        cancellationToken)
                    .ConfigureAwait(false);
            ReachyBehaviorMotionSnapshot baselineMotion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                    baseline);

            var worldModel = new BoundedWorldModel(
                WorldModelPolicy.CreateMobileDefault());
            var syntheticFeedback = new SyntheticOpticalTargetFeedbackSource(
                runtime,
                worldModel);
            ReachyVisualServoFeedbackSample initialSample =
                await syntheticFeedback.InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);
            WorldEntitySnapshot initialEntity =
                RequireOnlyEntity(initialSample.WorldSnapshot);
            double initialHorizontalError =
                initialEntity.Bounds.CenterX - 0.5;
            if (Math.Abs(initialHorizontalError) <= 0.06)
            {
                throw new InvalidOperationException(
                    "RMA-154 acceptance target did not start outside the center tolerance.");
            }

            var planner = new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            var executor = new ReachyBehaviorTrajectoryExecutor(
                new ReachyProductionBehaviorControllerTargetSink(runtime),
                new ReachyBehaviorSystemTrajectoryDelay());
            var loop = new ReachyVisualServoGazeLoop(
                planner,
                executor,
                syntheticFeedback,
                new ReachyVisualServoSystemPollDelay(),
                ReachyVisualServoPolicy.CreateMobileDefault());

            ReachyVisualServoResult result = await loop.CenterAsync(
                    initialEntity.EntityId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Centered)
            {
                throw new InvalidOperationException(
                    "RMA-154 visual-servo loop did not center: " +
                    result.Status + " / " + result.DiagnosticCode);
            }
            if (result.AdjustmentCount <= 0 || result.SubmittedFrameCount <= 0)
            {
                throw new InvalidOperationException(
                    "RMA-154 centered without a bounded physical adjustment.");
            }

            ReachySimAuthoritativeStateFrame finalState =
                await WaitForAuthoritativeStateAsync(
                        runtime,
                        baseline.Sequence,
                        cancellationToken)
                    .ConfigureAwait(false);
            ReachyBehaviorMotionSnapshot finalMotion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                    finalState);
            double maximumPhysicalMotion = MaximumHeadBodyMotion(
                baselineMotion,
                finalMotion);
            if (maximumPhysicalMotion < MotionThresholdRadians)
            {
                throw new InvalidOperationException(
                    "RMA-154 reported centering without authoritative MuJoCo head/body motion.");
            }
            if (!syntheticFeedback.ObservedPostMotionTransformedFrame)
            {
                throw new InvalidOperationException(
                    "RMA-154 did not observe a transformed frame generated after physical motion.");
            }

            double finalHorizontalError = result.HorizontalErrorNormalized;
            double finalVerticalError = result.VerticalErrorNormalized;
            if (Math.Abs(finalHorizontalError) >=
                Math.Abs(initialHorizontalError))
            {
                throw new InvalidOperationException(
                    "RMA-154 physical feedback did not reduce horizontal tracking error.");
            }

            ReachySimulationControlResult finalReset = runtime.ResetNeutral();
            if (!finalReset.IsSuccess)
            {
                throw new InvalidOperationException(
                    "RMA-154 final neutral reset failed: " +
                    finalReset.Error.Code + ": " +
                    finalReset.Error.Message);
            }

            return Report.Success(
                initialSample.AuthoritativeStateSequence,
                finalState.Sequence,
                initialEntity.Bounds.CenterX,
                initialEntity.Bounds.CenterY,
                finalHorizontalError,
                finalVerticalError,
                result.AdjustmentCount,
                result.SubmittedFrameCount,
                maximumPhysicalMotion,
                syntheticFeedback.ProducedFrameCount,
                syntheticFeedback.ObservedPhysicalMotion,
                syntheticFeedback.ObservedPostMotionTransformedFrame);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-154 physical acceptance requires an Android player build.");
#endif
        }

        private static async Task<ReachyProductionAuthoritativeRuntime>
            WaitForRuntimeAsync()
        {
            int waitedMilliseconds = 0;
            while (waitedMilliseconds < RuntimeWaitMilliseconds)
            {
                ReachyProductionAuthoritativeRuntime? runtime =
                    Object.FindAnyObjectByType<
                        ReachyProductionAuthoritativeRuntime>();
                if (runtime != null)
                {
                    if (runtime.Status ==
                        ReachyProductionRuntimeStatus.Faulted)
                    {
                        throw new InvalidOperationException(
                            "Production runtime faulted during RMA-154 startup: " +
                            runtime.Fault);
                    }
                    if (runtime.Status ==
                        ReachyProductionRuntimeStatus.Running)
                    {
                        return runtime;
                    }
                }

                await Task.Delay(50);
                waitedMilliseconds += 50;
            }

            throw new TimeoutException(
                "Timed out waiting for the production authoritative runtime for RMA-154.");
        }

        private static async Task<ReachySimAuthoritativeStateFrame>
            WaitForAuthoritativeStateAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                ulong minimumSequenceExclusive,
                CancellationToken cancellationToken)
        {
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame frame))
            {
                throw new InvalidOperationException(
                    "RMA-154 could not allocate an authoritative state frame.");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runtime.TryCaptureLatestAuthoritativeState(frame) &&
                    frame.Sequence > minimumSequenceExclusive)
                {
                    return frame;
                }
                await Task.Delay(
                        StateAdvanceDelayMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<ReachySimAuthoritativeStateFrame>
            WaitForResetStateAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                uint previousContinuityId,
                CancellationToken cancellationToken)
        {
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame frame))
            {
                throw new InvalidOperationException(
                    "RMA-154 could not allocate a reset-state frame.");
            }

            ReachyBehaviorMotionSnapshot? previousMotion = null;
            uint? resetContinuityId = null;
            ulong lastSequence = 0UL;
            int stableSampleCount = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!runtime.TryCaptureLatestAuthoritativeState(frame) ||
                    frame.ContinuityId == previousContinuityId ||
                    frame.Sequence == 0UL)
                {
                    await Task.Delay(
                            StateAdvanceDelayMilliseconds,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (resetContinuityId.HasValue &&
                    frame.ContinuityId != resetContinuityId.Value)
                {
                    throw new InvalidOperationException(
                        "RMA-154 reset continuity changed while waiting for the neutral mechanism to settle.");
                }
                resetContinuityId ??= frame.ContinuityId;

                if (frame.Sequence <= lastSequence)
                {
                    await Task.Delay(
                            StateAdvanceDelayMilliseconds,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                ReachyBehaviorMotionSnapshot currentMotion =
                    ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                        frame);
                bool settled = previousMotion != null &&
                    MaximumHeadBodyMotion(previousMotion, currentMotion) <=
                        ResetSettleMaximumMotionRadians &&
                    MaximumHeadBodyVelocity(currentMotion) <=
                        ResetSettleMaximumVelocityRadiansPerSecond;
                stableSampleCount = settled
                    ? checked(stableSampleCount + 1)
                    : 0;
                if (stableSampleCount >= ResetSettleConsecutiveSamples)
                {
                    return frame;
                }

                previousMotion = currentMotion;
                lastSequence = frame.Sequence;
                await Task.Delay(
                        ResetSettleSampleDelayMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static WorldEntitySnapshot RequireOnlyEntity(
            WorldModelSnapshot snapshot)
        {
            if (snapshot.Entities.Count != 1)
            {
                throw new InvalidOperationException(
                    "RMA-154 acceptance world model did not contain exactly one target.");
            }
            return snapshot.Entities[0];
        }

        private static double MaximumHeadBodyMotion(
            ReachyBehaviorMotionSnapshot before,
            ReachyBehaviorMotionSnapshot after)
        {
            double maximum = 0.0;
            for (int index = ReachyBehaviorPlannerActuators.BodyYaw;
                index <= ReachyBehaviorPlannerActuators.Stewart6;
                ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        after.PositionsRadians[index] -
                        before.PositionsRadians[index]));
            }
            return maximum;
        }

        private static double MaximumHeadBodyVelocity(
            ReachyBehaviorMotionSnapshot motion)
        {
            double maximum = 0.0;
            for (int index = ReachyBehaviorPlannerActuators.BodyYaw;
                index <= ReachyBehaviorPlannerActuators.Stewart6;
                ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        motion.VelocitiesRadiansPerSecond[index]));
            }
            return maximum;
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            string temporaryPath = path + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "RMA-154 result path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporaryPath, path);
        }

    }
}
