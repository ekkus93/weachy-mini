#nullable enable

using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public readonly struct ReachySimulationTimingSnapshot
    {
        internal ReachySimulationTimingSnapshot(
            ulong totalStepCount,
            ulong deadlineMissCount,
            ulong solverWarningCount,
            ulong commandQueueOverflowCount,
            ulong discardedCommandCount,
            double accumulatedLagSeconds,
            double lastStepDurationSeconds,
            double maximumStepDurationSeconds)
        {
            TotalStepCount = totalStepCount;
            DeadlineMissCount = deadlineMissCount;
            SolverWarningCount = solverWarningCount;
            CommandQueueOverflowCount = commandQueueOverflowCount;
            DiscardedCommandCount = discardedCommandCount;
            AccumulatedLagSeconds = accumulatedLagSeconds;
            LastStepDurationSeconds = lastStepDurationSeconds;
            MaximumStepDurationSeconds = maximumStepDurationSeconds;
        }

        public ulong TotalStepCount { get; }

        public ulong DeadlineMissCount { get; }

        public ulong SolverWarningCount { get; }

        public ulong CommandQueueOverflowCount { get; }

        public ulong DiscardedCommandCount { get; }

        public double AccumulatedLagSeconds { get; }

        public double LastStepDurationSeconds { get; }

        public double MaximumStepDurationSeconds { get; }
    }

    public readonly struct ReachyPublishedSimulationSnapshot
    {
        internal ReachyPublishedSimulationSnapshot(
            ulong publicationSequence,
            ReachySimStateSnapshot state,
            ReachySimulationTimingSnapshot timing)
        {
            PublicationSequence = publicationSequence;
            State = state;
            Timing = timing;
        }

        public ulong PublicationSequence { get; }

        public ReachySimStateSnapshot State { get; }

        public ReachySimulationTimingSnapshot Timing { get; }
    }
}
