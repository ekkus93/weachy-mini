#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.Behavior;

namespace ReachyMini.Core.Tests
{
    // RMA-195 (voice+visual grounding, 2026-08-22): locks in the one piece of
    // the new conversation-turn orchestrator that is pure Core-layer logic
    // (no Unity/UnityEngine dependency) -- the RMA-151 intent gesture ->
    // RMA-153 baseline-behavior-kind mapping. The orchestrator itself lives
    // under Assets/ReachyMini/Runtime/Application (Unity-only) and cannot be
    // exercised by this managed test project; see
    // Assets/ReachyMini/Tests/Editor for its Unity EditMode coverage.
    internal static class Rma195ConversationTurnGestureMappingTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            NoneMapsToNoGesture();
            NodMapsToAcknowledgment();
            SmallHeadTiltMapsToCuriosity();
            RecoilMapsToSurprise();
            UndefinedGestureFailsClosed();
        }

        private static void NoneMapsToNoGesture()
        {
            ReachyBaselineBehaviorKind? mapped =
                ReachyBehaviorIntentGestureMapper.Map(ReachyBehaviorGesture.None);
            Equal<ReachyBaselineBehaviorKind?>(null, mapped, "gesture none");
        }

        private static void NodMapsToAcknowledgment()
        {
            ReachyBaselineBehaviorKind? mapped =
                ReachyBehaviorIntentGestureMapper.Map(ReachyBehaviorGesture.Nod);
            Equal<ReachyBaselineBehaviorKind?>(
                ReachyBaselineBehaviorKind.Acknowledgment,
                mapped,
                "gesture nod");
        }

        private static void SmallHeadTiltMapsToCuriosity()
        {
            ReachyBaselineBehaviorKind? mapped =
                ReachyBehaviorIntentGestureMapper.Map(ReachyBehaviorGesture.SmallHeadTilt);
            Equal<ReachyBaselineBehaviorKind?>(
                ReachyBaselineBehaviorKind.Curiosity,
                mapped,
                "gesture small head tilt");
        }

        private static void RecoilMapsToSurprise()
        {
            ReachyBaselineBehaviorKind? mapped =
                ReachyBehaviorIntentGestureMapper.Map(ReachyBehaviorGesture.Recoil);
            Equal<ReachyBaselineBehaviorKind?>(
                ReachyBaselineBehaviorKind.Surprise,
                mapped,
                "gesture recoil");
        }

        private static void UndefinedGestureFailsClosed()
        {
            bool threw = false;
            try
            {
                ReachyBehaviorIntentGestureMapper.Map((ReachyBehaviorGesture)999);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Equal(true, threw, "undefined gesture fails closed");
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }
    }
}
