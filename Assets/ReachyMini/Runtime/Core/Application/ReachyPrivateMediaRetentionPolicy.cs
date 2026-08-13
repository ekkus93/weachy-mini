#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyPrivateMediaKind
    {
        RawCameraFrame = 0,
        MicrophoneAudio = 1,
        CloudRequestMedia = 2,
    }

    public sealed class ReachyConversationHistoryRetention
    {
        internal ReachyConversationHistoryRetention(bool historyEnabled, int configuredRetentionDays)
        {
            HistoryEnabled = historyEnabled;
            ConfiguredRetentionDays = configuredRetentionDays;
            PersistenceEnabled = historyEnabled && configuredRetentionDays > 0;
            MaximumAgeDays = PersistenceEnabled ? configuredRetentionDays : 0;
        }

        public bool HistoryEnabled { get; }
        public int ConfiguredRetentionDays { get; }
        public bool PersistenceEnabled { get; }
        public int MaximumAgeDays { get; }
        public bool SessionOnly => !PersistenceEnabled;
    }

    public static class ReachyPrivateMediaRetentionPolicy
    {
        public const string PersistentMediaRetentionUnavailableReason =
            "camera, microphone, and network-request media retention is disabled until an explicit recording/export consent flow exists";

        private static readonly int[] SupportedHistoryRetentionDays = { 0, 7, 30, 90 };

        public static bool RecordingEnabled => false;
        public static bool MediaExportEnabled => false;

        public static bool IsPersistentMediaRetentionAllowed(ReachyPrivateMediaKind kind)
        {
            ValidateMediaKind(kind);
            return false;
        }

        public static void RequirePersistentMediaRetentionAllowed(ReachyPrivateMediaKind kind)
        {
            ValidateMediaKind(kind);
            throw new InvalidOperationException(
                "Persistent media retention is unavailable: " +
                PersistentMediaRetentionUnavailableReason + ".");
        }

        public static ReachyConversationHistoryRetention GetConversationHistoryRetention(
            bool historyEnabled,
            int configuredRetentionDays)
        {
            if (!IsSupportedHistoryRetentionDays(configuredRetentionDays))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuredRetentionDays),
                    configuredRetentionDays,
                    "Conversation-history retention must use a supported bounded period.");
            }
            return new ReachyConversationHistoryRetention(historyEnabled, configuredRetentionDays);
        }

        public static bool IsSupportedHistoryRetentionDays(int retentionDays)
        {
            for (int index = 0; index < SupportedHistoryRetentionDays.Length; ++index)
            {
                if (SupportedHistoryRetentionDays[index] == retentionDays)
                {
                    return true;
                }
            }
            return false;
        }

        public static int GetNextHistoryRetentionDays(int currentRetentionDays)
        {
            for (int index = 0; index < SupportedHistoryRetentionDays.Length; ++index)
            {
                if (SupportedHistoryRetentionDays[index] == currentRetentionDays)
                {
                    return SupportedHistoryRetentionDays[
                        (index + 1) % SupportedHistoryRetentionDays.Length];
                }
            }
            throw new ArgumentOutOfRangeException(
                nameof(currentRetentionDays),
                currentRetentionDays,
                "Conversation-history retention must use a supported bounded period.");
        }

        private static void ValidateMediaKind(ReachyPrivateMediaKind kind)
        {
            if (!Enum.IsDefined(typeof(ReachyPrivateMediaKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported media kind.");
            }
        }
    }
}
