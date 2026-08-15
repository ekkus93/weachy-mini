#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Diagnostics;
using UnityEngine;

namespace ReachyMini.RuntimeDiagnostics
{
    public static class ReachyRuntimeDiagnostics
    {
        private static readonly object Gate = new object();
        private static ReachyDiagnosticContext context =
            new ReachyDiagnosticContext(string.Empty, string.Empty);
        private static ReachyDiagnosticRecordBuffer recordBuffer =
            new ReachyDiagnosticRecordBuffer();
        private static ReachyDiagnosticLogger logger = CreateDefaultLogger();

        public static void SetContext(string? sessionId, string? turnId)
        {
            lock (Gate)
            {
                context = new ReachyDiagnosticContext(sessionId, turnId);
            }
        }

        public static bool Emit(
            string component,
            string eventId,
            ReachyDiagnosticSeverity severity,
            ReachyDiagnosticErrorCategory errorCategory,
            params ReachyDiagnosticField[] fields)
        {
            ReachyDiagnosticContext current;
            lock (Gate)
            {
                current = context;
            }
            return logger.Emit(
                new ReachyDiagnosticEventDescriptor(
                    component,
                    eventId,
                    severity,
                    errorCategory),
                current,
                fields);
        }

        public static void Flush()
        {
            logger.Flush();
        }

        public static IReadOnlyList<ReachyDiagnosticRecord> CaptureRecentRecords()
        {
            return recordBuffer.Snapshot();
        }

        public static ulong DroppedCapturedRecordCount => recordBuffer.DroppedCount;

        internal static void ResetForTests(
            IReachyDiagnosticSink sink,
            IReachyMonotonicClock clock)
        {
            lock (Gate)
            {
                logger.Dispose();
                recordBuffer = new ReachyDiagnosticRecordBuffer();
                logger = new ReachyDiagnosticLogger(
                    new ReachyCompositeDiagnosticSink(
                        new IReachyDiagnosticSink[] { sink, recordBuffer }),
                    clock);
                context = new ReachyDiagnosticContext(string.Empty, string.Empty);
            }
        }

        private static ReachyDiagnosticLogger CreateDefaultLogger()
        {
            return new ReachyDiagnosticLogger(
                new ReachyCompositeDiagnosticSink(
                    new IReachyDiagnosticSink[]
                    {
                        new UnityDiagnosticSink(),
                        recordBuffer,
                    }));
        }

        private sealed class UnityDiagnosticSink : IReachyDiagnosticSink
        {
            public void Write(ReachyDiagnosticRecord record)
            {
                string json = ReachyDiagnosticJsonFormatter.Format(record);
                switch (record.Descriptor.Severity)
                {
                    case ReachyDiagnosticSeverity.Trace:
                    case ReachyDiagnosticSeverity.Information:
                        Debug.Log(json);
                        break;
                    case ReachyDiagnosticSeverity.Warning:
                        Debug.LogWarning(json);
                        break;
                    case ReachyDiagnosticSeverity.Error:
                    case ReachyDiagnosticSeverity.Critical:
                        Debug.LogError(json);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
