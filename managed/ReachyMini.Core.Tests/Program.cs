using System;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static int Main()
        {
            TestProjectMetadata();
            TestNativeLayouts();
            TestSimulationWorkerWarningAccounting();
            Rma170StructuredDiagnosticsContractTests.RunAll();
            Rma171DiagnosticsScreenContractTests.RunAll();
            Rma172DiagnosticBundleExportContractTests.RunAll();
            Rma180PerformanceHarnessContractTests.RunAll();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                TestNativeSessionLifecycle();
                TestAuthoritativeSimulationWorker();
                TestAuthoritativeSimulationWorkerPublishedState();
                TestAuthoritativeSimulationWorkerSnapshots();
                TestAuthoritativeSimulationWorkerDeadlineMetrics();
                TestAuthoritativeSimulationWorkerFaultRetention();
            }

            return 0;
        }
    }
}
