#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                    operation = new TurnOperation(
                        conversationEpoch,
                        request,
                        configuration.ManagedEventQueueCapacity,
                        cancellationToken);
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
                active.ProviderCancellation.Cancel();
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
                    operation?.ProviderCancellation.Cancel();
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
            Exception? disposalFailure = null;
            try
            {
                TurnOperation? operation;
                ILocalLlmModelSession? session;
                lock (stateGate)
                {
                    conversationEpoch = checked(conversationEpoch + 1UL);
                    operation = activeOperation;
                    operation?.ProviderCancellation.Cancel();
                    session = modelSession;
                    modelSession = null;
                }
                if (operation?.Worker != null)
                {
                    await operation.Worker.ConfigureAwait(false);
                }
                try
                {
                    session?.Dispose();
                }
                catch (Exception exception)
                {
                    disposalFailure = exception;
                }
                lock (stateGate)
                {
                    history.Clear();
                    activeOperation = null;
                    SetStateLocked(
                        LocalLlmProviderState.Disposed,
                        LocalLlmFailure.Disposed,
                        disposalFailure == null
                            ? "The local LLM provider is disposed."
                            : "The local LLM provider disposed with an explicit native cleanup failure.");
                }
            }
            finally
            {
                lifecycleGate.Release();
                lifecycleGate.Dispose();
            }

            if (disposalFailure != null)
            {
                throw new InvalidOperationException(
                    "Local LLM provider disposal could not release the native model cleanly.",
                    disposalFailure);
            }
        }

        private async Task<LocalLlmOperationResult> LoadCoreAsync(
            bool reload,
            CancellationToken cancellationToken)
        {
            ILocalLlmModelSession? previous;
            lock (stateGate)
            {
                if (activeOperation != null)
                {
                    return LocalLlmOperationResult.Failed(
                        LocalLlmFailure.Busy,
                        "The model cannot be loaded or reloaded during active generation.");
                }
                if (!reload && state == LocalLlmProviderState.Faulted)
                {
                    return LocalLlmOperationResult.Failed(
                        LocalLlmFailure.RuntimeFailure,
                        "The local LLM provider is faulted; explicit ReloadAsync is required.");
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
                previous = modelSession;
                modelSession = null;
            }

            if (previous != null)
            {
                try
                {
                    previous.Dispose();
                }
                catch (Exception exception)
                {
                    lock (stateGate)
                    {
                        SetStateLocked(
                            LocalLlmProviderState.Faulted,
                            LocalLlmFailure.RuntimeFailure,
                            "The previous local model could not be released before explicit reload: " +
                            exception.GetType().Name + ".");
                    }
                    return LocalLlmOperationResult.Failed(
                        LocalLlmFailure.RuntimeFailure,
                        stateDetail);
                }
            }

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
            LocalLlmBehaviorIntent? validatedIntent = null;
            string rawValidatedJson = string.Empty;
            ulong nextSequence = 1UL;
            LocalLlmEvent? terminal = null;
            try
            {
                ILocalLlmModelSession session;
                List<CommittedTurn> historySnapshot;
                lock (stateGate)
                {
                    session = modelSession ??
                        throw new InvalidOperationException(
                            "The local model session disappeared before generation.");
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
                    terminal = LocalLlmEvent.Failed(
                        nextSequence,
                        LocalLlmFailure.ContextLimit,
                        "Rendered conversation plus reserved output exceeds the bounded context; reset is required.");
                    return;
                }

                operation.LinkedCancellation.Token.ThrowIfCancellationRequested();
                generation = session.StartConstrained(
                    prompt,
                    configuration.ExecutionProfile,
                    configuration.Grammar,
                    configuration.GrammarRoot);

                var output = new StringBuilder();
                bool cancellationSent = false;
                while (terminal == null && validatedIntent == null)
                {
                    if (operation.LinkedCancellation.IsCancellationRequested && !cancellationSent)
                    {
                        generation.Cancel();
                        cancellationSent = true;
                    }

                    LocalLlmRuntimeEvent nativeEvent = generation.Poll();
                    switch (nativeEvent.Type)
                    {
                        case LocalLlmRuntimeEventType.None:
                            Thread.Sleep(PollSleepMilliseconds);
                            break;
                        case LocalLlmRuntimeEventType.Text:
                            if (cancellationSent)
                            {
                                break;
                            }
                            if (nativeEvent.Text.Length >
                                MaximumRawOutputCharacters - output.Length)
                            {
                                generation.Cancel();
                                string? drainFailure = DrainCancelledGeneration(generation);
                                terminal = drainFailure == null
                                    ? LocalLlmEvent.Failed(
                                        nextSequence,
                                        LocalLlmFailure.OutputLimit,
                                        "Local LLM output exceeded the independent managed output bound.")
                                    : RuntimeCleanupFailure(nextSequence, drainFailure);
                                break;
                            }
                            output.Append(nativeEvent.Text);
                            if (!operation.Events.TryWriteDelta(
                                    LocalLlmEvent.Delta(nextSequence++, nativeEvent.Text)))
                            {
                                generation.Cancel();
                                string? drainFailure = DrainCancelledGeneration(generation);
                                terminal = drainFailure == null
                                    ? LocalLlmEvent.Failed(
                                        nextSequence,
                                        LocalLlmFailure.RuntimeFailure,
                                        "The bounded managed event queue filled; generation was cancelled.")
                                    : RuntimeCleanupFailure(nextSequence, drainFailure);
                            }
                            break;
                        case LocalLlmRuntimeEventType.Completed:
                            if (cancellationSent || operation.LinkedCancellation.IsCancellationRequested)
                            {
                                terminal = CancellationTerminal(operation, nextSequence);
                                break;
                            }
                            LocalLlmIntentParseResult parsed =
                                LocalLlmBehaviorIntentParser.Parse(output.ToString());
                            if (!parsed.Succeeded || parsed.Intent == null)
                            {
                                terminal = LocalLlmEvent.Failed(
                                    nextSequence,
                                    LocalLlmFailure.InvalidIntent,
                                    "Constrained generation completed but independent behavior-intent validation failed: " +
                                    parsed.Failure + ".");
                                break;
                            }
                            if (parsed.Intent.GazeTarget != null &&
                                !operation.Request.IsTrackedEntityAllowed(
                                    parsed.Intent.GazeTarget.EntityId))
                            {
                                terminal = LocalLlmEvent.Failed(
                                    nextSequence,
                                    LocalLlmFailure.InvalidIntent,
                                    "Behavior intent referenced a tracked entity outside the request's current allowlist.");
                                break;
                            }
                            validatedIntent = parsed.Intent;
                            rawValidatedJson = output.ToString();
                            break;
                        case LocalLlmRuntimeEventType.Cancelled:
                            terminal = CancellationTerminal(operation, nextSequence);
                            break;
                        case LocalLlmRuntimeEventType.Error:
                            terminal = LocalLlmEvent.Failed(
                                nextSequence,
                                LocalLlmFailure.RuntimeFailure,
                                "Local LLM runtime reported terminal status " +
                                nativeEvent.Status + ".");
                            MarkRuntimeFault(terminal.Detail);
                            break;
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
                    string? cleanupFailure = CancelAndDrain(generation);
                    terminal = cleanupFailure == null
                        ? CancellationTerminal(operation, nextSequence)
                        : RuntimeCleanupFailure(nextSequence, cleanupFailure);
                }
                else
                {
                    terminal = CancellationTerminal(operation, nextSequence);
                }
            }
            catch (Exception exception)
            {
                string cleanupDetail = string.Empty;
                if (generation != null)
                {
                    string? cleanupFailure = CancelAndDrain(generation);
                    if (cleanupFailure != null)
                    {
                        cleanupDetail = " Cleanup also failed: " + cleanupFailure;
                    }
                }
                string detail =
                    "Local LLM runtime operation failed explicitly: " +
                    exception.GetType().Name + "." + cleanupDetail;
                MarkRuntimeFault(detail);
                terminal = LocalLlmEvent.Failed(
                    nextSequence,
                    LocalLlmFailure.RuntimeFailure,
                    detail);
            }
            finally
            {
                if (generation != null)
                {
                    if (terminal?.Failure == LocalLlmFailure.TimedOut)
                    {
                        terminal = AttachTimeoutMetrics(terminal, generation);
                    }
                    try
                    {
                        generation.Dispose();
                    }
                    catch (Exception exception)
                    {
                        string detail =
                            "Local LLM generation release failed explicitly: " +
                            exception.GetType().Name + ".";
                        MarkRuntimeFault(detail);
                        terminal = LocalLlmEvent.Failed(
                            nextSequence,
                            LocalLlmFailure.RuntimeFailure,
                            detail);
                        validatedIntent = null;
                        rawValidatedJson = string.Empty;
                    }
                }

                if (terminal == null && validatedIntent != null)
                {
                    terminal = CommitValidatedTurn(
                        operation,
                        rawValidatedJson,
                        validatedIntent,
                        nextSequence);
                }
                terminal ??= LocalLlmEvent.Failed(
                    nextSequence,
                    LocalLlmFailure.RuntimeFailure,
                    "Local LLM generation ended without a terminal result.");
                if (!operation.Events.TryWriteTerminal(terminal))
                {
                    operation.Events.ReplaceWithTerminal(
                        LocalLlmEvent.Failed(
                            nextSequence,
                            LocalLlmFailure.RuntimeFailure,
                            "The managed event queue could not publish the terminal generation result."));
                }

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
                messages.Add(new LocalLlmChatMessage(
                    "user",
                    BuildUserMessage(turn.UserText)));
                messages.Add(new LocalLlmChatMessage("assistant", turn.RawIntentJson));
            }
            messages.Add(new LocalLlmChatMessage(
                "user",
                BuildUserMessage(userText)));
            return messages;
        }

        private string BuildUserMessage(string userText)
        {
            return string.IsNullOrEmpty(configuration.UserPromptSuffix)
                ? userText
                : userText + "\n" + configuration.UserPromptSuffix;
        }

        private LocalLlmEvent CommitValidatedTurn(
            TurnOperation operation,
            string rawJson,
            LocalLlmBehaviorIntent intent,
            ulong sequence)
        {
            lock (stateGate)
            {
                if (operation.Epoch != conversationEpoch ||
                    operation.LinkedCancellation.IsCancellationRequested ||
                    !ReferenceEquals(activeOperation, operation))
                {
                    return CancellationTerminal(operation, sequence);
                }
                if (configuration.MaximumCommittedHistoryTurns > 0)
                {
                    history.Add(new CommittedTurn(operation.Request.UserText, rawJson));
                }
            }
            return LocalLlmEvent.Completed(sequence, intent);
        }

        private static LocalLlmEvent CancellationTerminal(
            TurnOperation operation,
            ulong sequence)
        {
            if (operation.CallerCancellation.IsCancellationRequested ||
                operation.ProviderCancellation.IsCancellationRequested)
            {
                return LocalLlmEvent.Cancelled(
                    sequence,
                    "Local LLM generation was cancelled explicitly.");
            }
            if (operation.TimeoutCancellation.IsCancellationRequested)
            {
                return LocalLlmEvent.Failed(
                    sequence,
                    LocalLlmFailure.TimedOut,
                    "Local LLM generation reached its explicit timeout.");
            }
            return LocalLlmEvent.Cancelled(
                sequence,
                "Local LLM generation was cancelled.");
        }

        private static LocalLlmEvent AttachTimeoutMetrics(
            LocalLlmEvent terminal,
            ILocalLlmGeneration generation)
        {
            try
            {
                LocalLlmGenerationMetrics metrics = generation.GetMetrics();
                return LocalLlmEvent.Failed(
                    terminal.Sequence,
                    LocalLlmFailure.TimedOut,
                    terminal.Detail +
                    " Native progress: prompt_tokens=" + metrics.PromptTokens +
                    " generated_tokens=" + metrics.GeneratedTokens +
                    " time_to_first_token_us=" + metrics.TimeToFirstTokenMicroseconds +
                    " decode_us=" + metrics.DecodeMicroseconds + ".");
            }
            catch (Exception exception)
            {
                return LocalLlmEvent.Failed(
                    terminal.Sequence,
                    LocalLlmFailure.TimedOut,
                    terminal.Detail +
                    " Native progress metrics unavailable explicitly: " +
                    exception.GetType().Name + ".");
            }
        }

        private LocalLlmEvent RuntimeCleanupFailure(ulong sequence, string detail)
        {
            string visible = "Local LLM cancellation cleanup failed explicitly: " + detail;
            MarkRuntimeFault(visible);
            return LocalLlmEvent.Failed(
                sequence,
                LocalLlmFailure.RuntimeFailure,
                visible);
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

        private static string? CancelAndDrain(ILocalLlmGeneration generation)
        {
            try
            {
                generation.Cancel();
            }
            catch (Exception exception)
            {
                return "generation cancel threw " + exception.GetType().Name + ".";
            }
            return DrainCancelledGeneration(generation);
        }

        private static string? DrainCancelledGeneration(ILocalLlmGeneration generation)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                while (stopwatch.Elapsed < CancellationDrainTimeout)
                {
                    LocalLlmRuntimeEvent item = generation.Poll();
                    if (item.Type == LocalLlmRuntimeEventType.Cancelled ||
                        item.Type == LocalLlmRuntimeEventType.Completed ||
                        item.Type == LocalLlmRuntimeEventType.Error)
                    {
                        return null;
                    }
                    Thread.Sleep(PollSleepMilliseconds);
                }
                return "the runtime did not reach a terminal event within 30 seconds.";
            }
            catch (Exception exception)
            {
                return "cancellation drain threw " + exception.GetType().Name + ".";
            }
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
                manifest.Runtime.RequiresNetworkAccess)
            {
                throw new ArgumentException(
                    "The approved artifact and manifest do not describe the same ABI-2 on-device model.");
            }

            LocalLlmExecutionProfile profile = configuration.ExecutionProfile;
            if (profile.ContextTokens > manifest.Inference.ContextLimitTokens ||
                profile.ContextTokens > manifest.Inference.MemoryEstimate.BasisContextTokens ||
                profile.BatchTokens > manifest.Inference.MemoryEstimate.BasisBatchTokens ||
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
                int eventQueueCapacity,
                CancellationToken callerCancellation)
            {
                Epoch = epoch;
                Request = request;
                CallerCancellation = callerCancellation;
                ProviderCancellation = new CancellationTokenSource();
                TimeoutSource = new CancellationTokenSource(request.Timeout);
                TimeoutCancellation = TimeoutSource.Token;
                LinkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    CallerCancellation,
                    ProviderCancellation.Token,
                    TimeoutCancellation);
                Events = new BoundedEventQueue(eventQueueCapacity);
            }

            public ulong Epoch { get; }

            public LocalLlmRequest Request { get; }

            public CancellationToken CallerCancellation { get; }

            public CancellationTokenSource ProviderCancellation { get; }

            public CancellationTokenSource TimeoutSource { get; }

            public CancellationToken TimeoutCancellation { get; }

            public CancellationTokenSource LinkedSource { get; }

            public CancellationTokenSource LinkedCancellation => LinkedSource;

            public BoundedEventQueue Events { get; }

            public Task? Worker { get; set; }

            public void Dispose()
            {
                LinkedSource.Dispose();
                TimeoutSource.Dispose();
                ProviderCancellation.Dispose();
                Events.Dispose();
            }
        }

        private sealed class BoundedEventQueue : IDisposable
        {
            private readonly object gate = new object();
            private readonly Queue<LocalLlmEvent> queue = new Queue<LocalLlmEvent>();
            private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            private readonly int capacity;
            private bool completed;

            public BoundedEventQueue(int capacity)
            {
                if (capacity < 2)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(capacity),
                        "The managed local-LLM queue requires one delta slot plus one terminal slot.");
                }
                this.capacity = capacity;
            }

            public bool TryWriteDelta(LocalLlmEvent item)
            {
                if (item == null || item.IsTerminal)
                {
                    throw new ArgumentException(
                        "Only non-terminal local LLM events can use the delta queue path.",
                        nameof(item));
                }
                lock (gate)
                {
                    if (completed || queue.Count >= capacity - 1)
                    {
                        return false;
                    }
                    queue.Enqueue(item);
                    signal.Release();
                    return true;
                }
            }

            public bool TryWriteTerminal(LocalLlmEvent terminal)
            {
                if (terminal == null || !terminal.IsTerminal)
                {
                    throw new ArgumentException(
                        "The terminal queue path requires a terminal event.",
                        nameof(terminal));
                }
                lock (gate)
                {
                    if (completed || queue.Count >= capacity)
                    {
                        return false;
                    }
                    queue.Enqueue(terminal);
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
                    queue.Clear();
                    queue.Enqueue(terminal);
                    signal.Release();
                }
            }

            public async ValueTask<LocalLlmEvent?> ReadAsync()
            {
                while (true)
                {
                    lock (gate)
                    {
                        if (queue.Count > 0)
                        {
                            return queue.Dequeue();
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
