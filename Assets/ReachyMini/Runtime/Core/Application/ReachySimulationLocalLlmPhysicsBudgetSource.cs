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
        private readonly ReachySimulationWorker worker;
        private readonly ReachyLocalLlmPhysicsBudgetTracker tracker;

        public ReachySimulationLocalLlmPhysicsBudgetSource(
            ReachySimulationWorker worker,
            double timestepSeconds = ReachyMini.Core.ProjectMetadata.InitialPhysicsTimestepSeconds)
        {
            this.worker = worker ?? throw new ArgumentNullException(nameof(worker));
            tracker = new ReachyLocalLlmPhysicsBudgetTracker(timestepSeconds);
        }

        public LocalLlmPhysicsBudgetState Capture()
        {
            lock (gate)
            {
                if (worker.State != ReachySimulationRunState.Running)
                {
                    tracker.Reset();
                    return LocalLlmPhysicsBudgetState.Unavailable;
                }
                if (!worker.TryGetLatestSnapshot(out ReachyPublishedSimulationSnapshot snapshot))
                {
                    return LocalLlmPhysicsBudgetState.Unavailable;
                }
                return tracker.Observe(snapshot.Timing);
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
