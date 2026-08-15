#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Diagnostics
{
    public sealed class ReachyDiagnosticRecordBuffer : IReachyDiagnosticSink
    {
        public const int DefaultCapacity = 512;
        public const int MaximumCapacity = 4096;

        private readonly object gate = new object();
        private readonly ReachyDiagnosticRecord?[] records;
        private int nextIndex;
        private int count;
        private ulong droppedCount;

        public ReachyDiagnosticRecordBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0 || capacity > MaximumCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    $"Diagnostic record buffers require 1-{MaximumCapacity} records.");
            }
            records = new ReachyDiagnosticRecord?[capacity];
        }

        public int Capacity => records.Length;

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return count;
                }
            }
        }

        public ulong DroppedCount
        {
            get
            {
                lock (gate)
                {
                    return droppedCount;
                }
            }
        }

        public void Write(ReachyDiagnosticRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            lock (gate)
            {
                if (count == records.Length)
                {
                    droppedCount = checked(droppedCount + 1UL);
                }
                else
                {
                    ++count;
                }

                records[nextIndex] = record;
                nextIndex = (nextIndex + 1) % records.Length;
            }
        }

        public IReadOnlyList<ReachyDiagnosticRecord> Snapshot()
        {
            lock (gate)
            {
                var result = new ReachyDiagnosticRecord[count];
                int start = count == records.Length ? nextIndex : 0;
                for (int index = 0; index < count; ++index)
                {
                    ReachyDiagnosticRecord? record =
                        records[(start + index) % records.Length];
                    result[index] = record ?? throw new InvalidOperationException(
                        "Diagnostic record buffer contained an empty retained slot.");
                }
                return Array.AsReadOnly(result);
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                Array.Clear(records, 0, records.Length);
                nextIndex = 0;
                count = 0;
                droppedCount = 0UL;
            }
        }
    }

    public sealed class ReachyCompositeDiagnosticSink : IReachyDiagnosticSink
    {
        private readonly IReachyDiagnosticSink[] sinks;

        public ReachyCompositeDiagnosticSink(
            IReadOnlyList<IReachyDiagnosticSink> diagnosticSinks)
        {
            if (diagnosticSinks == null)
            {
                throw new ArgumentNullException(nameof(diagnosticSinks));
            }
            if (diagnosticSinks.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diagnosticSinks),
                    "Composite diagnostic sinks require at least one target.");
            }

            sinks = new IReachyDiagnosticSink[diagnosticSinks.Count];
            for (int index = 0; index < diagnosticSinks.Count; ++index)
            {
                sinks[index] = diagnosticSinks[index] ??
                    throw new ArgumentException(
                        "Composite diagnostic sinks cannot contain null targets.",
                        nameof(diagnosticSinks));
            }
        }

        public void Write(ReachyDiagnosticRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            for (int index = 0; index < sinks.Length; ++index)
            {
                sinks[index].Write(record);
            }
        }
    }
}
