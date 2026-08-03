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
            DurableSettingsRoundTripAndSanitizeInvalidValues();
            SectionsActionsAndAttributionsAreComplete();
            Console.WriteLine("RMA-082 settings state tests passed.");
        }

        private static void ProviderSelectionsAreIndependentAndNetworkTruthful()
        {
            var store = new ReachySettingsStateStore();
            ReachySettingsSnapshot initial = store.Current;
            Equal(4, initial.ProviderSelections.Count, "provider slot count");
            Equal(
                ReachyProviderExecution.Unconfigured,
                initial.GetProvider(ReachyProviderKind.Asr).Execution,
                "initial ASR");

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
            Contains(store.Current.PrivacyCloudSummary, "LLM", "LLM privacy identity");
            Contains(
                store.Current.PrivacyCloudSummary,
                "Network required",
                "cloud privacy requirement");
        }

        private static void DurableSettingsRoundTripAndSanitizeInvalidValues()
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

            var invalid = new ReachyDurableSettings
            {
                AsrExecution = 999,
                LlmExecution = (int)ReachyProviderExecution.AndroidService,
                PreferredCameraFacing = 999,
                SpeechLanguage = "not-supported",
                SpeechVoice = "not-supported",
                LocalModelMemoryBudgetMb = -1,
                LocalModelContextTokens = -1,
                SimulationFidelity = 999,
                RetentionDays = -1,
            };
            restored.ApplyDurableSettings(invalid);
            Equal(
                ReachyProviderExecution.Unconfigured,
                restored.Current.GetProvider(ReachyProviderKind.Asr).Execution,
                "invalid ASR sanitized");
            Equal(
                ReachyProviderExecution.Unconfigured,
                restored.Current.GetProvider(ReachyProviderKind.Llm).Execution,
                "unsupported Android LLM sanitized");
            Equal(
                ReachyCameraFacing.Unconfigured,
                restored.Current.PreferredCameraFacing,
                "invalid camera sanitized");
            Equal("System default", restored.Current.SpeechLanguage, "language fallback");
            Equal(1024, restored.Current.LocalModelMemoryBudgetMb, "memory fallback");
            Equal(30, restored.Current.RetentionDays, "retention fallback");
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
                     Enum.GetValues(typeof(ReachySettingsSection)))
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
