#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Application.Tests
{
    internal static class Rma081MainScreenStateTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            InteractionLabelsCoverEveryRequiredState();
            CapabilityAndPanelChangesAreRevisionedAndExclusive();
            UnavailableActionsRemainVisibleAndDiagnostic();
            SnapshotsRejectAmbiguousPanels();
            Console.WriteLine("RMA-081 main-screen state tests passed.");
        }

        private static void InteractionLabelsCoverEveryRequiredState()
        {
            var expected = new Dictionary<ReachyInteractionState, string>
            {
                [ReachyInteractionState.Idle] = "Idle",
                [ReachyInteractionState.Listening] = "Listening",
                [ReachyInteractionState.Transcribing] = "Transcribing",
                [ReachyInteractionState.Thinking] = "Thinking",
                [ReachyInteractionState.Speaking] = "Speaking",
                [ReachyInteractionState.Interrupted] = "Interrupted",
                [ReachyInteractionState.Unavailable] = "Unavailable",
                [ReachyInteractionState.Error] = "Error",
            };
            foreach (KeyValuePair<ReachyInteractionState, string> entry in expected)
            {
                Equal(
                    entry.Value,
                    ReachyMainScreenStateStore.GetInteractionLabel(entry.Key),
                    $"label for {entry.Key}");
            }
            Equal(
                "Local",
                ReachyMainScreenStateStore.GetProviderLocationLabel(
                    ReachyProviderLocation.Local),
                "local provider label");
            Equal(
                "Cloud",
                ReachyMainScreenStateStore.GetProviderLocationLabel(
                    ReachyProviderLocation.Cloud),
                "cloud provider label");
        }

        private static void CapabilityAndPanelChangesAreRevisionedAndExclusive()
        {
            var store = new ReachyMainScreenStateStore();
            ReachyMainScreenSnapshot initial = store.Current;
            int changeCount = 0;
            store.Changed += (_, args) =>
            {
                ++changeCount;
                True(args.Snapshot.Revision > initial.Revision, "event revision");
            };

            store.SetCapabilities(
                "Fixed front / three-quarter",
                false,
                "On-device test provider",
                ReachyProviderLocation.Local,
                true);
            ReachyMainScreenSnapshot capabilities = store.Current;
            Equal("Not configured", initial.ActiveProvider, "old snapshot unchanged");
            Equal(
                "On-device test provider",
                capabilities.ActiveProvider,
                "provider update");
            Equal(
                ReachyProviderLocation.Local,
                capabilities.ProviderLocation,
                "provider location update");
            True(capabilities.MicrophoneAvailable, "microphone capability");

            store.ShowSettings("Settings opened.");
            True(store.Current.SettingsVisible, "settings visible");
            False(store.Current.DiagnosticsVisible, "diagnostics hidden by settings");
            store.ShowDiagnostics("Diagnostics opened.");
            False(store.Current.SettingsVisible, "settings hidden by diagnostics");
            True(store.Current.DiagnosticsVisible, "diagnostics visible");
            store.HidePanels("Panels closed.");
            False(store.Current.SettingsVisible, "settings closed");
            False(store.Current.DiagnosticsVisible, "diagnostics closed");
            Equal(4, changeCount, "revision event count");
        }

        private static void UnavailableActionsRemainVisibleAndDiagnostic()
        {
            var store = new ReachyMainScreenStateStore();
            store.ReportUnavailableAction(
                "Microphone",
                "audio capture is not installed");
            Equal(
                ReachyInteractionState.Unavailable,
                store.Current.InteractionState,
                "unavailable state");
            Contains(store.Current.Detail, "Microphone", "action identity");
            Contains(store.Current.Detail, "not installed", "action explanation");
        }

        private static void SnapshotsRejectAmbiguousPanels()
        {
            Throws<ArgumentException>(
                () =>
                {
                    ReachyMainScreenSnapshot snapshot = new ReachyMainScreenSnapshot(
                        ReachyInteractionState.Idle,
                        "Ready.",
                        "Fixed front / three-quarter",
                        false,
                        "Not configured",
                        ReachyProviderLocation.Unavailable,
                        false,
                        true,
                        true,
                        1UL);
                    GC.KeepAlive(snapshot);
                },
                "ambiguous panels");
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
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

        private static void Contains(string value, string expected, string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' lacks '{expected}'.");
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
