#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace ReachyMini.Diagnostics
{
    public sealed class ReachyDiagnosticLogger : IDisposable
    {
        public const int MaximumFields = 24;
        public const long DefaultRepeatWindowMilliseconds = 5000L;

        private static readonly string[] DiscriminatorKeys =
        {
            "provider",
            "status",
            "exception_type",
            "operation",
            "code",
            "error_code",
            "http_error_category",
        };

        private readonly object gate = new object();
        private readonly IReachyDiagnosticSink sink;
        private readonly IReachyMonotonicClock clock;
        private readonly long repeatWindowMilliseconds;
        private readonly Dictionary<string, BurstState> bursts =
            new Dictionary<string, BurstState>(StringComparer.Ordinal);
        private bool disposed;

        public ReachyDiagnosticLogger(
            IReachyDiagnosticSink diagnosticSink,
            IReachyMonotonicClock? monotonicClock = null,
            long repeatWindowMilliseconds = DefaultRepeatWindowMilliseconds)
        {
            sink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
            clock = monotonicClock ?? new ReachyStopwatchMonotonicClock();
            if (repeatWindowMilliseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(repeatWindowMilliseconds));
            }
            this.repeatWindowMilliseconds = repeatWindowMilliseconds;
        }

        public bool Emit(
            ReachyDiagnosticEventDescriptor descriptor,
            ReachyDiagnosticContext context,
            params ReachyDiagnosticField[] fields)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (fields.Length > MaximumFields)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fields),
                    fields.Length,
                    $"Diagnostics support at most {MaximumFields} fields per event.");
            }

            ReachyDiagnosticField[] redacted = Redact(fields);
            long now = clock.ElapsedMilliseconds;
            string burstKey = BuildBurstKey(descriptor, redacted);
            lock (gate)
            {
                ThrowIfDisposed();
                if (bursts.TryGetValue(burstKey, out BurstState? burst) && burst != null)
                {
                    if (now - burst.WindowStartMilliseconds < repeatWindowMilliseconds)
                    {
                        burst.OccurrenceCount = checked(burst.OccurrenceCount + 1UL);
                        burst.SuppressedCount = checked(burst.SuppressedCount + 1UL);
                        burst.LastMilliseconds = now;
                        return false;
                    }
                    EmitSummary(burst, now);
                }

                var next = new BurstState(
                    descriptor,
                    context,
                    redacted,
                    now);
                bursts[burstKey] = next;
                sink.Write(new ReachyDiagnosticRecord(
                    descriptor,
                    now,
                    context,
                    redacted,
                    occurrenceCount: 1UL,
                    suppressedCount: 0UL,
                    isRateLimitSummary: false));
                return true;
            }
        }

        public void Flush()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                long now = clock.ElapsedMilliseconds;
                foreach (BurstState burst in bursts.Values)
                {
                    EmitSummary(burst, now);
                }
                bursts.Clear();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                long now = clock.ElapsedMilliseconds;
                foreach (BurstState burst in bursts.Values)
                {
                    EmitSummary(burst, now);
                }
                bursts.Clear();
                disposed = true;
            }
        }

        private void EmitSummary(BurstState burst, long now)
        {
            if (burst.SuppressedCount == 0UL)
            {
                return;
            }
            sink.Write(new ReachyDiagnosticRecord(
                burst.Descriptor,
                now,
                burst.Context,
                burst.Fields,
                burst.OccurrenceCount,
                burst.SuppressedCount,
                isRateLimitSummary: true));
        }

        private static ReachyDiagnosticField[] Redact(
            ReachyDiagnosticField[] fields)
        {
            var redacted = new ReachyDiagnosticField[fields.Length];
            for (int index = 0; index < fields.Length; ++index)
            {
                redacted[index] = ReachyDiagnosticRedactor.Redact(fields[index]);
            }
            return redacted;
        }

        private static string BuildBurstKey(
            ReachyDiagnosticEventDescriptor descriptor,
            ReachyDiagnosticField[] fields)
        {
            var builder = new StringBuilder(256)
                .Append(descriptor.Component).Append('|')
                .Append(descriptor.EventId).Append('|')
                .Append(descriptor.ErrorCategory);
            foreach (string discriminator in DiscriminatorKeys)
            {
                for (int index = 0; index < fields.Length; ++index)
                {
                    if (string.Equals(
                            fields[index].Key,
                            discriminator,
                            StringComparison.Ordinal))
                    {
                        builder.Append('|')
                            .Append(discriminator)
                            .Append('=')
                            .Append(fields[index].Value);
                        break;
                    }
                }
            }
            return builder.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReachyDiagnosticLogger));
            }
        }

        private sealed class BurstState
        {
            public BurstState(
                ReachyDiagnosticEventDescriptor descriptor,
                ReachyDiagnosticContext context,
                ReachyDiagnosticField[] fields,
                long now)
            {
                Descriptor = descriptor;
                Context = context;
                Fields = fields;
                WindowStartMilliseconds = now;
                LastMilliseconds = now;
                OccurrenceCount = 1UL;
            }

            public ReachyDiagnosticEventDescriptor Descriptor { get; }
            public ReachyDiagnosticContext Context { get; }
            public ReachyDiagnosticField[] Fields { get; }
            public long WindowStartMilliseconds { get; }
            public long LastMilliseconds { get; set; }
            public ulong OccurrenceCount { get; set; }
            public ulong SuppressedCount { get; set; }
        }
    }
}
