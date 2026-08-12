#nullable enable

using System;
using System.Threading;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed class ReachyBaselineBehaviorLibrary
    {
        private readonly ReachyDeterministicBehaviorPlanner planner;
        private readonly ReachyBaselineBehaviorPolicy policy;

        public ReachyBaselineBehaviorLibrary(
            ReachyDeterministicBehaviorPlanner planner,
            ReachyBaselineBehaviorPolicy policy)
        {
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            this.planner.ValidateBaselinePolicy(this.policy);
        }

        public string ContractId => ReachyBaselineBehaviorPolicy.CurrentContractId;

        public ReachyBaselineBehaviorPlanResult Plan(
            ReachyBaselineBehaviorRequest request,
            WorldModelSnapshot? worldSnapshot,
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBehaviorSafetySnapshot safetySnapshot,
            long planningTimestampNanoseconds,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ReachyBehaviorPlanResult plannerResult = planner.PlanBaseline(
                request,
                policy,
                worldSnapshot,
                motionSnapshot,
                safetySnapshot,
                planningTimestampNanoseconds,
                cancellationToken);
            return new ReachyBaselineBehaviorPlanResult(request, plannerResult);
        }

        public static ReachyBaselineBehaviorLibrary CreateMobileDefault(
            ReachyDeterministicBehaviorPlanner planner)
        {
            return new ReachyBaselineBehaviorLibrary(
                planner,
                ReachyBaselineBehaviorPolicy.CreateMobileDefault());
        }
    }
}
