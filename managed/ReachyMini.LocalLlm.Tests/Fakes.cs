#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal sealed class CollectingSink : ILocalLlmStreamSink
{
    private readonly List<LocalLlmStreamEvent> events = new List<LocalLlmStreamEvent>();
    internal List<LocalLlmStreamEvent> Events => events;
    internal string Text
    {
        get
        {
            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < events.Count; ++index)
            {
                if (events[index].Type == LocalLlmStreamEventType.Text)
                {
                    builder.Append(events[index].Text);
                }
            }
            return builder.ToString();
        }
    }

    public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        events.Add(streamEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ThrowingSink : ILocalLlmStreamSink
{
    private readonly LocalLlmStreamEventType throwOn;
    internal ThrowingSink(LocalLlmStreamEventType throwOn) => this.throwOn = throwOn;

    public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (streamEvent.Type == throwOn)
        {
            throw new InvalidOperationException("synthetic sink failure");
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRuntime : ILocalLlmRuntime
{
    private readonly Queue<LocalLlmRuntimeLoadResult> loadResults = new Queue<LocalLlmRuntimeLoadResult>();
    private readonly Queue<LocalLlmRuntimePollResult> polls = new Queue<LocalLlmRuntimePollResult>();
    private bool staleAfterCancelEmitted;
    private bool currentGenerationCancelled;
    private ulong nextHandle = 1000UL;

    internal uint AbiVersion { get; set; } = 2U;
    internal int TokenCount { get; set; } = 100;
    internal bool BlockLoad { get; set; }
    internal bool BlockStart { get; set; }
    internal bool BlockUntilCancel { get; set; }
    internal bool EmitStaleTextAfterCancel { get; set; }
    internal bool TerminalAfterFailedCancel { get; set; }
    internal bool ThrowOnNextPoll { get; set; }
    internal bool ThrowOutOfMemoryOnStart { get; set; }
    internal bool ThrowOutOfMemoryOnNextPoll { get; set; }
    internal bool ThrowOnMetrics { get; set; }
    internal int CancelStatus { get; set; }
    internal int LoadCount { get; private set; }
    internal int UnloadCount { get; private set; }
    internal int StartCount { get; private set; }
    internal int CancelCount { get; private set; }
    internal int ReleaseCount { get; private set; }
    internal string? LastChatTemplate { get; private set; }
    internal List<LocalLlmRuntimeChatMessage> LastMessages { get; } = new List<LocalLlmRuntimeChatMessage>();
    internal int MandatoryPromptTokenCount { get; set; } = 8;
    private bool mandatoryMeasured;
    internal string LastGrammar { get; private set; } = string.Empty;
    internal string LastGrammarRoot { get; private set; } = string.Empty;
    internal ManualResetEventSlim LoadEntered { get; } = new ManualResetEventSlim(false);
    internal ManualResetEventSlim LoadRelease { get; } = new ManualResetEventSlim(false);
    internal ManualResetEventSlim StartEntered { get; } = new ManualResetEventSlim(false);
    internal ManualResetEventSlim StartRelease { get; } = new ManualResetEventSlim(false);
    internal TaskCompletionSource<bool> GenerationStarted { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public uint GetAbiVersion() => AbiVersion;

    public LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors)
    {
        Program.Require(Path.IsPathRooted(fullPath), "Provider passed a non-rooted approved model path.");
        Program.Require(checkTensors, "Provider disabled tensor checking.");
        ++LoadCount;
        LoadEntered.Set();
        if (BlockLoad && !LoadRelease.Wait(TimeSpan.FromSeconds(5)))
        {
            return new LocalLlmRuntimeLoadResult(14, "synthetic load gate timed out", 0UL);
        }
        if (loadResults.Count > 0)
        {
            return loadResults.Dequeue();
        }
        return new LocalLlmRuntimeLoadResult(0, string.Empty, ++nextHandle);
    }

    public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
    {
        Program.Require(modelHandle != 0UL, "Provider attempted to unload a null model handle.");
        ++UnloadCount;
        return new LocalLlmRuntimeCallResult(0, string.Empty);
    }

    public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
        ulong modelHandle,
        string? chatTemplate,
        IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
    {
        Program.Require(modelHandle != 0UL, "Template called with null model handle.");
        LastChatTemplate = chatTemplate;
        LastMessages.Clear();
        for (int index = 0; index < messages.Count; ++index)
        {
            LastMessages.Add(new LocalLlmRuntimeChatMessage(messages[index].Role, messages[index].Content));
        }
        return new LocalLlmRuntimeTemplateResult(0, string.Empty, "templated-prompt");
    }

    public LocalLlmRuntimeTokenCountResult CountTokens(ulong modelHandle, string prompt)
    {
        Program.Require(modelHandle != 0UL, "Tokenize called with null model handle.");
        Program.Require(prompt == "templated-prompt", "Provider tokenized something other than exact templated output.");
        // Creation measures the mandatory prompt once, before any request, so the governor
        // knows the floor a throttled context must clear. Only later calls are request
        // preflights and answer with TokenCount.
        if (!mandatoryMeasured)
        {
            mandatoryMeasured = true;
            return new LocalLlmRuntimeTokenCountResult(0, string.Empty, MandatoryPromptTokenCount);
        }
        return new LocalLlmRuntimeTokenCountResult(0, string.Empty, TokenCount);
    }

    public LocalLlmRuntimeStartResult StartConstrained(
        ulong modelHandle,
        string prompt,
        LocalLlmExecutionProfile profile,
        string grammar,
        string grammarRoot)
    {
        Program.Require(modelHandle != 0UL, "Generation started with null model handle.");
        Program.Require(prompt == "templated-prompt", "Generation did not consume exact templated prompt.");
        Program.Require(profile.ContextTokens == 2048, "Unexpected generation profile reached fake runtime.");
        ++StartCount;
        currentGenerationCancelled = false;
        staleAfterCancelEmitted = false;
        if (ThrowOutOfMemoryOnStart)
        {
            ThrowOutOfMemoryOnStart = false;
            throw (Exception)Activator.CreateInstance(typeof(OutOfMemoryException), "synthetic start OOM")!;
        }
        LastGrammar = grammar;
        LastGrammarRoot = grammarRoot;
        StartEntered.Set();
        GenerationStarted.TrySetResult(true);
        if (BlockStart && !StartRelease.Wait(TimeSpan.FromSeconds(5)))
        {
            return new LocalLlmRuntimeStartResult(14, "synthetic start gate timed out", 0UL);
        }
        return new LocalLlmRuntimeStartResult(0, string.Empty, 5000UL + (ulong)StartCount);
    }

    public LocalLlmRuntimePollResult Poll(ulong generationHandle)
    {
        Program.Require(generationHandle != 0UL, "Poll called with null generation handle.");
        if (ThrowOutOfMemoryOnNextPoll)
        {
            ThrowOutOfMemoryOnNextPoll = false;
            throw (Exception)Activator.CreateInstance(typeof(OutOfMemoryException), "synthetic poll OOM")!;
        }
        if (ThrowOnNextPoll)
        {
            ThrowOnNextPoll = false;
            throw new InvalidOperationException("synthetic poll threw");
        }
        if (currentGenerationCancelled && CancelStatus == 0)
        {
            if (EmitStaleTextAfterCancel && !staleAfterCancelEmitted)
            {
                staleAfterCancelEmitted = true;
                return new LocalLlmRuntimePollResult(
                    0, string.Empty, LocalLlmRuntimePollKind.Text, 0, 999UL, "STALE");
            }
            return new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.Cancelled, 13, 1000UL, string.Empty);
        }
        if (currentGenerationCancelled && CancelStatus != 0 && TerminalAfterFailedCancel)
        {
            return new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.Completed, 0, 1000UL, string.Empty);
        }
        if (BlockUntilCancel)
        {
            return new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.None, 0, 0UL, string.Empty);
        }
        if (polls.Count > 0)
        {
            return polls.Dequeue();
        }
        return new LocalLlmRuntimePollResult(
            0, string.Empty, LocalLlmRuntimePollKind.None, 0, 0UL, string.Empty);
    }

    public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
    {
        Program.Require(generationHandle != 0UL, "Cancel called with null generation handle.");
        ++CancelCount;
        currentGenerationCancelled = true;
        return new LocalLlmRuntimeCallResult(
            CancelStatus,
            CancelStatus == 0 ? string.Empty : "synthetic cancel failure");
    }

    public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
    {
        Program.Require(generationHandle != 0UL, "Metrics called with null generation handle.");
        if (ThrowOnMetrics)
        {
            throw new InvalidOperationException("synthetic metrics failure");
        }
        return new LocalLlmRuntimeMetricsResult(
            0,
            string.Empty,
            new LocalLlmGenerationMetrics(100UL, 20UL, 1000UL, 2000UL, 3000UL, 2048, 256, 4, 4));
    }

    public LocalLlmRuntimeCallResult Release(ulong generationHandle)
    {
        Program.Require(generationHandle != 0UL, "Release called with null generation handle.");
        ++ReleaseCount;
        return new LocalLlmRuntimeCallResult(0, string.Empty);
    }

    internal void EnqueueLoad(int status, ulong handle, string detail) =>
        loadResults.Enqueue(new LocalLlmRuntimeLoadResult(status, detail, handle));

    internal void EnqueueText(ulong sequence, string text) =>
        polls.Enqueue(new LocalLlmRuntimePollResult(
            0, string.Empty, LocalLlmRuntimePollKind.Text, 0, sequence, text));

    internal void EnqueueCompleted(ulong sequence) =>
        polls.Enqueue(new LocalLlmRuntimePollResult(
            0, string.Empty, LocalLlmRuntimePollKind.Completed, 0, sequence, string.Empty));

    internal void EnqueueError(ulong sequence, int eventStatus, string detail) =>
        polls.Enqueue(new LocalLlmRuntimePollResult(
            0, detail, LocalLlmRuntimePollKind.Error, eventStatus, sequence, string.Empty));

    public void Dispose()
    {
        LoadEntered.Dispose();
        LoadRelease.Dispose();
        StartEntered.Dispose();
        StartRelease.Dispose();
        GC.SuppressFinalize(this);
    }
}
