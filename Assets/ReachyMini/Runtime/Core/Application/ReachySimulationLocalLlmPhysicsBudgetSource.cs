#nullable enable

using System;
using ReachyMini.LocalModels;
using ReachyMini.Simulation;

namespace ReachyMini.AppState
{
    public sealed class ReachySimulationLocalLlmPhysicsBudgetSource :
        ILocalLlmPhysicsBudgetSource
    {
        private readonly object gate = new object();
        private readonly IReachySimulationTimingSource timingSource;
        private readonly ReachyLocalLlmPhysicsBudgetTracker tracker;

        public ReachySimulationLocalLlmPhysicsBudgetSource(
            IReachySimulationTimingSource timingSource,
            double timestepSeconds = ReachyMini.Core.ProjectMetadata.InitialPhysicsTimestepSeconds)
        {
            this.timingSource = timingSource ??
                throw new ArgumentNullException(nameof(timingSource));
            tracker = new ReachyLocalLlmPhysicsBudgetTracker(timestepSeconds);
        }

        public LocalLlmPhysicsBudgetState Capture()
        {
            lock (gate)
            {
                if (timingSource.SimulationRunState != ReachySimulationRunState.Running)
                {
                    tracker.Reset();
                    return LocalLlmPhysicsBudgetState.Unavailable;
                }
                if (!timingSource.TryGetLatestTimingSnapshot(
                    out ReachySimulationTimingSnapshot timing))
                {
                    return LocalLlmPhysicsBudgetState.Unavailable;
                }
                return tracker.Observe(timing);
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                tracker.Reset();
            }
        }
    }
}
