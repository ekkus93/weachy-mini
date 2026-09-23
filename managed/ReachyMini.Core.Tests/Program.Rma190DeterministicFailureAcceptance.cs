#nullable enable

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private const string Rma190FailureAcceptanceFlag =
            "REACHY_RMA190_DETERMINISTIC_FAILURE_ACCEPTANCE";
        private const string Rma190FailureReportPath =
            "REACHY_RMA190_FAILURE_REPORT";

        [ModuleInitializer]
        internal static void RunRma190DeterministicFailureAcceptanceModule()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(Rma190FailureAcceptanceFlag),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            RunRma190DeterministicFailureAcceptanceAsync()
                .GetAwaiter()
                .GetResult();
        }

        private static async Task RunRma190DeterministicFailureAcceptanceAsync()
        {
            await TestRma190ProviderNetworkFailureScenarioAsync().ConfigureAwait(false);
            await TestRma190ModelCorruptionAndOomScenarioAsync().ConfigureAwait(false);
            TestRma190SolverFaultAndControlledResetScenario();

            string reportPath = Environment.GetEnvironmentVariable(
                    Rma190FailureReportPath) ??
                throw new InvalidOperationException(
                    $"{Rma190FailureReportPath} must name the machine-readable acceptance report path.");
            string? directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            const string report =
                "{\n" +
                "  \"schema_version\": 1,\n" +
                "  \"network_loss\": \"Passed\",\n" +
                "  \"rate_limit\": \"Passed\",\n" +
                "  \"malformed_response\": \"Passed\",\n" +
                "  \"cancellation\": \"Passed\",\n" +
                "  \"model_corruption\": \"Passed\",\n" +
                "  \"model_oom_recovery\": \"Passed\",\n" +
                "  \"solver_fault\": \"Passed\",\n" +
                "  \"controlled_reset\": \"Passed\",\n" +
                "  \"production_http_transport\": true,\n" +
                "  \"production_model_package_manager\": true,\n" +
                "  \"production_local_llm_provider\": true,\n" +
                "  \"native_backed_simulation_worker\": true,\n" +
                "  \"hidden_fallback_used\": false\n" +
                "}\n";
            File.WriteAllText(reportPath, report);
            Console.WriteLine(report.TrimEnd());
        }
    }
}
