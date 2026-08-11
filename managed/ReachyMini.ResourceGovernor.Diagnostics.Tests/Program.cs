#nullable enable

using System;
using ReachyMini.LocalModels;

namespace ReachyMini.ResourceGovernor.Diagnostics.Tests
{
    internal static class Program
    {
        private const long GiB = 1024L * 1024L * 1024L;
        private static int failures;

        public static int Main()
        {
            Run("unavailable decision", UnavailableDecision);
            Run("nominal diagnostics", NominalDiagnostics);
            Run("throttled diagnostics", ThrottledDiagnostics);
            Run("suspended diagnostics", SuspendedDiagnostics);
            Run("thermal unavailable explicit", ThermalUnavailableExplicit);

            Console.WriteLine(failures == 0
                ? "RMA-135 governor diagnostic projection passed."
                : $"RMA-135 governor diagnostic projection failed: {failures}.");
            return failures == 0 ? 0 : 1;
        }

        private static void UnavailableDecision()
        {
            LocalLlmGovernorDiagnosticsSnapshot snapshot =
                LocalLlmGovernorDiagnosticsSnapshot.Create(null);
            Equal(LocalLlmGovernorPresentationState.Unavailable, snapshot.PresentationState);
            Equal(false, snapshot.InferenceAvailable);
            Contains(snapshot.DiagnosticLine, "decision unavailable");
        }

        private static void NominalDiagnostics()
        {
            LocalLlmGovernorDiagnosticsSnapshot snapshot = Snapshot(
                LocalLlmThermalStatus.None,
                LocalLlmPhysicsBudgetState.Healthy,
                12L * GiB,
                8L * GiB);
            Equal(LocalLlmGovernorPresentationState.Ready, snapshot.PresentationState);
            Equal(true, snapshot.InferenceAvailable);
            Contains(snapshot.DiagnosticLine, "mode=Nominal");
            Contains(snapshot.DiagnosticLine, "device=Performance");
            Contains(snapshot.DiagnosticLine, "ctx=2048,batch=256,ubatch=64,threads=4/4");
        }

        private static void ThrottledDiagnostics()
        {
            LocalLlmGovernorDiagnosticsSnapshot snapshot = Snapshot(
                LocalLlmThermalStatus.Light,
                LocalLlmPhysicsBudgetState.Healthy,
                12L * GiB,
                8L * GiB);
            Equal(LocalLlmGovernorPresentationState.Throttled, snapshot.PresentationState);
            Equal(true, snapshot.InferenceAvailable);
            Contains(snapshot.UserMessage, "resource-throttled");
            Contains(snapshot.DiagnosticLine, "light thermal pressure");
            Contains(snapshot.DiagnosticLine, "ctx=1536,batch=128,ubatch=64,threads=3/3");
        }

        private static void SuspendedDiagnostics()
        {
            LocalLlmGovernorDiagnosticsSnapshot snapshot = Snapshot(
                LocalLlmThermalStatus.None,
                LocalLlmPhysicsBudgetState.Exceeded,
                12L * GiB,
                8L * GiB);
            Equal(LocalLlmGovernorPresentationState.Suspended, snapshot.PresentationState);
            Equal(false, snapshot.InferenceAvailable);
            Equal("suspended", snapshot.EffectiveProfile);
            Contains(snapshot.UserMessage, "unavailable");
            Contains(snapshot.DiagnosticLine, "physics timing budget exceeded");
        }

        private static void ThermalUnavailableExplicit()
        {
            LocalLlmGovernorDiagnosticsSnapshot snapshot = Snapshot(
                LocalLlmThermalStatus.Unavailable,
                LocalLlmPhysicsBudgetState.Healthy,
                4L * GiB,
                2L * GiB);
            Equal(false, snapshot.ThermalTelemetryAvailable);
            Contains(snapshot.DiagnosticLine, "thermal telemetry unavailable");
            Contains(snapshot.DiagnosticLine, "device=Conservative");
        }

        private static LocalLlmGovernorDiagnosticsSnapshot Snapshot(
            LocalLlmThermalStatus thermal,
            LocalLlmPhysicsBudgetState physics,
            long totalMemory,
            long availableMemory)
        {
            var governor = new LocalLlmResourceGovernor();
            LocalLlmGovernorDecision decision = governor.Evaluate(
                LocalLlmExecutionProfile.CreateRma133V6Baseline(),
                new LocalLlmResourceSnapshot(
                    totalMemory,
                    availableMemory,
                    512L * 1024L * 1024L,
                    false,
                    8,
                    thermal,
                    physics));
            return LocalLlmGovernorDiagnosticsSnapshot.Create(decision);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception exception)
            {
                ++failures;
                Console.Error.WriteLine("FAIL: " + name + ": " + exception.Message);
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
            }
        }

        private static void Contains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    $"Expected diagnostic text to contain '{expected}'; actual '{value}'.");
            }
        }
    }
}
