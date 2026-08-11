#nullable enable

namespace ReachyMini.Simulation
{
    public interface IReachySimulationTimingSource
    {
        ReachySimulationRunState SimulationRunState { get; }

        bool TryGetLatestTimingSnapshot(out ReachySimulationTimingSnapshot timing);
    }
}
