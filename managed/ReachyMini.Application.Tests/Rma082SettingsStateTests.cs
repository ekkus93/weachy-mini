#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Application.Tests
{
    internal static class Rma082SettingsStateTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ProviderSelectionsAreIndependentAndNetworkTruthful();
            PrivacySummaryTracksCloudBoundSelections();
            DurableSettingsRoundTripAndRejectInvalidValues();
            SectionsActionsAndAttributionsAreComplete();
            Console.WriteLine("RMA-082 settings state tests passed.");
        }

        private static void ProviderSelectionsAreIndependentAndNetworkTruthful()
        {
            var store = new ReachySettingsStateStore();
            ReachySettingsSnapshot initial = store.Current;
            Equal(4, initial.ProviderSelections.Count, "provider slot count");

            store.CycleProvider(ReachyProviderKind.Asr);
            Equal(
                ReachyProviderExecution.OnDevice,
                store.Current.GetProvider(ReachyProviderKind.Asr).Execution,
                "local ASR preference");
            Equal(
                ReachyProviderExecution.Unconfigured,
                store.Current.GetProvider(ReachyProviderKind.Tts).Execution,
                "TTS remains independent");

            store.CycleProvider(ReachyProviderKind.Asr);
            ReachyProviderSelection androidAsr =
                store.Current.GetProvider(ReachyProviderKind.Asr);
            Equal(
                ReachyProviderExecution.AndroidService,
                androidAsr.Execution,
                "Android ASR preference");
            Equal(
                ReachyConnectivityRequirement.NetworkRequired,
                androidAsr.Connectivity,
                "Android service network truth");
            True(androidAsr.SendsDataOffDevice, "Android off-device indicator");
            Contains(
                store.Current.SpeechNetworkStatus,
                "Network required",
                "speech network status");

            Throws<ArgumentException>(
                () =>
                {
                    ReachyProviderSelection invalid = new ReachyProviderSelection(
                        ReachyProviderKind.Asr,
                        "invalid",
                        "Invalid",
                        ReachyProviderExecution.AndroidService,
                        ReachyConnectivityRequirement.OfflineCapable,
                        false,
                        "Invalid connectivity claim.");
                    GC.KeepAlive(invalid);
                },
                "network-backed provider cannot claim offline");
            Equal(
                ReachyProviderExecution.Unconfigured,
                initial.GetProvider(ReachyProviderKind.Asr).Execution,
                "old snapshot remains immutable");
        }

        private static void PrivacySummaryTracksCloudBoundSelections()
        {
            var store = new ReachySettingsStateStore();
            Contains(
                store.Current.PrivacyCloudSummary,
                "No selected provider",
                "initial privacy summary");

            store.CycleProvider(ReachyProviderKind.Llm);
            store.CycleProvider(ReachyProviderKind.Llm);
            ReachyProviderSelection llm =
                store.Current.GetProvider(ReachyProviderKind.Llm);
            Equal(
                ReachyProviderExecution.Cloud,
                llm.Execution,
                "cloud LLM preference");
            Equal(
                ReachyConnectivityRequirement.NetworkRequired,
                llm.Connectivity,
                "cloud LLM network requirement");
            Contains(store.Current.PrivacyCloudSummary, "LLM", "LLM identity");
            Contains(
                store.Current.PrivacyCloudSummary,
                "Network required",
                "cloud privacy requirement");
        }

        private static void DurableSettingsRoundTripAndRejectInvalidValues()
        {
            var source = new ReachySettingsStateStore();
            source.CycleProvider(ReachyProviderKind.Asr);
            source.CycleProvider(ReachyProviderKind.Asr);
            source.CycleProvider(ReachyProviderKind.Llm);
            source.CycleProvider(ReachyProviderKind.Llm);
            source.CyclePreferredCameraFacing();
            source.CycleSpeechLanguage();
            source.CycleSpeechVoice();
            source.CycleLocalModelMemoryBudget();
            source.CycleLocalModelContextLength();
            source.CycleSimulationFidelity();
            source.ToggleHistory();
            source.CycleRetentionDays();

            ReachyDurableSettings durable = source.CaptureDurableSettings();
            var restored = new ReachySettingsStateStore();
            restored.ApplyDurableSettings(durable);
            Equal(
                source.Current.GetProvider(ReachyProviderKind.Asr).Execution,
                restored.Current.GetProvider(ReachyProviderKind.Asr).Execution,
                "ASR round trip");
            Equal(
                source.Current.GetProvider(ReachyProviderKind.Llm).Execution,
                restored.Current.GetProvider(ReachyProviderKind.Llm).Execution,
                "LLM round trip");
            Equal(
                source.Current.PreferredCameraFacing,
                restored.Current.PreferredCameraFacing,
                "camera round trip");
            Equal(
                source.Current.SpeechLanguage,
                restored.Current.SpeechLanguage,
                "language round trip");
            Equal(
                source.Current.LocalModelMemoryBudgetMb,
                restored.Current.LocalModelMemoryBudgetMb,
                "memory round trip");
            Equal(
                source.Current.HistoryEnabled,
                restored.Current.HistoryEnabled,
                "history round trip");

            ReachyDurableSettings invalid = restored.CaptureDurableSettings();
            invalid.AsrExecution = 999;
            RejectsWithoutMutation(restored, invalid, "invalid ASR");

            invalid = restored.CaptureDurableSettings();
            invalid.LlmExecution = (int)ReachyProviderExecution.AndroidService;
            RejectsWithoutMutation(restored, invalid, "unsupported Android LLM");

            invalid = restored.CaptureDurableSettings();
            invalid.PreferredCameraFacing = 999;
            RejectsWithoutMutation(restored, invalid, "invalid camera facing");

            invalid = restored.CaptureDurableSettings();
            invalid.SpeechLanguage = "not-supported";
            RejectsWithoutMutation(restored, invalid, "invalid speech language");

            invalid = restored.CaptureDurableSettings();
            invalid.SpeechVoice = "not-supported";
            RejectsWithoutMutation(restored, invalid, "invalid speech voice");

            invalid = restored.CaptureDurableSettings();
            invalid.LocalModelMemoryBudgetMb = -1;
            RejectsWithoutMutation(restored, invalid, "invalid memory budget");

            invalid = restored.CaptureDurableSettings();
            invalid.LocalModelContextTokens = -1;
            RejectsWithoutMutation(restored, invalid, "invalid context length");

            invalid = restored.CaptureDurableSettings();
            invalid.SimulationFidelity = 999;
            RejectsWithoutMutation(restored, invalid, "invalid simulation fidelity");

            invalid = restored.CaptureDurableSettings();
            invalid.RetentionDays = -1;
            RejectsWithoutMutation(restored, invalid, "invalid retention period");
        }

        private static void RejectsWithoutMutation(
            ReachySettingsStateStore store,
            ReachyDurableSettings invalid,
            string label)
        {
            ReachySettingsSnapshot before = store.Current;
            Throws<ArgumentException>(
                () => store.ApplyDurableSettings(invalid),
                label + " rejected");
            ReachySettingsSnapshot after = store.Current;

            Equal(before.Revision, after.Revision, label + " preserves revision");
            foreach (ReachyProviderKind kind in Enum.GetValues<ReachyProviderKind>())
            {
                Equal(
                    before.GetProvider(kind).Execution,
                    after.GetProvider(kind).Execution,
                    label + $" preserves {kind} provider");
            }
            Equal(
                before.PreferredCameraFacing,
                after.PreferredCameraFacing,
                label + " preserves camera facing");
            Equal(
                before.SpeechLanguage,
                after.SpeechLanguage,
                label + " preserves speech language");
            Equal(
                before.SpeechVoice,
                after.SpeechVoice,
                label + " preserves speech voice");
            Equal(
                before.LocalModelMemoryBudgetMb,
                after.LocalModelMemoryBudgetMb,
                label + " preserves memory budget");
            Equal(
                before.LocalModelContextTokens,
                after.LocalModelContextTokens,
                label + " preserves context length");
            Equal(
                before.SimulationFidelity,
                after.SimulationFidelity,
                label + " preserves simulation fidelity");
            Equal(
                before.HistoryEnabled,
                after.HistoryEnabled,
                label + " preserves history setting");
            Equal(
                before.RetentionDays,
                after.RetentionDays,
                label + " preserves retention period");
        }

        private static void SectionsActionsAndAttributionsAreComplete()
        {
            var store = new ReachySettingsStateStore();
            int changes = 0;
            store.Changed += (_, eventArgs) =>
            {
                ++changes;
                True(eventArgs.Snapshot.Revision > 0UL, "settings revision advances");
            };

            foreach (ReachySettingsSection section in
                     Enum.GetValues<ReachySettingsSection>())
            {
                store.SelectSection(section);
                True(
                    !string.IsNullOrWhiteSpace(
                        ReachySettingsStateStore.GetSectionLabel(section)),
                    $"section label {section}");
            }
            store.ReportUnavailableAction(
                "Camera preview",
                "CameraX preview begins in RMA-091");
            Contains(
                store.Current.StatusMessage,
                "Camera preview unavailable",
                "unavailable action visibility");
            True(changes >= 8, "section and action change events");

            ReachyLicenseNotice[] notices =
                ReachySettingsStateStore.GetLicenseNotices();
            True(notices.Length >= 4, "license notice count");
            Contains(notices[1].Attribution, "Pollen", "Reachy attribution");
            Contains(notices[2].Attribution, "DeepMind", "MuJoCo attribution");
            Contains(notices[3].Attribution, "Unity", "Unity attribution");
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
