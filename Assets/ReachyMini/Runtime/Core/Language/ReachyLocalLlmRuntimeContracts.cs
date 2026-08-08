#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.LocalModels;

namespace ReachyMini.Language
{
    public sealed class LocalLlmExecutionProfile
    {
        public LocalLlmExecutionProfile(
            uint contextTokens,
            uint batchTokens,
            uint microBatchTokens,
            uint maximumGeneratedTokens,
            int threads,
            int batchThreads,
            float temperature,
            float minimumProbability,
            uint seed,
            uint streamQueueCapacity)
        {
            if (contextTokens < 128U ||
                batchTokens == 0U || batchTokens > contextTokens ||
                microBatchTokens == 0U || microBatchTokens > batchTokens ||
                maximumGeneratedTokens == 0U || maximumGeneratedTokens >= contextTokens ||
                threads <= 0 || batchThreads <= 0 ||
                float.IsNaN(temperature) || float.IsInfinity(temperature) || temperature < 0.0F ||
                float.IsNaN(minimumProbability) || float.IsInfinity(minimumProbability) ||
                minimumProbability < 0.0F || minimumProbability > 1.0F ||
                streamQueueCapacity == 0U)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contextTokens),
                    "Local LLM execution profile contains an invalid limit.");
            }

            ContextTokens = contextTokens;
            BatchTokens = batchTokens;
            MicroBatchTokens = microBatchTokens;
            MaximumGeneratedTokens = maximumGeneratedTokens;
            Threads = threads;
            BatchThreads = batchThreads;
            Temperature = temperature;
            MinimumProbability = minimumProbability;
            Seed = seed;
            StreamQueueCapacity = streamQueueCapacity;
        }

        public uint ContextTokens { get; }

        public uint BatchTokens { get; }

        public uint MicroBatchTokens { get; }

        public uint MaximumGeneratedTokens { get; }

        public int Threads { get; }

        public int BatchThreads { get; }

        public float Temperature { get; }

        public float MinimumProbability { get; }

        public uint Seed { get; }

        public uint StreamQueueCapacity { get; }

        public static LocalLlmExecutionProfile CreateRma133SelectedProfile()
        {
            return new LocalLlmExecutionProfile(
                contextTokens: 2048U,
                batchTokens: 256U,
                microBatchTokens: 64U,
                maximumGeneratedTokens: 128U,
                threads: 4,
                batchThreads: 4,
                temperature: 0.0F,
                minimumProbability: 0.0F,
                seed: 133U,
                streamQueueCapacity: 64U);
        }

        public static LocalLlmExecutionProfile CreateInitialProductCoexistenceProfile()
        {
            return new LocalLlmExecutionProfile(
                contextTokens: 2048U,
                batchTokens: 256U,
                microBatchTokens: 64U,
                maximumGeneratedTokens: 128U,
                threads: 2,
                batchThreads: 2,
                temperature: 0.0F,
                minimumProbability: 0.0F,
                seed: 133U,
                streamQueueCapacity: 64U);
        }
    }

    public sealed class LocalLlmProviderConfiguration
    {
        public const int MaximumSystemPromptCharacters = 65536;
        public const int MaximumGrammarCharacters = 65536;
        public const int MaximumGrammarRootCharacters = 64;
        public const int MaximumPromptSuffixCharacters = 128;
        public const int MaximumHistoryTurns = 32;

        public LocalLlmProviderConfiguration(
            string systemPrompt,
            string grammar,
            string grammarRoot,
            string userPromptSuffix,
            LocalLlmExecutionProfile executionProfile,
            int maximumCommittedHistoryTurns = 8,
            int managedEventQueueCapacity = 64)
        {
            SystemPrompt = RequireBounded(
                systemPrompt,
                nameof(systemPrompt),
                MaximumSystemPromptCharacters,
                allowEmpty: false);
            Grammar = RequireBounded(
                grammar,
                nameof(grammar),
                MaximumGrammarCharacters,
                allowEmpty: false);
            GrammarRoot = RequireBounded(
                grammarRoot,
                nameof(grammarRoot),
                MaximumGrammarRootCharacters,
                allowEmpty: false);
            UserPromptSuffix = RequireBounded(
                userPromptSuffix ?? string.Empty,
                nameof(userPromptSuffix),
                MaximumPromptSuffixCharacters,
                allowEmpty: true);
            ExecutionProfile = executionProfile ??
                throw new ArgumentNullException(nameof(executionProfile));
            if (maximumCommittedHistoryTurns < 0 ||
                maximumCommittedHistoryTurns > MaximumHistoryTurns)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCommittedHistoryTurns));
            }
            if (managedEventQueueCapacity < 2 || managedEventQueueCapacity > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(managedEventQueueCapacity));
            }
            MaximumCommittedHistoryTurns = maximumCommittedHistoryTurns;
            ManagedEventQueueCapacity = managedEventQueueCapacity;
        }

        public string SystemPrompt { get; }

        public string Grammar { get; }

        public string GrammarRoot { get; }

        public string UserPromptSuffix { get; }

        public LocalLlmExecutionProfile ExecutionProfile { get; }

        public int MaximumCommittedHistoryTurns { get; }

        public int ManagedEventQueueCapacity { get; }

        private static string RequireBounded(
            string value,
            string name,
            int maximumCharacters,
            bool allowEmpty)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
                value.Length > maximumCharacters)
            {
                throw new ArgumentException(
                    $"{name} is outside its configured character bound.",
                    name);
            }
            return value;
        }
    }

    public sealed class LocalLlmChatMessage
    {
        public LocalLlmChatMessage(string role, string content)
        {
            if (string.IsNullOrWhiteSpace(role) || role.Length > 32)
            {
                throw new ArgumentException("Chat role is invalid.", nameof(role));
            }
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Role = role;
        }

        public string Role { get; }

        public string Content { get; }
    }

    public enum LocalLlmRuntimeEventType
    {
        None = 0,
        Text = 1,
        Completed = 2,
        Cancelled = 3,
        Error = 4,
    }

    public sealed class LocalLlmRuntimeEvent
    {
        public LocalLlmRuntimeEvent(
            LocalLlmRuntimeEventType type,
            int status,
            ulong sequence,
            string text)
        {
            Type = type;
            Status = status;
            Sequence = sequence;
            Text = text ?? string.Empty;
        }

        public LocalLlmRuntimeEventType Type { get; }

        public int Status { get; }

        public ulong Sequence { get; }

        public string Text { get; }
    }

    public sealed class LocalLlmGenerationMetrics
    {
        public LocalLlmGenerationMetrics(
            ulong promptTokens,
            ulong generatedTokens,
            ulong timeToFirstTokenMicroseconds,
            ulong decodeMicroseconds)
        {
            PromptTokens = promptTokens;
            GeneratedTokens = generatedTokens;
            TimeToFirstTokenMicroseconds = timeToFirstTokenMicroseconds;
            DecodeMicroseconds = decodeMicroseconds;
        }

        public ulong PromptTokens { get; }

        public ulong GeneratedTokens { get; }

        public ulong TimeToFirstTokenMicroseconds { get; }

        public ulong DecodeMicroseconds { get; }
    }

    public interface ILocalLlmGeneration : IDisposable
    {
        LocalLlmRuntimeEvent Poll();

        void Cancel();

        LocalLlmGenerationMetrics GetMetrics();
    }

    public interface ILocalLlmModelSession : IDisposable
    {
        string RenderChatTemplate(IReadOnlyList<LocalLlmChatMessage> messages);

        int CountTokens(string prompt);

        ILocalLlmGeneration StartConstrained(
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot);
    }

    public interface ILocalLlmRuntimeFactory
    {
        uint AbiVersion { get; }

        ILocalLlmModelSession LoadModel(
            LocalModelApprovedArtifact artifact,
            LocalModelManifest manifest);
    }
}
