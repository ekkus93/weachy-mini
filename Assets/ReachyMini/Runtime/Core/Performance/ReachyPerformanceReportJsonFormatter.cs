#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace ReachyMini.Performance
{
    public static class ReachyPerformanceReportJsonFormatter
    {
        public static string Format(ReachyPerformanceReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var builder = new StringBuilder(8192);
            builder.Append('{');
            AppendNumber(builder, "schema_version", report.SchemaVersion);
            builder.Append(',');
            AppendString(builder, "label", report.Label);
            builder.Append(',');
            AppendNumber(builder, "target_fps", report.TargetFramesPerSecond);
            builder.Append(',');
            AppendNumber(builder, "duration_seconds", report.DurationSeconds);
            builder.Append(",\"timings\":[");
            for (int index = 0; index < report.Timings.Count; ++index)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                ReachyPerformanceTimingSummary timing = report.Timings[index];
                builder.Append('{');
                AppendString(builder, "workload", timing.Workload.ToString());
                builder.Append(',');
                AppendString(builder, "availability", timing.Availability.ToString());
                builder.Append(',');
                AppendString(builder, "availability_reason", timing.AvailabilityReason);
                builder.Append(',');
                AppendNumber(builder, "sample_count", timing.SampleCount);
                builder.Append(',');
                AppendBoolean(builder, "percentiles_approximate", timing.PercentilesApproximate);
                builder.Append(',');
                AppendNumber(builder, "median_ms", timing.MedianMilliseconds);
                builder.Append(',');
                AppendNumber(builder, "p95_ms", timing.P95Milliseconds);
                builder.Append(',');
                AppendNumber(builder, "p99_ms", timing.P99Milliseconds);
                builder.Append(',');
                AppendNumber(builder, "max_ms", timing.MaximumMilliseconds);
                builder.Append('}');
            }
            builder.Append(']');
            builder.Append(",\"resources\":");
            AppendResourceSummary(builder, report.Resources);
            builder.Append(",\"resource_samples\":[");
            for (int index = 0; index < report.ResourceSamples.Count; ++index)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                AppendResourceSample(builder, report.ResourceSamples[index]);
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendResourceSummary(
            StringBuilder builder,
            ReachyPerformanceResourceSummary summary)
        {
            builder.Append('{');
            AppendNumber(builder, "sample_count", summary.SampleCount);
            builder.Append(',');
            AppendNumber(builder, "dropped_sample_count", summary.DroppedSampleCount);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "initial_unity_allocated_memory_bytes",
                summary.InitialUnityAllocatedMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "final_unity_allocated_memory_bytes",
                summary.FinalUnityAllocatedMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "maximum_unity_allocated_memory_bytes",
                summary.MaximumUnityAllocatedMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "minimum_system_available_memory_bytes",
                summary.MinimumSystemAvailableMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "initial_battery_level_fraction",
                summary.InitialBatteryLevelFraction);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "final_battery_level_fraction",
                summary.FinalBatteryLevelFraction);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "battery_discharge_fraction",
                summary.BatteryDischargeFraction);
            builder.Append(',');
            AppendString(builder, "peak_thermal_state", summary.PeakThermalState);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "peak_thermal_severity",
                summary.PeakThermalSeverity);
            builder.Append('}');
        }

        private static void AppendResourceSample(
            StringBuilder builder,
            ReachyPerformanceResourceSample sample)
        {
            builder.Append('{');
            AppendNumber(builder, "monotonic_seconds", sample.MonotonicSeconds);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "unity_allocated_memory_bytes",
                sample.UnityAllocatedMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "system_available_memory_bytes",
                sample.SystemAvailableMemoryBytes);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "battery_level_fraction",
                sample.BatteryLevelFraction);
            builder.Append(',');
            AppendNullableNumber(
                builder,
                "thermal_severity",
                sample.ThermalSeverity);
            builder.Append(',');
            AppendString(builder, "thermal_state", sample.ThermalState);
            builder.Append(',');
            AppendString(builder, "unavailable_reason", sample.UnavailableReason);
            builder.Append('}');
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendName(builder, name);
            builder.Append('"');
            AppendEscaped(builder, value ?? string.Empty);
            builder.Append('"');
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendName(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendName(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            int value)
        {
            AppendName(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            double value)
        {
            AppendName(builder, name);
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendNullableNumber(
            StringBuilder builder,
            string name,
            long? value)
        {
            AppendName(builder, name);
            builder.Append(value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
        }

        private static void AppendNullableNumber(
            StringBuilder builder,
            string name,
            int? value)
        {
            AppendName(builder, name);
            builder.Append(value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
        }

        private static void AppendNullableNumber(
            StringBuilder builder,
            string name,
            double? value)
        {
            AppendName(builder, name);
            builder.Append(value.HasValue
                ? value.Value.ToString("R", CultureInfo.InvariantCulture)
                : "null");
        }

        private static void AppendName(StringBuilder builder, string name)
        {
            builder.Append('"');
            builder.Append(name);
            builder.Append("\":");
        }

        private static void AppendEscaped(StringBuilder builder, string value)
        {
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
        }
    }
}
