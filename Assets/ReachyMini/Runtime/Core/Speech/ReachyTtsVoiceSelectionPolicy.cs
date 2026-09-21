#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Speech
{
    // RMA-195: deterministic, fail-closed selection for the shipping offline
    // conversation path. The provider itself rejects locale mismatch,
    // uninstalled data, and any network-capable voice, so the orchestrator
    // should never pick an arbitrary "first" entry and rely on the provider
    // to reject it later. When several exact-locale offline voices exist,
    // choose a stable identifier ordering so the same installed catalog gives
    // the same result across launches.
    public static class ReachyTtsVoiceSelectionPolicy
    {
        public static TtsVoice? SelectExactOfflineVoice(
            IReadOnlyList<TtsVoice> voices,
            string languageTag)
        {
            if (voices == null)
            {
                throw new ArgumentNullException(nameof(voices));
            }
            string requiredLanguageTag = SpeechProviderDescriptor.RequireText(
                languageTag,
                nameof(languageTag));

            TtsVoice? selected = null;
            for (int index = 0; index < voices.Count; ++index)
            {
                TtsVoice voice = voices[index] ??
                    throw new ArgumentException(
                        "TTS voice catalogs cannot contain null entries.",
                        nameof(voices));
                if (!voice.IsInstalled || voice.MayUseNetwork ||
                    !string.Equals(
                        voice.LanguageTag,
                        requiredLanguageTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (selected == null ||
                    string.CompareOrdinal(voice.VoiceId, selected.VoiceId) < 0)
                {
                    selected = voice;
                }
            }
            return selected;
        }
    }
}
