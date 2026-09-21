#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.Speech;

namespace ReachyMini.Core.Tests
{
    internal static class Rma195TtsVoiceSelectionTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ExactOfflineInstalledVoiceIsRequired();
            NetworkOrMissingDataNeverWins();
            SelectionIsStableByVoiceId();
            WrongLocaleFailsClosed();
        }

        private static void ExactOfflineInstalledVoiceIsRequired()
        {
            TtsVoice expected = Voice("voice-b", "en-US", SpeechNetworkRequirement.None, true);
            TtsVoice? selected = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                new[]
                {
                    Voice("voice-online", "en-US", SpeechNetworkRequirement.ProviderControlled, true),
                    expected,
                },
                "en-US");
            Same(expected, selected, "exact offline installed voice");
        }

        private static void NetworkOrMissingDataNeverWins()
        {
            TtsVoice? selected = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                new[]
                {
                    Voice("network", "en-US", SpeechNetworkRequirement.Required, true),
                    Voice("missing", "en-US", SpeechNetworkRequirement.None, false),
                },
                "en-US");
            Same(null, selected, "network/missing data rejection");
        }

        private static void SelectionIsStableByVoiceId()
        {
            TtsVoice expected = Voice("a-voice", "en-US", SpeechNetworkRequirement.None, true);
            TtsVoice? selected = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                new[]
                {
                    Voice("z-voice", "en-US", SpeechNetworkRequirement.None, true),
                    expected,
                    Voice("m-voice", "en-US", SpeechNetworkRequirement.None, true),
                },
                "en-US");
            Same(expected, selected, "stable voice-id ordering");
        }

        private static void WrongLocaleFailsClosed()
        {
            TtsVoice? selected = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                new[] { Voice("voice", "en-GB", SpeechNetworkRequirement.None, true) },
                "en-US");
            Same(null, selected, "wrong locale rejection");
        }

        private static TtsVoice Voice(
            string id,
            string language,
            SpeechNetworkRequirement network,
            bool installed) =>
            new TtsVoice(id, id, language, network, installed);

        private static void Same<T>(T expected, T actual, string description)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}; actual {actual}.");
            }
        }
    }
}
