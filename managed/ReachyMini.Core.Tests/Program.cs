using System;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static int Main()
        {
            Rma190IndependentProviderCombinationTests.RunAll();
            Rma190ProviderEndToEndScenarioTests.RunAll();
            TestProjectMetadata();
            TestNativeLayouts();
            TestSimulationWorkerWarningAccounting();
            Rma170StructuredDiagnosticsContractTests.RunAll();
            Rma171DiagnosticsScreenContractTests.RunAll();
            Rma172DiagnosticBundleExportContractTests.RunAll();
            Rma180PerformanceHarnessContractTests.RunAll();
            Rma182ApplicationInterruptionContractTests.RunAll();
            Rma183MemoryStoragePressureContractTests.RunAll();
            Rma184RepresentativeDeviceMatrixContractTests.RunAll();
            Rma190NetworkFailureEndToEndTests.RunAll();

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
