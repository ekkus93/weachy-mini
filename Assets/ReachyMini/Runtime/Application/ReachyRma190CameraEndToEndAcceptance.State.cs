#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma190CameraEndToEndAcceptance
    {
        private static async Task<ReachyApplicationHostBehaviour>
            WaitForShippingHostAsync(
                CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < RuntimeWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyApplicationHostBehaviour? host =
                    Object.FindAnyObjectByType<
                        ReachyApplicationHostBehaviour>();
                if (host?.Host != null)
                {
                    return host;
                }
                await Task.Delay(50, cancellationToken)
                    .ConfigureAwait(true);
                waited += 50;
            }
            throw new TimeoutException(
                "Timed out waiting for the shipping application host.");
        }

        private static async Task<ReachyProductionAuthoritativeRuntime>
            WaitForRuntimeAsync(
                CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < RuntimeWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyProductionAuthoritativeRuntime? runtime =
                    Object.FindAnyObjectByType<
                        ReachyProductionAuthoritativeRuntime>();
                if (runtime?.Status ==
                    ReachyProductionRuntimeStatus.Running)
                {
                    return runtime;
                }
                if (runtime?.Status ==
                    ReachyProductionRuntimeStatus.Faulted)
                {
                    throw new InvalidOperationException(
                        "Production authoritative runtime faulted: " +
                        runtime.Fault);
                }
                await Task.Delay(50, cancellationToken)
                    .ConfigureAwait(true);
                waited += 50;
            }
            throw new TimeoutException(
                "Timed out waiting for the production runtime.");
        }

        private static async Task<ReachySimAuthoritativeStateFrame>
            WaitForStateAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                ulong minimumSequenceExclusive,
                CancellationToken cancellationToken)
        {
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame frame))
            {
                throw new InvalidOperationException(
                    "Could not allocate authoritative state.");
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runtime.TryCaptureLatestAuthoritativeState(frame) &&
                    frame.Sequence > minimumSequenceExclusive)
                {
                    return CopyFrame(frame);
                }
                await Task.Delay(5, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<ReachySimAuthoritativeStateFrame>
            WaitForNeutralAfterResetAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                uint previousContinuityId,
                CancellationToken cancellationToken)
        {
            ulong lastSequence = 0UL;
            uint? continuityId = null;
            ReachyBehaviorMotionSnapshot? previousMotion = null;
            int stableSamples = 0;
            while (true)
            {
                ReachySimAuthoritativeStateFrame state =
                    await WaitForStateAsync(
                            runtime,
                            lastSequence,
                            cancellationToken)
                        .ConfigureAwait(false);
                lastSequence = state.Sequence;
                if (state.ContinuityId == previousContinuityId)
                {
                    continue;
                }
                if (continuityId.HasValue &&
                    state.ContinuityId != continuityId.Value)
                {
                    throw new InvalidOperationException(
                        "Continuity changed again while waiting " +
                        "for neutral reset to settle.");
                }
                continuityId ??= state.ContinuityId;
                ReachyBehaviorMotionSnapshot motion =
                    ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                        state);
                bool settled =
                    previousMotion != null &&
                    MaximumMotion(previousMotion, motion) <=
                        ResetSettleMaximumMotionRadians &&
                    MaximumVelocity(motion) <=
                        ResetSettleMaximumVelocityRadiansPerSecond;
                stableSamples = settled
                    ? checked(stableSamples + 1)
                    : 0;
                if (stableSamples >= ResetSettleConsecutiveSamples)
                {
                    return state;
                }
                previousMotion = motion;
                await Task.Delay(
                        ResetSettleSampleDelayMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static double MaximumMotion(
            ReachyBehaviorMotionSnapshot left,
            ReachyBehaviorMotionSnapshot right)
        {
            double maximum = 0.0;
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        right.PositionsRadians[index] -
                        left.PositionsRadians[index]));
            }
            return maximum;
        }

        private static double MaximumVelocity(
            ReachyBehaviorMotionSnapshot motion)
        {
            double maximum = 0.0;
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        motion.VelocitiesRadiansPerSecond[index]));
            }
            return maximum;
        }

        private static ReachySimAuthoritativeStateFrame CopyFrame(
            ReachySimAuthoritativeStateFrame source)
        {
            var copy = new ReachySimAuthoritativeStateFrame(
                source.Layout)
            {
                Sequence = source.Sequence,
                SimulationTime = source.SimulationTime,
                ContinuityId = source.ContinuityId,
                JointCount = source.JointCount,
                ContactCount = source.ContactCount,
                HealthFlags = source.HealthFlags,
                CalibrationProfileId = source.CalibrationProfileId,
                WarningCount = source.WarningCount,
                ConstraintCount = source.ConstraintCount,
                EqualityConstraintCount =
                    source.EqualityConstraintCount,
                MaximumConstraintResidual =
                    source.MaximumConstraintResidual,
                MaximumEqualityConstraintResidual =
                    source.MaximumEqualityConstraintResidual,
            };
            for (int index = 0; index < source.QposCount; ++index)
            {
                copy.QposStorage[index] = source.GetQpos(index);
            }
            for (int index = 0; index < source.QvelCount; ++index)
            {
                copy.QvelStorage[index] = source.GetQvel(index);
            }
            for (int index = 0;
                index < source.ActuatorObservationCount;
                ++index)
            {
                copy.SetActuatorObservation(
                    index,
                    source.GetActuatorObservation(index));
            }
            for (int index = 0; index < source.BodyPoseCount; ++index)
            {
                copy.SetBodyPose(
                    index,
                    source.GetBodyPose(index));
            }
            return copy;
        }

        private static ReachySimBodyPoseSnapshot FindCameraPose(
            ReachySimAuthoritativeStateFrame state)
        {
            int match = -1;
            for (int index = 0;
                index < state.BodyPoseCount;
                ++index)
            {
                if (state.GetBodyPose(index).BodyId !=
                    ReachyCameraMujocoOpticalBinding
                        .CanonicalCameraBodyId)
                {
                    continue;
                }
                if (match >= 0)
                {
                    throw new InvalidOperationException(
                        "The authoritative model contains duplicate " +
                        "canonical camera bodies.");
                }
                match = index;
            }
            if (match < 0)
            {
                throw new InvalidOperationException(
                    "The authoritative canonical camera body " +
                    "is missing.");
            }
            return state.GetBodyPose(match);
        }

        private static ReachyBehaviorTrajectoryFrame
            SelectMaximumDisplacementFrame(
                ReachyBehaviorTrajectoryPlan plan,
                ReachyBehaviorMotionSnapshot baseline)
        {
            ReachyBehaviorTrajectoryFrame? best = null;
            double bestDistance = -1.0;
            foreach (ReachyBehaviorTrajectoryFrame frame in plan.Frames)
            {
                double distance = 0.0;
                for (int index = 0;
                    index < ReachyBehaviorPlannerActuators.Count;
                    ++index)
                {
                    distance = Math.Max(
                        distance,
                        Math.Abs(
                            frame.TargetPositionsRadians[index] -
                            baseline.PositionsRadians[index]));
                }
                if (distance > bestDistance)
                {
                    best = frame;
                    bestDistance = distance;
                }
            }
            return best ?? throw new InvalidOperationException(
                "Curiosity gesture did not contain any " +
                "trajectory frames.");
        }

        private static async Task<(
            ReachySimAuthoritativeStateFrame state,
            double radians)> WaitForCameraBodyRotationAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                ReachyQuaternionD baseline,
                ulong minimumSequenceExclusive,
                uint continuityId,
                CancellationToken cancellationToken)
        {
            ulong lastSequence = minimumSequenceExclusive;
            while (true)
            {
                ReachySimAuthoritativeStateFrame state =
                    await WaitForStateAsync(
                            runtime,
                            lastSequence,
                            cancellationToken)
                        .ConfigureAwait(false);
                lastSequence = state.Sequence;
                if (state.ContinuityId != continuityId)
                {
                    throw new InvalidOperationException(
                        "Continuity changed while waiting for " +
                        "RMA-190 camera-body rotation.");
                }
                ReachySimBodyPoseSnapshot pose =
                    FindCameraPose(state);
                var current = new ReachyQuaternionD(
                    pose.QuaternionX,
                    pose.QuaternionY,
                    pose.QuaternionZ,
                    pose.QuaternionW);
                double radians =
                    QuaternionAngularDistance(
                        baseline,
                        current);
                if (radians >= MinimumCameraRotationRadians)
                {
                    return (state, radians);
                }
            }
        }

        private static double QuaternionAngularDistance(
            ReachyQuaternionD left,
            ReachyQuaternionD right)
        {
            double dot = Math.Abs(
                left.X * right.X +
                left.Y * right.Y +
                left.Z * right.Z +
                left.W * right.W);
            dot = Math.Min(1.0, Math.Max(0.0, dot));
            return 2.0 * Math.Acos(dot);
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

        private static bool ReadBooleanLaunchExtra(
            string name,
            bool defaultValue)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>("getIntent");
            return intent != null &&
                intent.Call<bool>(
                    "getBooleanExtra",
                    name,
                    defaultValue);
#else
            return defaultValue;
#endif
        }
    }
}
