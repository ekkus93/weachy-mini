#nullable enable

using System;
using ReachyMini.LocalModels;
using ReachyMini.Performance;

namespace ReachyMini.AppState
{
    public sealed class ReachyPriorityDegradationRuntime
    {
        private readonly ILocalLlmResourceSignalSource resourceSignals;
        private readonly ILocalLlmPhysicsBudgetSource physicsBudget;
        private readonly ReachyPriorityDegradationCoordinator coordinator;

        public ReachyPriorityDegradationRuntime(
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            ReachyPriorityDegradationCoordinator coordinator)
        {
            this.resourceSignals = resourceSignals ??
                throw new ArgumentNullException(nameof(resourceSignals));
            this.physicsBudget = physicsBudget ??
                throw new ArgumentNullException(nameof(physicsBudget));
            this.coordinator = coordinator ??
                throw new ArgumentNullException(nameof(coordinator));
        }

        public ReachyPriorityDegradationDecision CaptureAndApply(
            double? recentRenderP95Milliseconds = null)
        {
            LocalLlmPhysicsBudgetState physics = physicsBudget.Capture();
            LocalLlmResourceSnapshot resource = resourceSignals.Capture(physics);
            ReachyPriorityDegradationSignals signals =
                ReachyPriorityDegradationSignals.FromResourceSnapshot(
                    resource,
                    recentRenderP95Milliseconds);
            return coordinator.EvaluateAndApply(signals);
        }
    }
}
