#nullable enable

using System;
using ReachyMini.Rendering;
using ReachyMini.Simulation;

namespace ReachyMini.AppState
{
    internal sealed class ReachyProductionSimulationApplicationService :
        ReachyApplicationServiceBase,
        IReachySimulationService,
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyProductionAuthoritativeRuntime runtime;

        public ReachyProductionSimulationApplicationService(
            ReachyProductionAuthoritativeRuntime runtime)
            : base(
                "production-simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        protected override void OnInitialize()
        {
            SetReady(
                $"Authoritative simulation adapter is present; runtime status is {runtime.Status}.");
        }

        public void PauseForApplicationInterruption()
        {
            ReachySimulationControlResult result =
                runtime.PauseForApplicationInterruption();
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation pause failed with " +
                    result.Error.Code + ".");
            }
        }

        public void ResumeAfterApplicationInterruption()
        {
            ReachySimulationControlResult result =
                runtime.ResumeAfterApplicationInterruption();
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation resume failed with " +
                    result.Error.Code + ".");
            }
        }
    }

}
