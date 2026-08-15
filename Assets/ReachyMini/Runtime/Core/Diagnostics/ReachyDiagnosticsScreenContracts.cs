#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace ReachyMini.Diagnostics
{
    public enum ReachyDiagnosticsAvailability
    {
        Available = 0,
        Degraded = 1,
        Unavailable = 2,
    }

    public sealed class ReachyDiagnosticsMetric
    {
        public ReachyDiagnosticsMetric(
            string label,
            string value,
            ReachyDiagnosticsAvailability availability =
                ReachyDiagnosticsAvailability.Available,
            string reason = "")
        {
            Label = RequireText(label, nameof(label), 64);
            if (!Enum.IsDefined(typeof(ReachyDiagnosticsAvailability), availability))
            {
                throw new ArgumentOutOfRangeException(nameof(availability));
            }

            Availability = availability;
            if (availability == ReachyDiagnosticsAvailability.Unavailable)
            {
                Value = "unavailable";
                Reason = RequireText(reason, nameof(reason), 256);
            }
            else
            {
                Value = RequireText(value, nameof(value), 160);
                Reason = availability == ReachyDiagnosticsAvailability.Degraded
                    ? RequireText(reason, nameof(reason), 256)
                    : BoundOptional(reason, 256);
            }
        }

        public string Label { get; }

        public string Value { get; }

        public ReachyDiagnosticsAvailability Availability { get; }

        public string Reason { get; }

        public static ReachyDiagnosticsMetric Unavailable(
            string label,
            string reason)
        {
            return new ReachyDiagnosticsMetric(
                label,
                "unavailable",
                ReachyDiagnosticsAvailability.Unavailable,
                reason);
        }

        public static ReachyDiagnosticsMetric Degraded(
            string label,
            string value,
            string reason)
        {
            return new ReachyDiagnosticsMetric(
                label,
                value,
                ReachyDiagnosticsAvailability.Degraded,
                reason);
        }

        private static string RequireText(
            string value,
            string parameterName,
            int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Diagnostics text cannot be empty.",
                    parameterName);
            }
            return BoundOptional(value, maximumCharacters);
        }

        private static string BoundOptional(string? value, int maximumCharacters)
        {
            string text = value ?? string.Empty;
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= maximumCharacters
                ? text
                : text.Substring(0, maximumCharacters - 1) + "…";
        }
    }

    public sealed class ReachyDiagnosticsSection
    {
        private const int MaximumMetrics = 24;
        private readonly ReachyDiagnosticsMetric[] metrics;

        public ReachyDiagnosticsSection(
            string title,
            IReadOnlyList<ReachyDiagnosticsMetric> sectionMetrics)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException(
                    "A diagnostics section requires a title.",
                    nameof(title));
            }
            if (sectionMetrics == null)
            {
                throw new ArgumentNullException(nameof(sectionMetrics));
            }
            if (sectionMetrics.Count == 0 || sectionMetrics.Count > MaximumMetrics)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sectionMetrics),
                    sectionMetrics.Count,
                    $"A diagnostics section requires 1-{MaximumMetrics} metrics.");
            }

            Title = title.Trim();
            metrics = new ReachyDiagnosticsMetric[sectionMetrics.Count];
            ReachyDiagnosticsAvailability availability =
                ReachyDiagnosticsAvailability.Available;
            for (int index = 0; index < sectionMetrics.Count; ++index)
            {
                ReachyDiagnosticsMetric metric = sectionMetrics[index] ??
                    throw new ArgumentException(
                        "Diagnostics sections cannot contain null metrics.",
                        nameof(sectionMetrics));
                metrics[index] = metric;
                if ((int)metric.Availability > (int)availability)
                {
                    availability = metric.Availability;
                }
            }
            Availability = availability;
        }

        public string Title { get; }

        public ReachyDiagnosticsAvailability Availability { get; }

        public IReadOnlyList<ReachyDiagnosticsMetric> Metrics =>
            Array.AsReadOnly(metrics);
    }

    public sealed class ReachyDiagnosticsScreenSnapshot
    {
        public ReachyDiagnosticsScreenSnapshot(
            ReachyDiagnosticsSection simulation,
            ReachyDiagnosticsSection rendering,
            ReachyDiagnosticsSection camera,
            ReachyDiagnosticsSection providers,
            ReachyDiagnosticsSection versions,
            ReachyDiagnosticsSection device)
        {
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            Rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
            Versions = versions ?? throw new ArgumentNullException(nameof(versions));
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public ReachyDiagnosticsSection Simulation { get; }

        public ReachyDiagnosticsSection Rendering { get; }

        public ReachyDiagnosticsSection Camera { get; }

        public ReachyDiagnosticsSection Providers { get; }

        public ReachyDiagnosticsSection Versions { get; }

        public ReachyDiagnosticsSection Device { get; }

        public IReadOnlyList<ReachyDiagnosticsSection> Sections =>
            new[] { Simulation, Rendering, Camera, Providers, Versions, Device };

        public string ToDisplayText()
        {
            var builder = new StringBuilder(2048);
            IReadOnlyList<ReachyDiagnosticsSection> sections = Sections;
            for (int sectionIndex = 0; sectionIndex < sections.Count; ++sectionIndex)
            {
                ReachyDiagnosticsSection section = sections[sectionIndex];
                if (sectionIndex > 0)
                {
                    builder.AppendLine();
                }
                builder.Append('[')
                    .Append(section.Title.ToUpperInvariant())
                    .Append("] ")
                    .AppendLine(section.Availability.ToString());
                for (int metricIndex = 0; metricIndex < section.Metrics.Count; ++metricIndex)
                {
                    ReachyDiagnosticsMetric metric = section.Metrics[metricIndex];
                    builder.Append("• ")
                        .Append(metric.Label)
                        .Append(": ")
                        .Append(metric.Value);
                    if (metric.Availability != ReachyDiagnosticsAvailability.Available)
                    {
                        builder.Append(" — ")
                            .Append(metric.Availability.ToString().ToLowerInvariant())
                            .Append(": ")
                            .Append(metric.Reason);
                    }
                    builder.AppendLine();
                }
            }
            return builder.ToString().TrimEnd();
        }

        public static ReachyDiagnosticsScreenSnapshot FromLegacyText(string text)
        {
            string reason = string.IsNullOrWhiteSpace(text)
                ? "Legacy diagnostics returned no detail."
                : text;
            ReachyDiagnosticsMetric unavailable =
                ReachyDiagnosticsMetric.Unavailable(
                    "Legacy diagnostics",
                    reason);
            return new ReachyDiagnosticsScreenSnapshot(
                SingleUnavailable("Simulation", unavailable),
                SingleUnavailable("Rendering", unavailable),
                SingleUnavailable("Camera", unavailable),
                SingleUnavailable("Providers", unavailable),
                SingleUnavailable("Versions", unavailable),
                SingleUnavailable("Device", unavailable));
        }

        private static ReachyDiagnosticsSection SingleUnavailable(
            string title,
            ReachyDiagnosticsMetric metric)
        {
            return new ReachyDiagnosticsSection(
                title,
                new[] { metric });
        }
    }
}
