#nullable enable

using System;
using System.Globalization;
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
    internal sealed partial class ReachyRma190CoreEndToEndAcceptance
    {
        private static async Task<ReachyApplicationHostBehaviour>
            WaitForShippingHostAsync(CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < RuntimeWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyApplicationHostBehaviour? host =
                    Object.FindAnyObjectByType<ReachyApplicationHostBehaviour>();
                if (host?.Host != null)
                {
                    return host;
                }
                await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                waited += 50;
            }
            throw new TimeoutException(
                "Timed out waiting for the shipping application host for RMA-190.");
        }

        private static async Task<ReachyProductionAuthoritativeRuntime>
            WaitForRuntimeAsync(CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < RuntimeWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyProductionAuthoritativeRuntime? runtime =
                    Object.FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>();
                if (runtime != null)
                {
                    if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                    {
                        throw new InvalidOperationException(
                            "Production authoritative runtime faulted: " +
                            runtime.Fault);
                    }
                    if (runtime.Status == ReachyProductionRuntimeStatus.Running)
                    {
                        return runtime;
                    }
                }
                await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                waited += 50;
            }
            throw new TimeoutException(
                "Timed out waiting for the production authoritative runtime for RMA-190.");
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
                    "RMA-190 could not allocate an authoritative state frame.");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runtime.TryCaptureLatestAuthoritativeState(frame) &&
                    frame.Sequence > minimumSequenceExclusive)
                {
                    return CopyFrame(frame);
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
                    "RMA-190 could not allocate a reset-state frame.");
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
                        "RMA-190 reset continuity changed while waiting for neutral to settle.");
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
                    ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(frame);
                bool settled = previousMotion != null &&
                    MaximumActuatorMotion(previousMotion, currentMotion) <=
                        ResetSettleMaximumMotionRadians &&
                    MaximumActuatorVelocity(currentMotion) <=
                        ResetSettleMaximumVelocityRadiansPerSecond;
                stableSampleCount = settled
                    ? checked(stableSampleCount + 1)
                    : 0;
                if (stableSampleCount >= ResetSettleConsecutiveSamples)
                {
                    return CopyFrame(frame);
                }

                previousMotion = currentMotion;
                lastSequence = frame.Sequence;
                await Task.Delay(
                        ResetSettleSampleDelayMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static ReachySimAuthoritativeStateFrame CopyFrame(
            ReachySimAuthoritativeStateFrame source)
        {
            var copy = new ReachySimAuthoritativeStateFrame(source.Layout)
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
                EqualityConstraintCount = source.EqualityConstraintCount,
                MaximumConstraintResidual = source.MaximumConstraintResidual,
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
            for (int index = 0; index < source.ActuatorObservationCount; ++index)
            {
                copy.SetActuatorObservation(
                    index,
                    source.GetActuatorObservation(index));
            }
            for (int index = 0; index < source.BodyPoseCount; ++index)
            {
                copy.SetBodyPose(index, source.GetBodyPose(index));
            }
            return copy;
        }

        private static void ValidateAuthoritativeLayout(
            ReachySimAuthoritativeStateFrame frame)
        {
            if (frame.Layout.ModelHash == 0UL ||
                frame.QposCount != 37 ||
                frame.QvelCount != 30 ||
                frame.ActuatorObservationCount != 9 ||
                frame.BodyPoseCount != 18)
            {
                throw new InvalidOperationException(
                    "RMA-190 authoritative state layout does not match the pinned full Reachy model.");
            }
            for (int index = 0; index < frame.QposCount; ++index)
            {
                RequireFinite(frame.GetQpos(index), "qpos", index);
            }
            for (int index = 0; index < frame.QvelCount; ++index)
            {
                RequireFinite(frame.GetQvel(index), "qvel", index);
            }
        }

        private static double MaximumActuatorMotion(
            ReachyBehaviorMotionSnapshot before,
            ReachyBehaviorMotionSnapshot after)
        {
            double maximum = 0.0;
            for (int index = 0; index < ReachyBehaviorPlannerActuators.Count; ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        after.PositionsRadians[index] -
                        before.PositionsRadians[index]));
            }
            return maximum;
        }

        private static double MaximumActuatorVelocity(
            ReachyBehaviorMotionSnapshot motion)
        {
            double maximum = 0.0;
            for (int index = 0; index < ReachyBehaviorPlannerActuators.Count; ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(motion.VelocitiesRadiansPerSecond[index]));
            }
            return maximum;
        }

        private static double MaximumQposDifference(
            ReachySimAuthoritativeStateFrame left,
            ReachySimAuthoritativeStateFrame right)
        {
            if (left.QposCount != right.QposCount)
            {
                throw new InvalidOperationException(
                    "RMA-190 replay qpos layouts differ.");
            }
            double maximum = 0.0;
            for (int index = 0; index < left.QposCount; ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(left.GetQpos(index) - right.GetQpos(index)));
            }
            return maximum;
        }

        private static double MaximumQvelDifference(
            ReachySimAuthoritativeStateFrame left,
            ReachySimAuthoritativeStateFrame right)
        {
            if (left.QvelCount != right.QvelCount)
            {
                throw new InvalidOperationException(
                    "RMA-190 replay qvel layouts differ.");
            }
            double maximum = 0.0;
            for (int index = 0; index < left.QvelCount; ++index)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(left.GetQvel(index) - right.GetQvel(index)));
            }
            return maximum;
        }

        private static void RequireFinite(
            double value,
            string field,
            int index)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException(
                    "RMA-190 authoritative " + field + "[" +
                    index.ToString(CultureInfo.InvariantCulture) +
                    "] is non-finite.");
            }
        }
    }
}
