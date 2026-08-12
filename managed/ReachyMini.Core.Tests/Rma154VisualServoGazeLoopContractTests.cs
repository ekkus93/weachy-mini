#nullable enable

using System.Runtime.CompilerServices;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma154VisualServoGazeLoopContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            EdgeTargetRecentersOnlyAfterAuthoritativeMotionAndNewFrame();
            RequestedTargetsDoNotCountAsMotionFeedback();
            StopConditionsAreFailClosed();
            FeedbackRegressionFailsClosed();
            ObservationReplayProducesRepeatableTrajectories();
        }
    }
}
