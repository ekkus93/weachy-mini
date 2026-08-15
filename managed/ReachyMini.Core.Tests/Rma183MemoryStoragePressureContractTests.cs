#nullable enable

using System;
using System.IO;
using ReachyMini.AppState;
using ReachyMini.Diagnostics;

namespace ReachyMini.Core.Tests
{
    internal static class Rma183MemoryStoragePressureContractTests
    {
        public static void RunAll()
        {
            MemoryPressureRegistryCountsReleaseAndActiveRetention();
            DiagnosticExportFailsBeforeWriteWhenStorageIsLow();
        }

        private static void MemoryPressureRegistryCountsReleaseAndActiveRetention()
        {
            var releasedParticipant = new FakeMemoryPressureParticipant(
                ReachyMemoryPressureReleaseStatus.Released);
            var retainedParticipant = new FakeMemoryPressureParticipant(
                ReachyMemoryPressureReleaseStatus.RetainedActiveState);

            using IDisposable released = ReachyMemoryPressureRegistry.Register(
                releasedParticipant);
            using IDisposable retained = ReachyMemoryPressureRegistry.Register(
                retainedParticipant);

            ReachyMemoryPressureSweepResult result =
                ReachyMemoryPressureRegistry.ReleaseRegisteredResources();
            Equal(2, result.ParticipantCount, "memory-pressure participant count");
            Equal(1, result.ReleasedCount, "memory-pressure released count");
            Equal(1, result.RetainedActiveCount, "memory-pressure active retention count");
            Equal(0, result.FailureCount, "memory-pressure failure count");
            Equal(1, releasedParticipant.CallCount, "released participant calls");
            Equal(1, retainedParticipant.CallCount, "retained participant calls");
        }

        private static void DiagnosticExportFailsBeforeWriteWhenStorageIsLow()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "reachy-rma183-storage-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "bundle.zip");
            try
            {
                var exporter = new ReachyStorageAwareDiagnosticBundleExporter(
                    new ConstantDiagnosticStorageProbe(0L),
                    safetyReserveBytes: 0L);
                AssertThrows<ReachyDiagnosticBundleInsufficientStorageException>(() =>
                    exporter.Export(
                        path,
                        new ReachyDiagnosticBundlePayload(
                            EmptySnapshot(),
                            Array.Empty<ReachyDiagnosticRecord>()),
                        ReachyDiagnosticBundleUserSelection.RedactedOnly));
                True(!File.Exists(path), "low-storage export did not create final bundle");
                True(!Directory.Exists(directory), "low-storage export wrote before preflight rejection");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static ReachyDiagnosticsScreenSnapshot EmptySnapshot()
        {
            ReachyDiagnosticsSection Section(string title) =>
                new ReachyDiagnosticsSection(
                    title,
                    new[] { new ReachyDiagnosticsMetric("status", "ok") });

            return new ReachyDiagnosticsScreenSnapshot(
                Section("Simulation"),
                Section("Rendering"),
                Section("Camera"),
                Section("Providers"),
                Section("Versions"),
                Section("Device"));
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Expected " + typeof(TException).Name + ".");
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + " expected true.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected=" + expected + "; actual=" + actual);
            }
        }

        private sealed class FakeMemoryPressureParticipant :
            IReachyMemoryPressureParticipant
        {
            private readonly ReachyMemoryPressureReleaseStatus status;

            public FakeMemoryPressureParticipant(
                ReachyMemoryPressureReleaseStatus status)
            {
                this.status = status;
            }

            public int CallCount { get; private set; }

            public ReachyMemoryPressureReleaseResult ReleaseForMemoryPressure()
            {
                ++CallCount;
                return new ReachyMemoryPressureReleaseResult(status, string.Empty);
            }
        }

        private sealed class ConstantDiagnosticStorageProbe :
            IReachyDiagnosticBundleStorageProbe
        {
            private readonly long availableBytes;

            public ConstantDiagnosticStorageProbe(long availableBytes)
            {
                this.availableBytes = availableBytes;
            }

            public long GetAvailableBytes(string outputDirectory)
            {
                True(Path.IsPathRooted(outputDirectory), "diagnostic storage path is rooted");
                return availableBytes;
            }
        }
    }
}
