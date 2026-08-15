#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Performance;

namespace ReachyMini.Core.Tests
{
    internal static class Rma180PerformanceHarnessContractTests
    {
        public static void RunAll()
        {
            ExactPercentilesAndResourceSummaryAreReported();
            ThirtyAndSixtyFpsProfilesAreExplicit();
            LongRunsRemainBounded();
            MeasurementScopeUsesTheActiveSessionOnly();
            InvalidAndPrivateSessionInputsFailClosed();
        }

        private static void ExactPercentilesAndResourceSummaryAreReported()
        {
            using ReachyPerformanceSession session =
                ReachyPerformanceTelemetry.StartSession(30, "rma180-unit");
            for (int milliseconds = 1; milliseconds <= 100; ++milliseconds)
            {
                ReachyPerformanceTelemetry.RecordDurationSeconds(
                    ReachyPerformanceWorkload.NativePhysics,
                    milliseconds / 1000.0);
            }
            ReachyPerformanceTelemetry.RecordResourceSample(
                new ReachyPerformanceResourceSample(
                    1.0,
                    unityAllocatedMemoryBytes: 1000L,
                    systemAvailableMemoryBytes: 8000L,
                    batteryLevelFraction: 0.80,
                    thermalSeverity: 2,
                    thermalState: "Light"));
            ReachyPerformanceTelemetry.RecordResourceSample(
                new ReachyPerformanceResourceSample(
                    11.0,
                    unityAllocatedMemoryBytes: 1500L,
                    systemAvailableMemoryBytes: 7000L,
                    batteryLevelFraction: 0.75,
                    thermalSeverity: 4,
                    thermalState: "Severe"));

            ReachyPerformanceReport report = session.Complete();
            ReachyPerformanceTimingSummary physics =
                report.FindTiming(ReachyPerformanceWorkload.NativePhysics);
            Equal(100L, physics.SampleCount, "physics sample count");
            Near(50.0, physics.MedianMilliseconds, "physics median");
            Near(95.0, physics.P95Milliseconds, "physics p95");
            Near(99.0, physics.P99Milliseconds, "physics p99");
            Near(100.0, physics.MaximumMilliseconds, "physics maximum");
            Equal(false, physics.PercentilesApproximate, "exact percentile flag");

            ReachyPerformanceTimingSummary audio =
                report.FindTiming(ReachyPerformanceWorkload.Audio);
            Equal(
                ReachyPerformanceMetricAvailability.Unavailable,
                audio.Availability,
                "unexercised workload availability");
            Equal(true, audio.AvailabilityReason.Length > 0, "unavailable reason");

            Equal(2L, report.Resources.SampleCount, "resource sample count");
            Equal(1000L, report.Resources.InitialUnityAllocatedMemoryBytes, "initial allocated memory");
            Equal(1500L, report.Resources.FinalUnityAllocatedMemoryBytes, "final allocated memory");
            Equal(1500L, report.Resources.MaximumUnityAllocatedMemoryBytes, "maximum allocated memory");
            Equal(7000L, report.Resources.MinimumSystemAvailableMemoryBytes, "minimum available memory");
            Near(0.80, report.Resources.InitialBatteryLevelFraction, "initial battery");
            Near(0.75, report.Resources.FinalBatteryLevelFraction, "final battery");
            Near(0.05, report.Resources.BatteryDischargeFraction, "battery discharge");
            Equal("Severe", report.Resources.PeakThermalState, "peak thermal state");

            string json = ReachyPerformanceReportJsonFormatter.Format(report);
            Contains(json, "\"median_ms\":50", "median JSON");
            Contains(json, "\"p95_ms\":95", "p95 JSON");
            Contains(json, "\"p99_ms\":99", "p99 JSON");
            Contains(json, "\"battery_discharge_fraction\"", "battery JSON");
            Contains(json, "\"peak_thermal_state\":\"Severe\"", "thermal JSON");
        }

        private static void ThirtyAndSixtyFpsProfilesAreExplicit()
        {
            using (ReachyPerformanceSession fps30 =
                ReachyPerformanceTelemetry.StartSession(30, "fps30"))
            {
                Equal(30, fps30.TargetFramesPerSecond, "30 FPS target");
                Throws<InvalidOperationException>(() =>
                    ReachyPerformanceTelemetry.StartSession(60, "nested"));
                fps30.Complete();
            }

            using ReachyPerformanceSession fps60 =
                ReachyPerformanceTelemetry.StartSession(60, "fps60");
            ReachyPerformanceTelemetry.RecordDurationSeconds(
                ReachyPerformanceWorkload.UnityRendering,
                1.0 / 60.0);
            ReachyPerformanceReport report = fps60.Complete();
            Equal(60, report.TargetFramesPerSecond, "60 FPS target");
            Equal(
                1L,
                report.FindTiming(ReachyPerformanceWorkload.UnityRendering).SampleCount,
                "60 FPS rendering sample");
        }

        private static void LongRunsRemainBounded()
        {
            using ReachyPerformanceSession session =
                ReachyPerformanceTelemetry.StartSession(30, "bounded");
            int timingSamples =
                ReachyPerformanceTelemetry.PercentileReservoirCapacity + 500;
            for (int index = 0; index < timingSamples; ++index)
            {
                ReachyPerformanceTelemetry.RecordDurationSeconds(
                    ReachyPerformanceWorkload.CameraWarp,
                    (index + 1) / 1000000.0);
            }
            int resourceSamples =
                ReachyPerformanceTelemetry.MaximumResourceSamples + 7;
            for (int index = 0; index < resourceSamples; ++index)
            {
                ReachyPerformanceTelemetry.RecordResourceSample(
                    new ReachyPerformanceResourceSample(
                        index,
                        index == 0 ? 100000L : 1000L + index,
                        index == 0 ? 100L : 5000L + index,
                        index == 0 ? 1.0 : 0.75,
                        index == 0 ? 7 : 1,
                        index == 0 ? "Shutdown" : "None",
                        "test"));
            }

            ReachyPerformanceReport report = session.Complete();
            ReachyPerformanceTimingSummary warp =
                report.FindTiming(ReachyPerformanceWorkload.CameraWarp);
            Equal((long)timingSamples, warp.SampleCount, "bounded timing total count");
            Equal(true, warp.PercentilesApproximate, "reservoir percentile flag");
            Near(
                timingSamples / 1000.0,
                warp.MaximumMilliseconds,
                "reservoir exact maximum");
            Equal(
                (long)ReachyPerformanceTelemetry.MaximumResourceSamples,
                report.Resources.SampleCount,
                "bounded retained resource samples");
            Equal(7L, report.Resources.DroppedSampleCount, "dropped resource samples");
            Equal(
                7.0,
                report.ResourceSamples[0].MonotonicSeconds,
                "resource ring retains newest window");
            Equal(
                100000L,
                report.Resources.MaximumUnityAllocatedMemoryBytes,
                "resource summary preserves dropped-window maximum");
            Equal(
                100L,
                report.Resources.MinimumSystemAvailableMemoryBytes,
                "resource summary preserves dropped-window minimum");
            Near(
                1.0,
                report.Resources.InitialBatteryLevelFraction,
                "resource summary preserves initial battery");
            Near(
                0.25,
                report.Resources.BatteryDischargeFraction,
                "resource summary preserves session-wide discharge");
            Equal(
                "Shutdown",
                report.Resources.PeakThermalState,
                "resource summary preserves peak thermal state");
        }

        private static void MeasurementScopeUsesTheActiveSessionOnly()
        {
            using (ReachyPerformanceTelemetry.Measure(
                ReachyPerformanceWorkload.Network))
            {
                // No active session: this must be a zero-cost logical no-op.
            }

            using ReachyPerformanceSession session =
                ReachyPerformanceTelemetry.StartSession(30, "scope");
            using (ReachyPerformanceTelemetry.Measure(
                ReachyPerformanceWorkload.Network))
            {
            }
            ReachyPerformanceReport report = session.Complete();
            Equal(
                1L,
                report.FindTiming(ReachyPerformanceWorkload.Network).SampleCount,
                "active measurement scope count");
        }

        private static void InvalidAndPrivateSessionInputsFailClosed()
        {
            Throws<ArgumentOutOfRangeException>(() =>
                ReachyPerformanceTelemetry.StartSession(45, "fps45"));
            Throws<ArgumentException>(() =>
                ReachyPerformanceTelemetry.StartSession(
                    30,
                    "contains private conversation text"));
            Throws<ArgumentOutOfRangeException>(() =>
  {
      ReachyPerformanceResourceSample sample =
          new ReachyPerformanceResourceSample(
              0.0,
              0L,
              0L,
              1.1,
              null,
              "unavailable");
      GC.KeepAlive(sample);
  });
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-180 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void Near(
            double expected,
            double? actual,
            string description,
            double tolerance = 1.0e-9)
        {
            if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance)
            {
                throw new InvalidOperationException(
                    $"RMA-180 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void Contains(string value, string expected, string description)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RMA-180 contract failed for {description}: missing {expected}.");
            }
        }

        private static void Throws<TException>(Action action)
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
                $"RMA-180 contract expected {typeof(TException).Name}.");
        }
    }
}
