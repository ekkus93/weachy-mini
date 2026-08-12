#nullable enable

namespace ReachyMini.Simulation
{
    public enum ReachySimulationRunState
    {
        Created = 0,
        Starting = 1,
        Running = 2,
        Paused = 3,
        Faulted = 4,
        Stopping = 5,
        Stopped = 6,
        Disposed = 7,
    }

    public enum ReachySimulationCommandEnqueueResult
    {
        Accepted = 0,
        QueueFull = 1,
        CommandTooLarge = 2,
        InvalidFormat = 3,
        WorkerUnavailable = 4,
    }
}
