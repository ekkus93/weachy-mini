#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.LocalModels
{
    internal enum LocalLlmRuntimePollKind
    {
        None = 0,
        Text = 1,
        Completed = 2,
        Cancelled = 3,
        Error = 4,
    }

    internal class LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeCallResult(int status, string detail)
        {
            Status = status;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
        }

        internal int Status { get; }
        internal string Detail { get; }
        internal bool Succeeded => Status == 0;
    }

    internal sealed class LocalLlmRuntimeLoadResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeLoadResult(int status, string detail, ulong modelHandle)
            : base(status, detail)
        {
            ModelHandle = modelHandle;
        }

        internal ulong ModelHandle { get; }
    }

    internal sealed class LocalLlmRuntimeTemplateResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeTemplateResult(int status, string detail, string prompt)
            : base(status, detail)
        {
            Prompt = prompt;
        }

        internal string Prompt { get; }
    }

    internal sealed class LocalLlmRuntimeTokenCountResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeTokenCountResult(int status, string detail, int tokenCount)
            : base(status, detail)
        {
            TokenCount = tokenCount;
        }

        internal int TokenCount { get; }
    }

    internal sealed class LocalLlmRuntimeStartResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeStartResult(int status, string detail, ulong generationHandle)
            : base(status, detail)
        {
            GenerationHandle = generationHandle;
        }

        internal ulong GenerationHandle { get; }
    }

    internal sealed class LocalLlmRuntimePollResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimePollResult(
            int status,
            string detail,
            LocalLlmRuntimePollKind kind,
            int eventStatus,
            ulong sequence,
            string text)
            : base(status, detail)
        {
            Kind = kind;
            EventStatus = eventStatus;
            Sequence = sequence;
            Text = text;
        }

        internal LocalLlmRuntimePollKind Kind { get; }
        internal int EventStatus { get; }
        internal ulong Sequence { get; }
        internal string Text { get; }
    }

    internal sealed class LocalLlmRuntimeMetricsResult : LocalLlmRuntimeCallResult
    {
        internal LocalLlmRuntimeMetricsResult(
            int status,
            string detail,
            LocalLlmGenerationMetrics? metrics)
            : base(status, detail)
        {
            Metrics = metrics;
        }

        internal LocalLlmGenerationMetrics? Metrics { get; }
    }

    internal sealed class LocalLlmRuntimeChatMessage
    {
        internal LocalLlmRuntimeChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        internal string Role { get; }
        internal string Content { get; }
    }

    internal interface ILocalLlmRuntime : IDisposable
    {
        uint GetAbiVersion();
        LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors);
        LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle);
        LocalLlmRuntimeTemplateResult ApplyChatTemplate(
            ulong modelHandle,
            string? chatTemplate,
            IReadOnlyList<LocalLlmRuntimeChatMessage> messages);
        LocalLlmRuntimeTokenCountResult CountTokens(ulong modelHandle, string prompt);
        LocalLlmRuntimeStartResult StartConstrained(
            ulong modelHandle,
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot);
        LocalLlmRuntimePollResult Poll(ulong generationHandle);
        LocalLlmRuntimeCallResult Cancel(ulong generationHandle);
        LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle);
        LocalLlmRuntimeCallResult Release(ulong generationHandle);
    }
}
