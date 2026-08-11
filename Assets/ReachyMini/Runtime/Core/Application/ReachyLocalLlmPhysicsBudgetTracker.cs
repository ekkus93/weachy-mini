#nullable enable

using System;
using ReachyMini.Core;
using ReachyMini.LocalModels;
using ReachyMini.Simulation;

namespace ReachyMini.AppState
{
    public sealed class ReachyLocalLlmPhysicsBudgetTracker
    {
        private readonly double timestepSeconds;
        private bool hasPrevious;
        private ReachySimulationTimingSnapshot previous;

        public ReachyLocalLlmPhysicsBudgetTracker(
            double timestepSeconds = ProjectMetadata.InitialPhysicsTimestepSeconds)
        {
            if (double.IsNaN(timestepSeconds) ||
                double.IsInfinity(timestepSeconds) ||
                timestepSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestepSeconds));
            }
            this.timestepSeconds = timestepSeconds;
        }

        public LocalLlmPhysicsBudgetState Observe(
            ReachySimulationTimingSnapshot current)
        {
            if (!hasPrevious)
            {
                previous = current;
                hasPrevious = true;
                return LocalLlmPhysicsBudgetState.Unavailable;
            }

            ValidateMonotonicCounters(previous, current);
            ulong newDeadlineMisses =
                current.DeadlineMissCount - previous.DeadlineMissCount;
            double lagGrowth =
                current.AccumulatedLagSeconds - previous.AccumulatedLagSeconds;
            previous = current;

            if (newDeadlineMisses > 0UL)
            {
                return LocalLlmPhysicsBudgetState.Exceeded;
            }
            if (lagGrowth > timestepSeconds ||
                current.LastStepDurationSeconds > timestepSeconds)
            {
                return LocalLlmPhysicsBudgetState.AtRisk;
            }
            return LocalLlmPhysicsBudgetState.Healthy;
        }

        public void Reset()
        {
            hasPrevious = false;
            previous = default;
        }

        private static void ValidateMonotonicCounters(
            ReachySimulationTimingSnapshot previousSnapshot,
            ReachySimulationTimingSnapshot currentSnapshot)
        {
            if (currentSnapshot.TotalStepCount < previousSnapshot.TotalStepCount ||
                currentSnapshot.DeadlineMissCount < previousSnapshot.DeadlineMissCount ||
                currentSnapshot.SolverWarningCount < previousSnapshot.SolverWarningCount ||
                currentSnapshot.CommandQueueOverflowCount < previousSnapshot.CommandQueueOverflowCount ||
                currentSnapshot.DiscardedCommandCount < previousSnapshot.DiscardedCommandCount ||
                currentSnapshot.AccumulatedLagSeconds < previousSnapshot.AccumulatedLagSeconds)
            {
                throw new InvalidOperationException(
                    "Simulation timing counters regressed while evaluating the local LLM physics budget.");
            }
        }
    }
}
