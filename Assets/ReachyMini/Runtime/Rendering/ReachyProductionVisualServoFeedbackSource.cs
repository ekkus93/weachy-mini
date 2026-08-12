#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Perception;

namespace ReachyMini.Rendering
{
    public sealed class ReachyProductionVisualServoFeedbackSource :
        IReachyVisualServoFeedbackSource
    {
        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly BoundedWorldModel worldModel;
        private readonly Func<long> timestampNanosecondsSource;
        private readonly Func<bool> workspaceClearSource;
        private ReachySimAuthoritativeStateFrame? authoritativeStateFrame;

        public ReachyProductionVisualServoFeedbackSource(
            ReachyProductionAuthoritativeRuntime runtime,
            BoundedWorldModel worldModel,
            Func<long> timestampNanosecondsSource,
            Func<bool> workspaceClearSource)
        {
            this.runtime = runtime ??
                throw new ArgumentNullException(nameof(runtime));
            this.worldModel = worldModel ??
                throw new ArgumentNullException(nameof(worldModel));
            this.timestampNanosecondsSource = timestampNanosecondsSource ??
                throw new ArgumentNullException(nameof(timestampNanosecondsSource));
            this.workspaceClearSource = workspaceClearSource ??
                throw new ArgumentNullException(nameof(workspaceClearSource));
        }

        public ValueTask<ReachyVisualServoFeedbackSample> CaptureAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (runtime.Status != ReachyProductionRuntimeStatus.Running)
            {
                throw Unavailable(
                    "visual-servo-production-runtime-not-running",
                    "The production authoritative runtime is not running.");
            }

            long timestampNanoseconds = timestampNanosecondsSource();
            if (timestampNanoseconds <= 0L)
            {
                throw Unavailable(
                    "visual-servo-production-timestamp-unavailable",
                    "The production visual-servo timestamp source returned an invalid value.");
            }

            WorldModelSnapshot worldSnapshot =
                worldModel.GetSnapshot(timestampNanoseconds);
            ReachySimAuthoritativeStateFrame state =
                GetOrCreateAuthoritativeStateFrame();
            if (!runtime.TryCaptureLatestAuthoritativeState(state))
            {
                throw Unavailable(
                    "visual-servo-authoritative-state-unavailable",
                    "No current authoritative MuJoCo state is available for visual servoing.");
            }
            if (state.Sequence == 0UL)
            {
                throw Unavailable(
                    "visual-servo-authoritative-state-sequence-invalid",
                    "The authoritative MuJoCo state has no valid sequence.");
            }

            ReachyBehaviorMotionSnapshot motionSnapshot =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(state);
            ReachyBehaviorSafetySnapshot safetySnapshot =
                ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot(
                    state,
                    normalControllerAvailable: true,
                    workspaceClear: workspaceClearSource());

            return new ValueTask<ReachyVisualServoFeedbackSample>(
                new ReachyVisualServoFeedbackSample(
                    timestampNanoseconds,
                    state.Sequence,
                    worldSnapshot,
                    motionSnapshot,
                    safetySnapshot));
        }

        private ReachySimAuthoritativeStateFrame GetOrCreateAuthoritativeStateFrame()
        {
            ReachySimAuthoritativeStateFrame? existing = authoritativeStateFrame;
            if (existing != null)
            {
                return existing;
            }
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame created))
            {
                throw Unavailable(
                    "visual-servo-authoritative-layout-unavailable",
                    "The production authoritative-state layout is unavailable.");
            }
            authoritativeStateFrame = created;
            return created;
        }

        private static ReachyVisualServoFeedbackUnavailableException Unavailable(
            string diagnosticCode,
            string message)
        {
            return new ReachyVisualServoFeedbackUnavailableException(
                diagnosticCode,
                message);
        }
    }
}
