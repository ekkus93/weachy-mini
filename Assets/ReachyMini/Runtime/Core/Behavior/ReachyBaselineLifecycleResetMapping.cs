#nullable enable

using System;
using ReachyMini.Interop;

namespace ReachyMini.Behavior
{
    public static class ReachyBaselineLifecycleResetMapping
    {
        public static ReachySimResetPose RequireResetPose(
            ReachyBaselineLifecycleAction action)
        {
            switch (action)
            {
                case ReachyBaselineLifecycleAction.EnterSleepRest:
                    return ReachySimResetPose.SleepRest;
                case ReachyBaselineLifecycleAction.WakeNeutral:
                    return ReachySimResetPose.NeutralAwake;
                case ReachyBaselineLifecycleAction.None:
                    throw new ArgumentException(
                        "The none lifecycle action does not request a simulator reset.",
                        nameof(action));
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }
}
