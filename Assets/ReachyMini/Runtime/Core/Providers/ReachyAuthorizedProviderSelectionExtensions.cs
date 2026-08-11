#nullable enable

using System;
using ReachyMini.Perception;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public static class ReachyAuthorizedProviderSelectionExtensions
    {
        public static SpeechProviderSelectionSnapshot SelectFallback(
            this SpeechProviderSelection selection,
            SpeechProviderDescriptor sourceProvider,
            SpeechProviderDescriptor targetProvider,
            ReachyAuthorizedProviderSwitch authorization)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (sourceProvider == null)
            {
                throw new ArgumentNullException(nameof(sourceProvider));
            }
            if (targetProvider == null)
            {
                throw new ArgumentNullException(nameof(targetProvider));
            }
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }
            if (sourceProvider.Kind != targetProvider.Kind)
            {
                throw new ArgumentException(
                    "A speech provider fallback cannot change provider kind.",
                    nameof(targetProvider));
            }

            SpeechProviderSelectionSnapshot current = selection.Current;
            if (current.Kind != sourceProvider.Kind ||
                !string.Equals(
                    current.ProviderInstanceId,
                    sourceProvider.InstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Speech provider selection changed before fallback authorization could be consumed.");
            }

            ReachyProviderWorkloadKind workload = sourceProvider.Kind switch
            {
                SpeechProviderKind.AutomaticSpeechRecognition =>
                    ReachyProviderWorkloadKind.Asr,
                SpeechProviderKind.TextToSpeech =>
                    ReachyProviderWorkloadKind.Tts,
                _ => throw new InvalidOperationException(
                    "Unsupported speech provider kind for fallback authorization."),
            };
            authorization.Consume(
                workload,
                sourceProvider.ProviderId,
                targetProvider.ProviderId);
            return selection.Select(targetProvider);
        }

        public static VisionProviderSelectionSnapshot SelectFallback(
            this VisionProviderSelection selection,
            ProviderDescriptor sourceProvider,
            ProviderDescriptor targetProvider,
            ReachyAuthorizedProviderSwitch authorization)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (sourceProvider == null)
            {
                throw new ArgumentNullException(nameof(sourceProvider));
            }
            if (targetProvider == null)
            {
                throw new ArgumentNullException(nameof(targetProvider));
            }
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }
            if (sourceProvider.Kind != VisionProviderKind.SemanticVisionLanguage ||
                targetProvider.Kind != VisionProviderKind.SemanticVisionLanguage)
            {
                throw new ArgumentException(
                    "RMA-146 fallback authorization applies only to semantic VLM provider switching.",
                    nameof(targetProvider));
            }

            VisionProviderSelectionSnapshot current = selection.Current;
            if (current.Kind != sourceProvider.Kind ||
                !string.Equals(
                    current.ProviderInstanceId,
                    sourceProvider.InstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Vision provider selection changed before fallback authorization could be consumed.");
            }

            authorization.Consume(
                ReachyProviderWorkloadKind.Vlm,
                sourceProvider.ProviderId,
                targetProvider.ProviderId);
            return selection.Select(targetProvider);
        }
    }
}
