#nullable enable

using System;
using ReachyMini.Behavior;
using ReachyMini.Simulation;

namespace ReachyMini.Rendering
{
    public sealed class ReachyProductionBehaviorControllerTargetSink :
        IReachyBehaviorControllerTargetSink
    {
        private readonly ReachyProductionAuthoritativeRuntime runtime;

        public ReachyProductionBehaviorControllerTargetSink(
            ReachyProductionAuthoritativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public ReachyBehaviorTargetSubmissionStatus Submit(
            ReachyBehaviorTrajectoryFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            double[] targets = frame.CopyTargetPositionsRadians();
            try
            {
                ReachySimulationCommandEnqueueResult result =
                    runtime.SubmitPositionTargets(targets);
                return result switch
                {
                    ReachySimulationCommandEnqueueResult.Accepted =>
                        ReachyBehaviorTargetSubmissionStatus.Accepted,
                    ReachySimulationCommandEnqueueResult.QueueFull =>
                        ReachyBehaviorTargetSubmissionStatus.QueueFull,
                    ReachySimulationCommandEnqueueResult.WorkerUnavailable =>
                        ReachyBehaviorTargetSubmissionStatus.ControllerUnavailable,
                    _ => ReachyBehaviorTargetSubmissionStatus.ContractRejected,
                };
            }
            finally
            {
                Array.Clear(targets, 0, targets.Length);
            }
        }
    }
}
