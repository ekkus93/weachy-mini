#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        private readonly ReachyBehaviorPlannerPolicy policy;

        public ReachyDeterministicBehaviorPlanner(
            ReachyBehaviorPlannerPolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        private static bool RequestsMotion(ReachyBehaviorIntent intent)
        {
            return intent.GazeTarget != null ||
                (intent.Expression.HasValue &&
                    intent.Expression.Value != ReachyBehaviorExpression.Neutral) ||
                (intent.Gesture.HasValue &&
                    intent.Gesture.Value != ReachyBehaviorGesture.None);
        }

        private static double UrgencyScale(ReachyBehaviorUrgency? urgency)
        {
            return urgency switch
            {
                ReachyBehaviorUrgency.Low => 0.55,
                ReachyBehaviorUrgency.High => 1.0,
                _ => 0.75,
            };
        }

        private static ReachyBehaviorPlanResult Success(
            ReachyBehaviorTrajectoryPlan plan)
        {
            return new ReachyBehaviorPlanResult(
                ReachyBehaviorPlannerStatus.Planned,
                "planned",
                plan);
        }

        private static ReachyBehaviorPlanResult Failure(
            ReachyBehaviorPlannerStatus status,
            string diagnosticCode)
        {
            return new ReachyBehaviorPlanResult(
                status,
                diagnosticCode,
                plan: null);
        }

        private static string SafetyDiagnostic(
            ReachyBehaviorSafetySnapshot safety)
        {
            if (!safety.MotionPathAvailable)
            {
                return "normal-controller-path-unavailable";
            }
            if (!safety.WorkspaceClear)
            {
                return "workspace-not-cleared-for-behavior-motion";
            }
            if (safety.ActiveFault)
            {
                return "authoritative-motion-fault-active";
            }
            if (safety.ActiveCollision)
            {
                return "authoritative-collision-active";
            }
            if (safety.ActiveHardStop)
            {
                return "authoritative-hard-stop-active";
            }
            if (safety.LoadLimitActive)
            {
                return "authoritative-load-limit-active";
            }
            return "behavior-motion-safety-interlock-active";
        }
    }
}
