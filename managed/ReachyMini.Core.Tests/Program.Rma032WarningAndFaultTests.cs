using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static void TestSimulationWorkerWarningAccounting()
        {
            const uint sleepingHealthFlag = 1U << 0;
            const uint warningHealthFlag = 1U << 1;

            ulong warningCount =
                ReachySimulationWorker.CountNewSolverWarningEpisodes(
                    currentCount: 0UL,
                    previousHealthFlags: 0U,
                    currentHealthFlags: sleepingHealthFlag);
            AssertEqual(
                0UL,
                warningCount,
                "sleeping health does not increment solver warnings");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                sleepingHealthFlag,
                sleepingHealthFlag | warningHealthFlag);
            AssertEqual(1UL, warningCount, "warning rising edge");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                sleepingHealthFlag | warningHealthFlag,
                warningHealthFlag);
            AssertEqual(1UL, warningCount, "persistent warning is not recounted");

            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                warningHealthFlag,
                currentHealthFlags: 0U);
            warningCount = ReachySimulationWorker.CountNewSolverWarningEpisodes(
                warningCount,
                previousHealthFlags: 0U,
                currentHealthFlags: warningHealthFlag);
            AssertEqual(2UL, warningCount, "second warning episode");
        }
    }
}
