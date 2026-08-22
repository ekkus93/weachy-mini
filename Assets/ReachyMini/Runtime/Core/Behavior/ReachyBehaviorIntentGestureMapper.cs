#nullable enable

using System;

namespace ReachyMini.Behavior
{
    // RMA-195 (voice+visual grounding, 2026-08-22): maps an RMA-151 behavior
    // intent's requested gesture onto the baseline gesture vocabulary the
    // deterministic planner (RMA-152/153) already knows how to execute via
    // IReachyBehaviorService.TryTriggerGesture. Deliberately v1-minimal --
    // only Gesture is mapped here. Expression, GazeTarget, Urgency, and
    // Timing are explicitly out of scope for this pass, not silently
    // dropped: there is no baseline-behavior counterpart wired for them yet,
    // and a future phase can extend this mapper (or add sibling mappers)
    // once those counterparts exist. Kept pure and Core-layer-only (no
    // ReachyMini.AppState / Unity dependency) specifically so it is
    // verifiable with a plain `dotnet test`, unlike the rest of the
    // conversation-turn orchestrator which requires a Unity Editor.
    public static class ReachyBehaviorIntentGestureMapper
    {
        public static ReachyBaselineBehaviorKind? Map(ReachyBehaviorGesture gesture)
        {
            switch (gesture)
            {
                case ReachyBehaviorGesture.None:
                    return null;
                case ReachyBehaviorGesture.Nod:
                    return ReachyBaselineBehaviorKind.Acknowledgment;
                case ReachyBehaviorGesture.SmallHeadTilt:
                    return ReachyBaselineBehaviorKind.Curiosity;
                case ReachyBehaviorGesture.Recoil:
                    return ReachyBaselineBehaviorKind.Surprise;
                default:
                    throw new ArgumentOutOfRangeException(nameof(gesture));
            }
        }
    }
}
