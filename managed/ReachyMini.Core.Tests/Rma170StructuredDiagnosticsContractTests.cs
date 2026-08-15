#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Diagnostics;

namespace ReachyMini.Core.Tests
{
    internal static class Rma170StructuredDiagnosticsContractTests
    {
        public static void RunAll()
        {
            SecretAndPrivateFieldsAreRedacted();
            UrlCredentialsAndQueryAreNotRetained();
            RepeatedEventsPreserveFirstAndFinalCounts();
            BurstDiscriminatorsDoNotCollapseDistinctOperations();
            JsonIncludesStableIdentityAndCorrelation();
        }

        private static void SecretAndPrivateFieldsAreRedacted()
        {
            var sink = new CollectingSink();
            var clock = new FakeClock();
            using var logger = new ReachyDiagnosticLogger(sink, clock);
            logger.Emit(
                Descriptor(),
                new ReachyDiagnosticContext("session-1", "turn-2"),
                new ReachyDiagnosticField(
                    "api_key",
                    "secret-value",
                    ReachyDiagnosticDataClass.Secret),
                new ReachyDiagnosticField(
                    "transcript",
                    "private words",
                    ReachyDiagnosticDataClass.PrivateText));
            Equal(1, sink.Records.Count, "record count");
            foreach (ReachyDiagnosticField field in sink.Records[0].Fields)
            {
                Equal(
                    ReachyDiagnosticRedactor.RedactedValue,
                    field.Value,
                    field.Key);
            }
        }

        private static void UrlCredentialsAndQueryAreNotRetained()
        {
            ReachyDiagnosticField redacted = ReachyDiagnosticRedactor.Redact(
                new ReachyDiagnosticField(
                    "endpoint",
                    "https://user:pass@example.test/v1/chat?token=secret#fragment",
                    ReachyDiagnosticDataClass.Url));
            Equal("https://example.test/v1/chat", redacted.Value, "redacted URL");
        }

        private static void RepeatedEventsPreserveFirstAndFinalCounts()
        {
            var sink = new CollectingSink();
            var clock = new FakeClock();
            using var logger = new ReachyDiagnosticLogger(
                sink,
                clock,
                repeatWindowMilliseconds: 1000L);
            ReachyDiagnosticField operation = new ReachyDiagnosticField(
                "operation",
                "camera_start",
                ReachyDiagnosticDataClass.Identifier);

            True(logger.Emit(Descriptor(), default, operation), "first event emitted");
            clock.Advance(10L);
            False(logger.Emit(Descriptor(), default, operation), "duplicate suppressed");
            clock.Advance(10L);
            False(logger.Emit(Descriptor(), default, operation), "second duplicate suppressed");
            logger.Flush();

            Equal(2, sink.Records.Count, "first plus final summary");
            False(sink.Records[0].IsRateLimitSummary, "first event is not summary");
            True(sink.Records[1].IsRateLimitSummary, "final event is summary");
            Equal(3UL, sink.Records[1].OccurrenceCount, "occurrence count");
            Equal(2UL, sink.Records[1].SuppressedCount, "suppressed count");
        }

        private static void BurstDiscriminatorsDoNotCollapseDistinctOperations()
        {
            var sink = new CollectingSink();
            var clock = new FakeClock();
            using var logger = new ReachyDiagnosticLogger(sink, clock);
            True(
                logger.Emit(
                    Descriptor(),
                    default,
                    new ReachyDiagnosticField(
                        "operation",
                        "start",
                        ReachyDiagnosticDataClass.Identifier)),
                "start emitted");
            True(
                logger.Emit(
                    Descriptor(),
                    default,
                    new ReachyDiagnosticField(
                        "operation",
                        "stop",
                        ReachyDiagnosticDataClass.Identifier)),
                "stop emitted");
            Equal(2, sink.Records.Count, "distinct operation bursts");
        }

        private static void JsonIncludesStableIdentityAndCorrelation()
        {
            var record = new ReachyDiagnosticRecord(
                Descriptor(),
                42L,
                new ReachyDiagnosticContext("session-1", "turn-2"),
                new[]
                {
                    new ReachyDiagnosticField("operation", "start"),
                },
                1UL,
                0UL,
                false);
            string json = ReachyDiagnosticJsonFormatter.Format(record);
            Contains(json, "\"component\":\"camera\"");
            Contains(json, "\"event_id\":\"camera.operation.failed\"");
            Contains(json, "\"monotonic_ms\":42");
            Contains(json, "\"session_id\":\"session-1\"");
            Contains(json, "\"turn_id\":\"turn-2\"");
        }

        private static ReachyDiagnosticEventDescriptor Descriptor() =>
            new ReachyDiagnosticEventDescriptor(
                "camera",
                "camera.operation.failed",
                ReachyDiagnosticSeverity.Error,
                ReachyDiagnosticErrorCategory.Camera);

        private sealed class FakeClock : IReachyMonotonicClock
        {
            public long ElapsedMilliseconds { get; private set; }

            public void Advance(long milliseconds)
            {
                ElapsedMilliseconds = checked(ElapsedMilliseconds + milliseconds);
            }
        }

        private sealed class CollectingSink : IReachyDiagnosticSink
        {
            public List<ReachyDiagnosticRecord> Records { get; } =
                new List<ReachyDiagnosticRecord>();

            public void Write(ReachyDiagnosticRecord record)
            {
                Records.Add(record);
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + " expected true.");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(label + " expected false.");
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

        private static void Contains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Expected diagnostics JSON to contain: " + expected);
            }
        }
    }
}
