#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ReachyMini.Diagnostics
{
    public enum ReachyDiagnosticSeverity
    {
        Trace = 0,
        Information = 1,
        Warning = 2,
        Error = 3,
        Critical = 4,
    }

    public enum ReachyDiagnosticErrorCategory
    {
        None = 0,
        Validation = 1,
        Configuration = 2,
        Lifecycle = 3,
        Provider = 4,
        Network = 5,
        Timeout = 6,
        Cancellation = 7,
        Native = 8,
        Camera = 9,
        Rendering = 10,
        Storage = 11,
        Resource = 12,
        Unknown = 13,
    }

    public enum ReachyDiagnosticDataClass
    {
        Public = 0,
        Identifier = 1,
        Url = 2,
        Header = 3,
        Secret = 4,
        PrivateText = 5,
        RawAudio = 6,
        RawImage = 7,
        RawMedia = 8,
    }

    public sealed class ReachyDiagnosticEventDescriptor
    {
        public ReachyDiagnosticEventDescriptor(
            string component,
            string eventId,
            ReachyDiagnosticSeverity severity,
            ReachyDiagnosticErrorCategory errorCategory)
        {
            Component = RequireToken(component, nameof(component));
            EventId = RequireToken(eventId, nameof(eventId));
            if (!Enum.IsDefined(typeof(ReachyDiagnosticSeverity), severity))
            {
                throw new ArgumentOutOfRangeException(nameof(severity));
            }
            if (!Enum.IsDefined(typeof(ReachyDiagnosticErrorCategory), errorCategory))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCategory));
            }
            Severity = severity;
            ErrorCategory = errorCategory;
        }

        public string Component { get; }
        public string EventId { get; }
        public ReachyDiagnosticSeverity Severity { get; }
        public ReachyDiagnosticErrorCategory ErrorCategory { get; }

        private static string RequireToken(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            {
                throw new ArgumentException(
                    "Diagnostic identifiers require 1-64 characters.",
                    name);
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char current = value[index];
                if (!(char.IsLetterOrDigit(current) ||
                      current == '.' || current == '_' || current == '-'))
                {
                    throw new ArgumentException(
                        "Diagnostic identifiers may contain letters, digits, '.', '_' and '-' only.",
                        name);
                }
            }
            return value;
        }
    }

    public readonly struct ReachyDiagnosticContext
    {
        public ReachyDiagnosticContext(string? sessionId, string? turnId)
        {
            SessionId = BoundIdentifier(sessionId);
            TurnId = BoundIdentifier(turnId);
        }

        public string SessionId { get; }
        public string TurnId { get; }

        private static string BoundIdentifier(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > 96)
            {
                normalized = normalized.Substring(0, 96);
            }
            return normalized;
        }
    }

    public readonly struct ReachyDiagnosticField
    {
        public ReachyDiagnosticField(
            string key,
            string? value,
            ReachyDiagnosticDataClass dataClass = ReachyDiagnosticDataClass.Public)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            {
                throw new ArgumentException(
                    "Diagnostic field keys require 1-64 characters.",
                    nameof(key));
            }
            if (!Enum.IsDefined(typeof(ReachyDiagnosticDataClass), dataClass))
            {
                throw new ArgumentOutOfRangeException(nameof(dataClass));
            }
            Key = key;
            Value = value ?? string.Empty;
            DataClass = dataClass;
        }

        public string Key { get; }
        public string Value { get; }
        public ReachyDiagnosticDataClass DataClass { get; }
    }

    public sealed class ReachyDiagnosticRecord
    {
        public ReachyDiagnosticRecord(
            ReachyDiagnosticEventDescriptor descriptor,
            long monotonicMilliseconds,
            ReachyDiagnosticContext context,
            IReadOnlyList<ReachyDiagnosticField> fields,
            ulong occurrenceCount,
            ulong suppressedCount,
            bool isRateLimitSummary)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (monotonicMilliseconds < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds));
            }
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (occurrenceCount == 0UL || suppressedCount >= occurrenceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(occurrenceCount));
            }
            var copied = new ReachyDiagnosticField[fields.Count];
            for (int index = 0; index < fields.Count; ++index)
            {
                copied[index] = fields[index];
            }
            MonotonicMilliseconds = monotonicMilliseconds;
            Context = context;
            Fields = Array.AsReadOnly(copied);
            OccurrenceCount = occurrenceCount;
            SuppressedCount = suppressedCount;
            IsRateLimitSummary = isRateLimitSummary;
        }

        public ReachyDiagnosticEventDescriptor Descriptor { get; }
        public long MonotonicMilliseconds { get; }
        public ReachyDiagnosticContext Context { get; }
        public IReadOnlyList<ReachyDiagnosticField> Fields { get; }
        public ulong OccurrenceCount { get; }
        public ulong SuppressedCount { get; }
        public bool IsRateLimitSummary { get; }
    }

    public interface IReachyDiagnosticSink
    {
        void Write(ReachyDiagnosticRecord record);
    }

    public interface IReachyMonotonicClock
    {
        long ElapsedMilliseconds { get; }
    }

    public sealed class ReachyStopwatchMonotonicClock : IReachyMonotonicClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;
    }

    public static class ReachyDiagnosticEventIds
    {
        public const string ProviderHttpFailed = "provider.http.failed";
        public const string AndroidBridgeFailed = "android.bridge.failed";
        public const string CameraBootstrapFailed = "camera.bootstrap.failed";
        public const string CameraUiOperationFailed = "camera.ui_operation.failed";
        public const string ApplicationDisposalFailed = "application.disposal.failed";
        public const string ApplicationFaulted = "application.faulted";
        public const string ApplicationLowMemoryHandled = "application.memory.low_handled";
        public const string ApplicationStartupFailed = "application.startup.failed";
        public const string MainScreenBootstrapFailed = "ui.bootstrap.failed";
        public const string ProductionRuntimeUnavailable = "runtime.unavailable";
        public const string ProductionRuntimeStarted = "runtime.started";
        public const string ProductionRuntimeFaulted = "runtime.faulted";
        public const string ProductionRuntimeShutdownFailed = "runtime.shutdown.failed";
        public const string RendererFaulted = "renderer.faulted";
        public const string DiagnosticBundleExportStarted = "diagnostics.bundle.export_started";
        public const string DiagnosticBundleExportSucceeded = "diagnostics.bundle.export_succeeded";
        public const string DiagnosticBundleExportFailed = "diagnostics.bundle.export_failed";
        public const string StorageCleanupSucceeded = "storage.cleanup.succeeded";
        public const string StorageCleanupFailed = "storage.cleanup.failed";
    }
}
