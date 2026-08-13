#nullable enable

using System;
using System.IO;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Application.Tests
{
    internal static class Rma162PrivateMediaRetentionTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            PersistentMediaRetentionDefaultsToDenied();
            ConversationHistoryRequiresOptInAndBound();
            TemporaryMediaLeaseDeletesPromptly();
            AbandonedTemporaryMediaIsPurged();
            Console.WriteLine("RMA-162 private-media retention tests passed.");
        }

        private static void PersistentMediaRetentionDefaultsToDenied()
        {
            False(ReachyPrivateMediaRetentionPolicy.RecordingEnabled, "recording default");
            False(ReachyPrivateMediaRetentionPolicy.MediaExportEnabled, "media export default");

            foreach (ReachyPrivateMediaKind kind in Enum.GetValues<ReachyPrivateMediaKind>())
            {
                False(
                    ReachyPrivateMediaRetentionPolicy.IsPersistentMediaRetentionAllowed(kind),
                    kind + " persistence default");
                Throws<InvalidOperationException>(
                    () => ReachyPrivateMediaRetentionPolicy.RequirePersistentMediaRetentionAllowed(
                        kind),
                    kind + " persistent retention gate");
            }
        }

        private static void ConversationHistoryRequiresOptInAndBound()
        {
            ReachyConversationHistoryRetention disabled =
                ReachyPrivateMediaRetentionPolicy.GetConversationHistoryRetention(
                    historyEnabled: false,
                    configuredRetentionDays: 30);
            False(disabled.PersistenceEnabled, "disabled history persistence");
            True(disabled.SessionOnly, "disabled history session-only contract");
            Equal(0, disabled.MaximumAgeDays, "disabled history maximum age");

            ReachyConversationHistoryRetention sessionOnly =
                ReachyPrivateMediaRetentionPolicy.GetConversationHistoryRetention(
                    historyEnabled: true,
                    configuredRetentionDays: 0);
            False(sessionOnly.PersistenceEnabled, "zero-day history persistence");
            True(sessionOnly.SessionOnly, "zero-day history session-only contract");

            foreach (int days in new[] { 7, 30, 90 })
            {
                ReachyConversationHistoryRetention bounded =
                    ReachyPrivateMediaRetentionPolicy.GetConversationHistoryRetention(
                        historyEnabled: true,
                        configuredRetentionDays: days);
                True(bounded.PersistenceEnabled, days + "-day history persistence");
                False(bounded.SessionOnly, days + "-day history session-only contract");
                Equal(days, bounded.MaximumAgeDays, days + "-day maximum age");
            }

            Throws<ArgumentOutOfRangeException>(
                () => ReachyPrivateMediaRetentionPolicy.GetConversationHistoryRetention(
                    historyEnabled: true,
                    configuredRetentionDays: 365),
                "unbounded history retention");
        }

        private static void TemporaryMediaLeaseDeletesPromptly()
        {
            string root = CreateTemporaryRoot();
            try
            {
                var store = new ReachyPrivateMediaTemporaryFileStore(root);
                byte[] content = { 11, 22, 33, 44 };
                string path;
                using (ReachyPrivateMediaTemporaryFileLease lease = store.Create(
                           ReachyPrivateMediaKind.MicrophoneAudio,
                           content))
                {
                    path = lease.Path;
                    True(File.Exists(path), "temporary media exists during lease");
                    True(
                        IsInside(path, store.RootPath),
                        "temporary media remains under dedicated cache root");
                }
                False(File.Exists(path), "temporary media deleted on lease disposal");
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static void AbandonedTemporaryMediaIsPurged()
        {
            string root = CreateTemporaryRoot();
            try
            {
                var store = new ReachyPrivateMediaTemporaryFileStore(root);
                Directory.CreateDirectory(store.RootPath);
                string stalePath = Path.Combine(store.RootPath, "stale.media");
                File.WriteAllBytes(stalePath, new byte[] { 1, 2, 3 });

                Equal(1, store.PurgeAbandonedFiles(), "abandoned media purge count");
                False(File.Exists(stalePath), "abandoned media purge deletion");
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateTemporaryRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "weachy-rma162-" + Guid.NewGuid().ToString("N"));
        }

        private static bool IsInside(string path, string root)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.Ordinal);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected false.");
            }
        }

        private static void Throws<TException>(Action action, string label)
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
                $"Managed test failed for {label}: expected {typeof(TException).Name}.");
        }
    }
}
