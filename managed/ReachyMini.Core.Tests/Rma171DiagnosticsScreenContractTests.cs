#nullable enable

using System;
using ReachyMini.Diagnostics;

namespace ReachyMini.Core.Tests
{
    internal static class Rma171DiagnosticsScreenContractTests
    {
        public static void RunAll()
        {
            UnavailableMetricRequiresReason();
            SectionPromotesWorstAvailability();
            SnapshotRendersEverySectionAndReason();
        }

        private static void UnavailableMetricRequiresReason()
        {
            AssertThrows<ArgumentException>(() =>
  {
      ReachyDiagnosticsMetric metric =
          new ReachyDiagnosticsMetric(
              "Camera FPS",
              "unavailable",
              ReachyDiagnosticsAvailability.Unavailable);
      GC.KeepAlive(metric);
  });
        }

        private static void SectionPromotesWorstAvailability()
        {
            var section = new ReachyDiagnosticsSection(
                "Camera",
                new[]
                {
                    new ReachyDiagnosticsMetric("Active camera", "rear"),
                    ReachyDiagnosticsMetric.Degraded(
                        "Coverage",
                        "42%",
                        "Coverage is below the normal threshold."),
                    ReachyDiagnosticsMetric.Unavailable(
                        "Reprojection time",
                        "No timing source is bound."),
                });
            Equal(
                ReachyDiagnosticsAvailability.Unavailable,
                section.Availability,
                "section availability");
        }

        private static void SnapshotRendersEverySectionAndReason()
        {
            ReachyDiagnosticsSection Section(string title) =>
                new ReachyDiagnosticsSection(
                    title,
                    new[]
                    {
                        ReachyDiagnosticsMetric.Unavailable(
                            "metric",
                            title + " reason"),
                    });

            var snapshot = new ReachyDiagnosticsScreenSnapshot(
                Section("Simulation"),
                Section("Rendering"),
                Section("Camera"),
                Section("Providers"),
                Section("Versions"),
                Section("Device"));
            string display = snapshot.ToDisplayText();
            foreach (string title in new[]
                     {
                         "SIMULATION",
                         "RENDERING",
                         "CAMERA",
                         "PROVIDERS",
                         "VERSIONS",
                         "DEVICE",
                     })
            {
                Contains(display, "[" + title + "]");
            }
            Contains(display, "unavailable: Camera reason");
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

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected=" + expected + "; actual=" + actual);
            }
        }

        private static void Contains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Expected diagnostics text to contain: " + expected);
            }
        }
    }
}
