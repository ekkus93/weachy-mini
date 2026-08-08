#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Language
{
    public sealed class ReachyLocalLlmProvider : ILocalLlmProvider
    {
        public const uint RequiredRuntimeAbiVersion = 2U;
        public const int MaximumRawOutputCharacters = 16384;
        private const int PollSleepMilliseconds = 1;
        private static readonly TimeSpan CancellationDrainTimeout = TimeSpan.FromSeconds(30.0);

        private readonly object stateGate = new object();
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly ILocalLlmRuntimeFactory runtimeFactory;
        private readonly LocalModelApprovedArtifact artifact;
        private readonly LocalModelManifest manifest;
        private readonly LocalLlmProviderConfiguration configuration;
        private readonly List<CommittedTurn> history = new List<CommittedTurn>();

        private ILocalLlmModelSession? modelSession;
        private TurnOperation? activeOperation;
        private LocalLlmProviderState state = LocalLlmProviderState.Created;
        private LocalLlmFailure stateFailure = LocalLlmFailure.None;
        private string stateDetail = "Local LLM provider is created but not loaded.";
        private ulong stateRevision = 1UL;
        private ulong conversationEpoch = 1UL;
        private int disposed;

        public ReachyLocalLlmProvider(
            ILocalLlmRuntimeFactory runtimeFactory,
            LocalModelApprovedArtifact artifact,
            LocalModelManifest manifest,
            LocalLlmProviderConfiguration configuration)
        {
            this.runtimeFactory = runtimeFactory ??
                throw new ArgumentNullException(nameof(runtimeFactory));
            this.artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));

            ValidateManifestAndArtifact();
            Descriptor = new LocalLlmProviderDescriptor(
                "local-llama-cpp",
                manifest.Identity.ModelId,
                manifest.Identity.DisplayName);
            LocalLlmExecutionProfile profile = configuration.ExecutionProfile;
            Capabilities = new LocalLlmProviderCapabilities(
                profile.ContextTokens,
                profile.MaximumGeneratedTokens,
                checked((uint)profile.Threads),
                checked((uint)profile.BatchThreads),
                profile.StreamQueueCapacity);
        }

        public LocalLlmProviderDescriptor Descriptor { get; }

        public LocalLlmProviderCapabilities Capabilities { get; }

        public LocalLlmProviderAvailability Availability
        {
            get
            {
                lock (stateGate)
                {
                    return new LocalLlmProviderAvailability(
                        state,
                        stateFailure,
                        stateDetail,
                        stateRevision);
                }
            }
        }

        public async ValueTask<LocalLlmOperationResult> LoadAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadCoreAsync(reload: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async ValueTask<LocalLlmOperationResult> ReloadAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadCoreAsync(reload: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async IAsyncEnumerable<LocalLlmEvent> GenerateAsync(
            LocalLlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            TurnOperation? operation;
            LocalLlmEvent? immediateFailure;
            lock (stateGate)
            {
                if (Volatile.Read(ref disposed) != 0)
                {
                    operation = null;
                    immediateFailure = LocalLlmEvent.Failed(
                        1UL,
                        LocalLlmFailure.Disposed,
                        "The local LLM provider is disposed.");
                }
                else if (activeOperation != null)
                {
                    operation = null;
                    immediateFailure = LocalLlmEvent.Failed(
                        1UL,
                        LocalLlmFailure.Busy,
                        "A local LLM generation is already active; requests are not queued.");
                }
                else if (modelSession == null || state != LocalLlmProviderState.Ready)
                {
                    operation = null;
                    immediateFailure = LocalLlmEvent.Failed(
                        1UL,
                        state == LocalLlmProviderState.Faulted
                            ? LocalLlmFailure.RuntimeFailure
                            : LocalLlmFailure.Unavailable,
                        stateDetail);
                }
                else if (configuration.MaximumCommittedHistoryTurns > 0 &&
                    history.Count >= configuration.MaximumCommittedHistoryTurns)
                {
                    operation = null;
                    immediateFailure = LocalLlmEvent.Failed(
                        1UL,
                        LocalLlmFailure.ContextLimit,
                        "The bounded conversation history is full; reset is required before another turn.");
                }
                else
                {
                    var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    requestCancellation.CancelAfter(request.Timeout);
                    operation = new TurnOperation(
                        conversationEpoch,
                        request,
                        requestCancellation,
                        new BoundedEventQueue(configuration.ManagedEventQueueCapacity));
                    activeOperation = operation;
                    SetStateLocked(
                        LocalLlmProviderState.Busy,
                        LocalLlmFailure.None,
                        "Local LLM generation is active.");
                    immediateFailure = null;
                }
            }

            if (immediateFailure != null)
            {
                yield return immediateFailure;
                yield break;
            }

            TurnOperation active = operation!;
            active.Worker = Task.Run(
                () => RunTurn(active),
                CancellationToken.None);
            try
            {
                while (true)
                {
                    LocalLlmEvent? item = await active.Events.ReadAsync()
                        .ConfigureAwait(false);
                    if (item == null)
                    {
                        break;
                    }
                    yield return item;
                    if (item.IsTerminal)
                    {
                        break;
                    }
                }
            }
            finally
            {
                active.Cancellation.Cancel();
                Task worker = active.Worker ?? Task.CompletedTask;
                await worker.ConfigureAwait(false);
                active.Dispose();
            }
        }

        public async ValueTask<LocalLlmOperationResult> ResetConversationAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TurnOperation? operation;
                lock (stateGate)
                {
                    conversationEpoch = checked(conversationEpoch + 1UL);
                    operation = activeOperation;
                    operation?.Cancellation.Cancel();
                }

                if (operation?.Worker != null)
                {
                    await operation.Worker.ConfigureAwait(false);
                }

                lock (stateGate)
                {
                    history.Clear();
                    if (modelSession != null && state != LocalLlmProviderState.Faulted)
                    {
                        SetStateLocked(
                            LocalLlmProviderState.Ready,
                            LocalLlmFailure.None,
                            "Local LLM conversation was reset; the verified model remains loaded.");
                    }
                }
                return LocalLlmOperationResult.Success(
                    "Local LLM conversation history was cleared.");
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                TurnOperation? operation;
                ILocalLlmModelSession? session;
                lock (stateGate)
                {
                    conversationEpoch = checked(conversationEpoch + 1UL);
                    operation = activeOperation;
                    operation?.Cancellation.Cancel();
                    session = modelSession;
                    modelSession = null;
                }
                if (operation?.Worker != null)
                {
                    await operation.Worker.ConfigureAwait(false);
                }
                session?.Dispose();
                lock (stateGate)
                {
                    history.Clear();
                    activeOperation = null;
                    SetStateLocked(
                        LocalLlmProviderState.Disposed,
                        LocalLlmFailure.Disposed,
                        "The local LLM provider is disposed.");
                }
            }
            finally
            {
                lifecycleGate.Release();
                lifecycleGate.Dispose();
            }
        }

        private async Task<LocalLlmOperationResult> LoadCoreAsync(
            bool reload,
            CancellationToken cancellationToken)
        {
            lock (stateGate)
            {
                if (activeOperation != null)
                {
                    return LocalLlmOperationResult.Failed(
                        LocalLlmFailure.Busy,
                        "The model cannot be loaded or reloaded during active generation.");
                }
                if (!reload && modelSession != null && state == LocalLlmProviderState.Ready)
                {
                    return LocalLlmOperationResult.Success(
                        "The exact verified local model is already loaded.");
                }
                if (runtimeFactory.AbiVersion != RequiredRuntimeAbiVersion)
                {
                    SetStateLocked(
                        LocalLlmProviderState.Unavailable,
                        LocalLlmFailure.AbiMismatch,
                        $"reachy_llama ABI {RequiredRuntimeAbiVersion} is required.");
                    return LocalLlmOperationResult.Failed(
                        LocalLlmFailure.AbiMismatch,
                        stateDetail);
                }
                SetStateLocked(
                    LocalLlmProviderState.Loading,
                    LocalLlmFailure.None,
                    reload
                        ? "Explicit local-model reload is in progress."
                        : "Local-model load is in progress.");
            }

            ILocalLlmModelSession? previous;
            lock (stateGate)
            {
                previous = modelSession;
                modelSession = null;
            }
            previous?.Dispose();

            try
            {
                ILocalLlmModelSession loaded = await Task.Run(
                        () => runtimeFactory.LoadModel(artifact, manifest),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    loaded.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                lock (stateGate)
                {
                    modelSession = loaded;
                    SetStateLocked(
                        LocalLlmProviderState.Ready,
                        LocalLlmFailure.None,
                        "The exact verified local model is loaded and ready.");
                }
                return LocalLlmOperationResult.Success(
                    "The exact verified local model loaded successfully.");
            }
            catch (OperationCanceledException)
            {
                lock (stateGate)
                {
                    SetStateLocked(
                        LocalLlmProviderState.Unavailable,
                        LocalLlmFailure.Cancelled,
                        "Local-model loading was cancelled; no alternate model was loaded.");
                }
                return LocalLlmOperationResult.Failed(
                    LocalLlmFailure.Cancelled,
                    stateDetail);
            }
            catch (Exception exception)
            {
                lock (stateGate)
                {
                    SetStateLocked(
                        LocalLlmProviderState.Faulted,
                        LocalLlmFailure.RuntimeFailure,
                        "Local-model loading failed explicitly: " + exception.GetType().Name + ".");
                }
                return LocalLlmOperationResult.Failed(
                    LocalLlmFailure.RuntimeFailure,
                    stateDetail);
            }
        }

        private void RunTurn(TurnOperation operation)
        {
            ILocalLlmGeneration? generation = null;
            try
            {
                ILocalLlmModelSession session;
                List<CommittedTurn> historySnapshot;
                lock (stateGate)
                {
                    session = modelSession ??
                        throw new InvalidOperationException("The local model session disappeared before generation.");
                    historySnapshot = new List<CommittedTurn>(history);
                }

                List<LocalLlmChatMessage> messages = BuildMessages(
                    historySnapshot,
                    operation.Request.UserText);
                string prompt = session.RenderChatTemplate(messages);
                int promptTokens = session.CountTokens(prompt);
                if (promptTokens < 0 ||
                    (ulong)promptTokens + configuration.ExecutionProfile.MaximumGeneratedTokens >
                    configuration.ExecutionProfile.ContextTokens)
                {
                    operation.Events.ReplaceWithTerminal(
                        LocalLlmEvent.Failed(
                            1UL,
                            LocalLlmFailure.ContextLimit,
                            "Rendered conversation plus reserved output exceeds the bounded context; reset is required."));
                    return;
                }

                operation.Cancellation.Token.ThrowIfCancellationRequested();
                generation = session.StartConstrained(
                    prompt,
                    configuration.ExecutionProfile,
                    configuration.Grammar,
                    configuration.GrammarRoot);

                var output = new StringBuilder();
                ulong nextSequence = 1UL;
                bool cancellationSent = false;
                while (true)
                {
                    if (operation.Cancellation.IsCancellationRequested && !cancellationSent)
                    {
                        generation.Cancel();
                        cancellationSent = true;
                    }

                    LocalLlmRuntimeEvent nativeEvent = generation.Poll();
                    switch (nativeEvent.Type)
                    {
                        case LocalLlmRuntimeEventType.None:
                            Thread.Sleep(PollSleepMilliseconds);
                            continue;
                        case LocalLlmRuntimeEventType.Text:
                            if (cancellationSent)
                            {
                                continue;
                            }
                            if (nativeEvent.Text.Length >
                                MaximumRawOutputCharacters - output.Length)
                            {
                                generation.Cancel();
                                DrainCancelledGeneration(generation);
                                operation.Events.ReplaceWithTerminal(
                                    LocalLlmEvent.Failed(
                                        nextSequence,
                                        LocalLlmFailure.OutputLimit,
                                        "Local LLM output exceeded the independent managed output bound."));
                                return;
                            }
                            output.Append(nativeEvent.Text);
                            if (!operation.Events.TryWrite(
                                    LocalLlmEvent.Delta(nextSequence++, nativeEvent.Text)))
                            {
                                generation.Cancel();
                                DrainCancelledGeneration(generation);
                                operation.Events.ReplaceWithTerminal(
                                    LocalLlmEvent.Failed(
                                        nextSequence,
                                        LocalLlmFailure.RuntimeFailure,
                                        "The bounded managed event queue filled; generation was cancelled."));
                                return;
                            }
                            continue;
                        case LocalLlmRuntimeEventType.Completed:
                            if (cancellationSent || operation.Cancellation.IsCancellationRequested)
                            {
                                operation.Events.ReplaceWithTerminal(
                                    CancellationTerminal(operation, nextSequence));
                                return;
                            }
                            CompleteValidatedTurn(operation, output.ToString(), nextSequence);
                            return;
                        case LocalLlmRuntimeEventType.Cancelled:
                            operation.Events.ReplaceWithTerminal(
                                CancellationTerminal(operation, nextSequence));
                            return;
                        case LocalLlmRuntimeEventType.Error:
                            MarkRuntimeFault(
                                "Local LLM generation failed explicitly with runtime status " +
                                nativeEvent.Status + ".");
                            operation.Events.ReplaceWithTerminal(
                                LocalLlmEvent.Failed(
                                    nextSequence,
                                    LocalLlmFailure.RuntimeFailure,
                                    "Local LLM runtime reported a terminal generation failure."));
                            return;
                        default:
                            throw new InvalidOperationException(
                                "The local LLM runtime returned an unknown event type.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (generation != null)
                {
                    CancelAndDrainBestEffort(generation);
                }
                operation.Events.ReplaceWithTerminal(
                    CancellationTerminal(operation, 1UL));
            }
            catch (Exception exception)
            {
                if (generation != null)
                {
                    CancelAndDrainBestEffort(generation);
                }
                MarkRuntimeFault(
                    "Local LLM runtime operation failed explicitly: " +
                    exception.GetType().Name + ".");
                operation.Events.ReplaceWithTerminal(
                    LocalLlmEvent.Failed(
                        1UL,
                        LocalLlmFailure.RuntimeFailure,
                        "Local LLM runtime operation failed; explicit reload is required."));
            }
            finally
            {
                generation?.Dispose();
                lock (stateGate)
                {
                    if (ReferenceEquals(activeOperation, operation))
                    {
                        activeOperation = null;
                        if (state != LocalLlmProviderState.Faulted &&
                            Volatile.Read(ref disposed) == 0)
                        {
                            SetStateLocked(
                                modelSession == null
                                    ? LocalLlmProviderState.Unavailable
                                    : LocalLlmProviderState.Ready,
                                modelSession == null
                                    ? LocalLlmFailure.Unavailable
                                    : LocalLlmFailure.None,
                                modelSession == null
                                    ? "The verified local model is not loaded."
                                    : "The verified local model is ready.");
                        }
                    }
                }
                operation.Events.Complete();
            }
        }

        private List<LocalLlmChatMessage> BuildMessages(
            IReadOnlyList<CommittedTurn> committed,
            string userText)
        {
            var messages = new List<LocalLlmChatMessage>(2 + committed.Count * 2);
            messages.Add(new LocalLlmChatMessage("system", configuration.SystemPrompt));
            for (int index = 0; index < committed.Count; ++index)
            {
                CommittedTurn turn = committed[index];
                messages.Add(new LocalLlmChatMessage("user", turn.UserText));
                messages.Add(new LocalLlmChatMessage("assistant", turn.RawIntentJson));
            }
            string finalUser = userText;
            if (!string.IsNullOrEmpty(configuration.UserPromptSuffix))
            {
                finalUser += "\n" + configuration.UserPromptSuffix;
            }
            messages.Add(new LocalLlmChatMessage("user", finalUser));
            return messages;
        }

        private void CompleteValidatedTurn(
            TurnOperation operation,
            string rawJson,
            ulong sequence)
        {
            LocalLlmIntentParseResult parsed = LocalLlmBehaviorIntentParser.Parse(rawJson);
            if (!parsed.Succeeded || parsed.Intent == null)
            {
                operation.Events.ReplaceWithTerminal(
                    LocalLlmEvent.Failed(
                        sequence,
                        LocalLlmFailure.InvalidIntent,
                        "Constrained generation completed but independent behavior-intent validation failed: " +
                        parsed.Failure + "."));
                return;
            }

            lock (stateGate)
            {
                if (operation.Epoch != conversationEpoch ||
                    operation.Cancellation.IsCancellationRequested ||
                    !ReferenceEquals(activeOperation, operation))
                {
                    operation.Events.ReplaceWithTerminal(
                        CancellationTerminal(operation, sequence));
                    return;
                }
                if (configuration.MaximumCommittedHistoryTurns > 0)
                {
                    history.Add(new CommittedTurn(operation.Request.UserText, rawJson));
                }
            }
            operation.Events.ReplaceWithTerminal(
                LocalLlmEvent.Completed(sequence, parsed.Intent));
        }

        private static LocalLlmEvent CancellationTerminal(
            TurnOperation operation,
            ulong sequence)
        {
            return LocalLlmEvent.Cancelled(
                sequence,
                operation.CallerCancellation.IsCancellationRequested
                    ? "Local LLM generation was cancelled."
                    : "Local LLM generation reached its explicit timeout.");
        }

        private void MarkRuntimeFault(string detail)
        {
            lock (stateGate)
            {
                SetStateLocked(
                    LocalLlmProviderState.Faulted,
                    LocalLlmFailure.RuntimeFailure,
                    detail);
            }
        }

        private static void CancelAndDrainBestEffort(ILocalLlmGeneration generation)
        {
            try
            {
                generation.Cancel();
                DrainCancelledGeneration(generation);
            }
            catch (Exception)
            {
            }
        }

        private static void DrainCancelledGeneration(ILocalLlmGeneration generation)
        {
            DateTime deadline = DateTime.UtcNow + CancellationDrainTimeout;
            while (DateTime.UtcNow < deadline)
            {
                LocalLlmRuntimeEvent item = generation.Poll();
                if (item.Type == LocalLlmRuntimeEventType.Cancelled ||
                    item.Type == LocalLlmRuntimeEventType.Completed ||
                    item.Type == LocalLlmRuntimeEventType.Error)
                {
                    return;
                }
                Thread.Sleep(PollSleepMilliseconds);
            }
            throw new InvalidOperationException(
                "Local LLM cancellation did not reach a terminal runtime event within 30 seconds.");
        }

        private void ValidateManifestAndArtifact()
        {
            if (!string.Equals(
                    artifact.ManifestId,
                    manifest.Identity.ManifestId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    artifact.ModelId,
                    manifest.Identity.ModelId,
                    StringComparison.Ordinal) ||
                artifact.FileSizeBytes != manifest.Artifact.FileSizeBytes ||
                !string.Equals(
                    artifact.Sha256,
                    manifest.Artifact.Sha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.Runtime.RuntimeId,
                    "reachy_llama",
                    StringComparison.Ordinal) ||
                manifest.Runtime.AbiVersion != RequiredRuntimeAbiVersion ||
                manifest.DeviceCompatibility.RequiresNetwork)
            {
                throw new ArgumentException(
                    "The approved artifact and manifest do not describe the same ABI-2 on-device model.");
            }

            LocalLlmExecutionProfile profile = configuration.ExecutionProfile;
            if (profile.ContextTokens > manifest.Inference.ContextLimitTokens ||
                profile.ContextTokens > manifest.Inference.MemoryEstimate.ContextTokens ||
                profile.BatchTokens > manifest.Inference.MemoryEstimate.BatchTokens ||
                profile.Threads > manifest.Inference.RecommendedThreads ||
                profile.BatchThreads > manifest.Inference.RecommendedThreads)
            {
                throw new ArgumentException(
                    "The local LLM execution profile exceeds the benchmark-backed manifest limits.",
                    nameof(configuration));
            }
        }

        private void SetStateLocked(
            LocalLlmProviderState newState,
            LocalLlmFailure failure,
            string detail)
        {
            state = newState;
            stateFailure = failure;
            stateDetail = detail ?? string.Empty;
            stateRevision = checked(stateRevision + 1UL);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyLocalLlmProvider));
            }
        }

        private sealed class CommittedTurn
        {
            public CommittedTurn(string userText, string rawIntentJson)
            {
                UserText = userText;
                RawIntentJson = rawIntentJson;
            }

            public string UserText { get; }

            public string RawIntentJson { get; }
        }

        private sealed class TurnOperation : IDisposable
        {
            public TurnOperation(
                ulong epoch,
                LocalLlmRequest request,
                CancellationTokenSource cancellation,
                BoundedEventQueue events)
            {
                Epoch = epoch;
                Request = request;
                Cancellation = cancellation;
                CallerCancellation = cancellation.Token;
                Events = events;
            }

            public ulong Epoch { get; }

            public LocalLlmRequest Request { get; }

            public CancellationTokenSource Cancellation { get; }

            public CancellationToken CallerCancellation { get; }

            public BoundedEventQueue Events { get; }

            public Task? Worker { get; set; }

            public void Dispose()
            {
                Cancellation.Dispose();
                Events.Dispose();
            }
        }

        private sealed class BoundedEventQueue : IDisposable
        {
            private readonly object gate = new object();
            private readonly ConcurrentQueue<LocalLlmEvent> queue =
                new ConcurrentQueue<LocalLlmEvent>();
            private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            private readonly int capacity;
            private bool completed;
            private int count;

            public BoundedEventQueue(int capacity)
            {
                this.capacity = capacity;
            }

            public bool TryWrite(LocalLlmEvent item)
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }
                lock (gate)
                {
                    if (completed || count >= capacity)
                    {
                        return false;
                    }
                    queue.Enqueue(item);
                    ++count;
                    signal.Release();
                    return true;
                }
            }

            public void ReplaceWithTerminal(LocalLlmEvent terminal)
            {
                if (terminal == null || !terminal.IsTerminal)
                {
                    throw new ArgumentException(
                        "Replacement event must be terminal.",
                        nameof(terminal));
                }
                lock (gate)
                {
                    while (queue.TryDequeue(out _))
                    {
                        --count;
                    }
                    queue.Enqueue(terminal);
                    count = 1;
                    signal.Release();
                }
            }

            public async ValueTask<LocalLlmEvent?> ReadAsync()
            {
                while (true)
                {
                    lock (gate)
                    {
                        if (queue.TryDequeue(out LocalLlmEvent? item))
                        {
                            --count;
                            return item;
                        }
                        if (completed)
                        {
                            return null;
                        }
                    }
                    await signal.WaitAsync().ConfigureAwait(false);
                }
            }

            public void Complete()
            {
                lock (gate)
                {
                    if (completed)
                    {
                        return;
                    }
                    completed = true;
                    signal.Release();
                }
            }

            public void Dispose()
            {
                signal.Dispose();
            }
        }
    }
}
