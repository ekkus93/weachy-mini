#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed class LocalLlmProvider : IAsyncDisposable
    {
        private static readonly TimeSpan CancellationDrainTimeout = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly ILocalLlmRuntime runtime;
        private readonly LocalModelManifest manifest;
        private readonly LocalModelApprovedArtifact artifact;
        private readonly LocalLlmExecutionProfile profile;
        private readonly bool ownsRuntime;

        private LocalLlmProviderState state;
        private ulong modelHandle;
        private ulong conversationEpoch = 1UL;
        private CancellationTokenSource? activeCancellation;
        private Task<LocalLlmGenerationResult>? activeGenerationTask;

        private LocalLlmProvider(
            ILocalLlmRuntime runtime,
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ulong modelHandle,
            bool ownsRuntime)
        {
            this.runtime = runtime;
            this.manifest = manifest;
            this.artifact = artifact;
            this.profile = profile;
            this.modelHandle = modelHandle;
            this.ownsRuntime = ownsRuntime;
            state = LocalLlmProviderState.Ready;
        }

        public LocalLlmProviderState State
        {
            get
            {
                lock (sync)
                {
                    return state;
                }
            }
        }

        public ulong ConversationEpoch
        {
            get
            {
                lock (sync)
                {
                    return conversationEpoch;
                }
            }
        }

        public LocalLlmExecutionProfile ExecutionProfile => profile;

        public string ModelId => manifest.Identity.ModelId;

        public static Task<LocalLlmProviderCreationResult> CreateAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            CancellationToken cancellationToken)
        {
            return CreateCoreAsync(
                manifest,
                artifact,
                profile,
                new ReachyLlamaLocalLlmRuntime(),
                ownsRuntime: true,
                cancellationToken);
        }

        internal static Task<LocalLlmProviderCreationResult> CreateForTestingAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ILocalLlmRuntime runtime,
            CancellationToken cancellationToken)
        {
            return CreateCoreAsync(
                manifest,
                artifact,
                profile,
                runtime,
                ownsRuntime: false,
                cancellationToken);
        }

        public Task<LocalLlmGenerationResult> GenerateAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return Task.FromResult(Result(
                        LocalLlmGenerationStatus.Disposed,
                        request.RequestId,
                        conversationEpoch,
                        "The local LLM provider is disposed."));
                }
                if (state == LocalLlmProviderState.Generating || activeGenerationTask != null)
                {
                    return Task.FromResult(Result(
                        LocalLlmGenerationStatus.Busy,
                        request.RequestId,
                        conversationEpoch,
                        "The local LLM provider already has an active generation."));
                }
                if (state != LocalLlmProviderState.Ready || modelHandle == 0UL)
                {
                    return Task.FromResult(Result(
                        LocalLlmGenerationStatus.Unavailable,
                        request.RequestId,
                        conversationEpoch,
                        "The local LLM provider is not ready."));
                }

                string? validationFailure = ValidateRequest(request);
                if (validationFailure != null)
                {
                    return Task.FromResult(Result(
                        LocalLlmGenerationStatus.InvalidRequest,
                        request.RequestId,
                        conversationEpoch,
                        validationFailure));
                }

                ulong epoch = conversationEpoch;
                var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                activeCancellation = linkedCancellation;
                state = LocalLlmProviderState.Generating;
                Task<LocalLlmGenerationResult> task = Task.Run(
                    () => RunGenerationWrapperAsync(
                        request,
                        sink,
                        epoch,
                        linkedCancellation),
                    CancellationToken.None);
                activeGenerationTask = task;
                return task;
            }
        }

        public ulong ResetConversation()
        {
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(LocalLlmProvider));
                }
                conversationEpoch = NextEpoch(conversationEpoch);
                activeCancellation?.Cancel();
                return conversationEpoch;
            }
        }

        public async Task<LocalLlmReloadResult> ReloadAsync(
            CancellationToken cancellationToken)
        {
            ulong handleToUnload;
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Disposed,
                        "The local LLM provider is disposed.",
                        0);
                }
                if (state == LocalLlmProviderState.Generating || activeGenerationTask != null)
                {
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Busy,
                        "The local LLM provider cannot reload during generation.",
                        ReachyLlamaNativeContract.StatusBusy);
                }
                handleToUnload = modelHandle;
                state = LocalLlmProviderState.Loading;
            }

            try
            {
                LocalLlmBehaviorContract.ValidateFrozenBytes();
                LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
                profile.ValidateAgainst(manifest);
                if (runtime.GetAbiVersion() != ReachyLlamaNativeContract.AbiVersion)
                {
                    SetState(LocalLlmProviderState.Faulted);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Unavailable,
                        "The installed reachy_llama runtime does not expose ABI 2.",
                        ReachyLlamaNativeContract.StatusAbiMismatch);
                }

                if (handleToUnload != 0UL)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(handleToUnload),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        SetState(LocalLlmProviderState.Faulted);
                        return new LocalLlmReloadResult(
                            LocalLlmReloadStatus.RuntimeFailure,
                            unload.Detail,
                            unload.Status);
                    }
                    lock (sync)
                    {
                        if (modelHandle == handleToUnload)
                        {
                            modelHandle = 0UL;
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    SetState(LocalLlmProviderState.Unavailable);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Cancelled,
                        "Local LLM reload was cancelled after the prior model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                LocalLlmRuntimeLoadResult load = await Task.Run(
                    () => runtime.LoadModel(artifact.FullPath, checkTensors: true),
                    CancellationToken.None).ConfigureAwait(false);
                if (!load.Succeeded || load.ModelHandle == 0UL)
                {
                    SetState(LocalLlmProviderState.Faulted);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.RuntimeFailure,
                        load.Detail.Length == 0
                            ? "reachy_llama did not return a model handle during reload."
                            : load.Detail,
                        load.Status);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    LocalLlmRuntimeCallResult cancelledUnload = await Task.Run(
                        () => runtime.UnloadModel(load.ModelHandle),
                        CancellationToken.None).ConfigureAwait(false);
                    SetModelHandle(0UL, LocalLlmProviderState.Unavailable);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Cancelled,
                        cancelledUnload.Succeeded
                            ? "Local LLM reload was cancelled and the newly loaded model was unloaded."
                            : "Local LLM reload was cancelled, but cleanup of the newly loaded model failed: " + cancelledUnload.Detail,
                        cancelledUnload.Succeeded
                            ? ReachyLlamaNativeContract.StatusCancelled
                            : cancelledUnload.Status);
                }

                SetModelHandle(load.ModelHandle, LocalLlmProviderState.Ready);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.Reloaded,
                    string.Empty,
                    ReachyLlamaNativeContract.StatusOk);
            }
            catch (OperationCanceledException)
            {
                SetState(LocalLlmProviderState.Unavailable);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.Cancelled,
                    "Local LLM reload was cancelled.",
                    ReachyLlamaNativeContract.StatusCancelled);
            }
            catch (ArgumentException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (InvalidOperationException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (DllNotFoundException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message);
            }
            catch (EntryPointNotFoundException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message);
            }
            catch (BadImageFormatException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task<LocalLlmGenerationResult>? activeTask;
            CancellationTokenSource? cancellation;
            ulong handle;
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return;
                }
                state = LocalLlmProviderState.Disposed;
                conversationEpoch = NextEpoch(conversationEpoch);
                cancellation = activeCancellation;
                activeTask = activeGenerationTask;
                cancellation?.Cancel();
                handle = modelHandle;
            }

            if (activeTask != null)
            {
                await activeTask.ConfigureAwait(false);
            }

            lock (sync)
            {
                handle = modelHandle;
                modelHandle = 0UL;
            }
            if (handle != 0UL)
            {
                await Task.Run(
                    () => runtime.UnloadModel(handle),
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (ownsRuntime)
            {
                runtime.Dispose();
            }
            cancellation?.Dispose();
            GC.SuppressFinalize(this);
        }

        private static async Task<LocalLlmProviderCreationResult> CreateCoreAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ILocalLlmRuntime runtime,
            bool ownsRuntime,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            try
            {
                if (manifest == null || artifact == null || profile == null)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.InvalidConfiguration,
                        "Local LLM creation requires a manifest, approved artifact, and execution profile.",
                        ReachyLlamaNativeContract.StatusInvalidArgument,
                        runtime,
                        ownsRuntime);
                }
                LocalLlmBehaviorContract.ValidateFrozenBytes();
                LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
                profile.ValidateAgainst(manifest);
                if (string.IsNullOrWhiteSpace(artifact.FullPath) ||
                    artifact.FullPath.IndexOf('\0') >= 0 ||
                    !Path.IsPathRooted(artifact.FullPath))
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.InvalidConfiguration,
                        "The approved local-model artifact path is not an absolute safe path.",
                        ReachyLlamaNativeContract.StatusInvalidArgument,
                        runtime,
                        ownsRuntime);
                }
                if (runtime.GetAbiVersion() != ReachyLlamaNativeContract.AbiVersion)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Unavailable,
                        "The installed reachy_llama runtime does not expose ABI 2.",
                        ReachyLlamaNativeContract.StatusAbiMismatch,
                        runtime,
                        ownsRuntime);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Cancelled,
                        "Local LLM provider creation was cancelled before model load.",
                        ReachyLlamaNativeContract.StatusCancelled,
                        runtime,
                        ownsRuntime);
                }

                LocalLlmRuntimeLoadResult load = await Task.Run(
                    () => runtime.LoadModel(artifact.FullPath, checkTensors: true),
                    CancellationToken.None).ConfigureAwait(false);
                if (!load.Succeeded || load.ModelHandle == 0UL)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.RuntimeFailure,
                        load.Detail.Length == 0
                            ? "reachy_llama did not return a model handle."
                            : load.Detail,
                        load.Status,
                        runtime,
                        ownsRuntime);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(load.ModelHandle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (ownsRuntime)
                    {
                        runtime.Dispose();
                    }
                    return new LocalLlmProviderCreationResult(
                        LocalLlmProviderCreationStatus.Cancelled,
                        unload.Succeeded
                            ? "Local LLM provider creation was cancelled and the loaded model was unloaded."
                            : "Local LLM creation was cancelled, but model cleanup failed: " + unload.Detail,
                        unload.Succeeded
                            ? ReachyLlamaNativeContract.StatusCancelled
                            : unload.Status,
                        null);
                }

                var provider = new LocalLlmProvider(
                    runtime,
                    manifest,
                    artifact,
                    profile,
                    load.ModelHandle,
                    ownsRuntime);
                return new LocalLlmProviderCreationResult(
                    LocalLlmProviderCreationStatus.Created,
                    string.Empty,
                    ReachyLlamaNativeContract.StatusOk,
                    provider);
            }
            catch (ArgumentException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument,
                    runtime,
                    ownsRuntime);
            }
            catch (InvalidOperationException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument,
                    runtime,
                    ownsRuntime);
            }
            catch (DllNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound,
                    runtime,
                    ownsRuntime);
            }
            catch (EntryPointNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound,
                    runtime,
                    ownsRuntime);
            }
            catch (BadImageFormatException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound,
                    runtime,
                    ownsRuntime);
            }
        }

        private async Task<LocalLlmGenerationResult> RunGenerationWrapperAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            ulong epoch,
            CancellationTokenSource linkedCancellation)
        {
            try
            {
                return await RunGenerationCoreAsync(
                    request,
                    sink,
                    epoch,
                    linkedCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(activeCancellation, linkedCancellation))
                    {
                        activeCancellation = null;
                        activeGenerationTask = null;
                        if (state != LocalLlmProviderState.Disposed &&
                            state != LocalLlmProviderState.Faulted)
                        {
                            state = modelHandle == 0UL
                                ? LocalLlmProviderState.Unavailable
                                : LocalLlmProviderState.Ready;
                        }
                    }
                }
                linkedCancellation.Dispose();
            }
        }

        private async Task<LocalLlmGenerationResult> RunGenerationCoreAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            ulong epoch,
            CancellationToken cancellationToken)
        {
            if (!IsCurrentEpoch(epoch))
            {
                return Result(
                    LocalLlmGenerationStatus.Superseded,
                    request.RequestId,
                    epoch,
                    "The conversation was reset before generation started.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(request.RequestId, epoch);
            }

            IReadOnlyList<LocalLlmRuntimeChatMessage> messages;
            try
            {
                messages = BuildRuntimeMessages(request);
            }
            catch (ArgumentException exception)
            {
                return Result(
                    LocalLlmGenerationStatus.InvalidRequest,
                    request.RequestId,
                    epoch,
                    exception.Message);
            }

            LocalLlmRuntimeTemplateResult template = runtime.ApplyChatTemplate(
                modelHandle,
                manifest.Inference.ChatTemplate,
                messages);
            if (!template.Succeeded)
            {
                return RuntimeFailure(request.RequestId, epoch, template.Status, template.Detail);
            }
            LocalLlmRuntimeTokenCountResult tokenCount = runtime.CountTokens(
                modelHandle,
                template.Prompt);
            if (!tokenCount.Succeeded)
            {
                return RuntimeFailure(request.RequestId, epoch, tokenCount.Status, tokenCount.Detail);
            }
            long totalTokens = (long)tokenCount.TokenCount + profile.MaximumGeneratedTokens;
            if (totalTokens > profile.ContextTokens)
            {
                return Result(
                    LocalLlmGenerationStatus.ContextLimit,
                    request.RequestId,
                    epoch,
                    "The exact templated prompt plus requested output exceeds the configured context.",
                    ReachyLlamaNativeContract.StatusContextLimit);
            }
            if (cancellationToken.IsCancellationRequested || !IsCurrentEpoch(epoch))
            {
                return CancellationResult(request.RequestId, epoch);
            }

            LocalLlmRuntimeStartResult start = runtime.StartConstrained(
                modelHandle,
                template.Prompt,
                profile,
                LocalLlmBehaviorContract.Grammar,
                LocalLlmBehaviorContract.GrammarRoot);
            if (!start.Succeeded || start.GenerationHandle == 0UL)
            {
                if (start.Status == ReachyLlamaNativeContract.StatusBusy)
                {
                    return Result(
                        LocalLlmGenerationStatus.Busy,
                        request.RequestId,
                        epoch,
                        start.Detail,
                        start.Status);
                }
                if (start.Status == ReachyLlamaNativeContract.StatusContextLimit)
                {
                    return Result(
                        LocalLlmGenerationStatus.ContextLimit,
                        request.RequestId,
                        epoch,
                        start.Detail,
                        start.Status);
                }
                return RuntimeFailure(request.RequestId, epoch, start.Status, start.Detail);
            }

            ulong generationHandle = start.GenerationHandle;
            var response = new StringBuilder();
            int responseUtf8Bytes = 0;
            bool cancellationIssued = false;
            bool outputLimit = false;
            bool consumerFailure = false;
            string consumerFailureDetail = string.Empty;
            bool sequenceSeen = false;
            ulong lastSequence = 0UL;
            Stopwatch? cancellationDrain = null;
            LocalLlmRuntimePollResult? terminal = null;

            while (terminal == null)
            {
                bool superseded = !IsCurrentEpoch(epoch);
                if ((cancellationToken.IsCancellationRequested || superseded ||
                    outputLimit || consumerFailure) && !cancellationIssued)
                {
                    LocalLlmRuntimeCallResult cancel = runtime.Cancel(generationHandle);
                    cancellationIssued = true;
                    cancellationDrain = Stopwatch.StartNew();
                    if (!cancel.Succeeded && cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
                    {
                        bool cleaned = await TryDrainAndReleaseAfterPollFailureAsync(
                            generationHandle).ConfigureAwait(false);
                        if (!cleaned)
                        {
                            MarkFaulted();
                        }
                        return RuntimeFailure(
                            request.RequestId,
                            epoch,
                            cancel.Status,
                            "Failed to cancel local LLM generation: " + cancel.Detail);
                    }
                }
                if (cancellationIssued && cancellationDrain != null &&
                    cancellationDrain.Elapsed > CancellationDrainTimeout)
                {
                    MarkFaulted();
                    return RuntimeFailure(
                        request.RequestId,
                        epoch,
                        ReachyLlamaNativeContract.StatusInternalError,
                        "Timed out while draining a cancelled local LLM generation.");
                }

                LocalLlmRuntimePollResult poll = runtime.Poll(generationHandle);
                if (!poll.Succeeded)
                {
                    bool cleaned = await TryDrainAndReleaseAfterPollFailureAsync(
                        generationHandle).ConfigureAwait(false);
                    if (!cleaned)
                    {
                        MarkFaulted();
                    }
                    return RuntimeFailure(
                        request.RequestId,
                        epoch,
                        poll.Status,
                        poll.Detail);
                }

                switch (poll.Kind)
                {
                    case LocalLlmRuntimePollKind.None:
                        await Task.Delay(1).ConfigureAwait(false);
                        break;
                    case LocalLlmRuntimePollKind.Text:
                        if (sequenceSeen && poll.Sequence <= lastSequence)
                        {
                            bool cleaned = await CancelDrainReleaseAsync(
                                generationHandle).ConfigureAwait(false);
                            if (!cleaned)
                            {
                                MarkFaulted();
                            }
                            return RuntimeFailure(
                                request.RequestId,
                                epoch,
                                ReachyLlamaNativeContract.StatusInternalError,
                                "reachy_llama emitted a non-monotonic stream sequence.");
                        }
                        sequenceSeen = true;
                        lastSequence = poll.Sequence;
                        if (cancellationIssued || cancellationToken.IsCancellationRequested ||
                            !IsCurrentEpoch(epoch) || outputLimit || consumerFailure)
                        {
                            break;
                        }
                        int fragmentBytes = Encoding.UTF8.GetByteCount(poll.Text);
                        if (fragmentBytes > profile.MaximumResponseUtf8Bytes - responseUtf8Bytes)
                        {
                            outputLimit = true;
                            break;
                        }
                        responseUtf8Bytes += fragmentBytes;
                        response.Append(poll.Text);
                        try
                        {
                            await sink.OnEventAsync(
                                new LocalLlmStreamEvent(
                                    LocalLlmStreamEventType.Text,
                                    poll.Sequence,
                                    poll.Text,
                                    string.Empty),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                        }
                        catch (Exception exception)
                        {
                            consumerFailure = true;
                            consumerFailureDetail = exception.Message;
                        }
                        break;
                    case LocalLlmRuntimePollKind.Completed:
                    case LocalLlmRuntimePollKind.Cancelled:
                    case LocalLlmRuntimePollKind.Error:
                        terminal = poll;
                        break;
                    default:
                        bool releasedUnknown = await CancelDrainReleaseAsync(
                            generationHandle).ConfigureAwait(false);
                        if (!releasedUnknown)
                        {
                            MarkFaulted();
                        }
                        return RuntimeFailure(
                            request.RequestId,
                            epoch,
                            ReachyLlamaNativeContract.StatusInternalError,
                            "The local LLM runtime returned an unknown poll event.");
                }
            }

            LocalLlmRuntimeMetricsResult metricsResult = runtime.GetGenerationMetrics(
                generationHandle);
            LocalLlmRuntimeCallResult release = runtime.Release(generationHandle);
            if (!release.Succeeded)
            {
                MarkFaulted();
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    release.Status,
                    "Failed to release local LLM generation: " + release.Detail);
            }
            if (!metricsResult.Succeeded)
            {
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    metricsResult.Status,
                    metricsResult.Detail);
            }

            if (consumerFailure)
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Error,
                    lastSequence,
                    "The local LLM stream consumer failed.").ConfigureAwait(false);
                return Result(
                    LocalLlmGenerationStatus.ConsumerFailure,
                    request.RequestId,
                    epoch,
                    consumerFailureDetail.Length == 0
                        ? "The local LLM stream consumer failed."
                        : consumerFailureDetail,
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }
            if (outputLimit)
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Error,
                    lastSequence,
                    "The local LLM response exceeded the managed output-byte limit.").ConfigureAwait(false);
                return Result(
                    LocalLlmGenerationStatus.OutputLimit,
                    request.RequestId,
                    epoch,
                    "The local LLM response exceeded the managed output-byte limit.",
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }
            if (!IsCurrentEpoch(epoch))
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Superseded,
                    lastSequence,
                    "The conversation reset superseded this generation.").ConfigureAwait(false);
                return Result(
                    LocalLlmGenerationStatus.Superseded,
                    request.RequestId,
                    epoch,
                    "The conversation reset superseded this generation.",
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }
            if (cancellationToken.IsCancellationRequested ||
                terminal.Kind == LocalLlmRuntimePollKind.Cancelled)
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Cancelled,
                    lastSequence,
                    "The local LLM generation was cancelled.").ConfigureAwait(false);
                return Result(
                    LocalLlmGenerationStatus.Cancelled,
                    request.RequestId,
                    epoch,
                    "The local LLM generation was cancelled.",
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }
            if (terminal.Kind == LocalLlmRuntimePollKind.Error)
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Error,
                    lastSequence,
                    terminal.Detail).ConfigureAwait(false);
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    terminal.EventStatus,
                    terminal.Detail,
                    metricsResult.Metrics);
            }
            if (terminal.Kind != LocalLlmRuntimePollKind.Completed)
            {
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM generation terminated without a completed event.",
                    metricsResult.Metrics);
            }

            if (!LocalLlmBehaviorContract.TryParseIntent(
                    response.ToString(),
                    out LocalLlmBehaviorIntent? intent,
                    out string parseDetail) || intent == null)
            {
                await TryPublishTerminalAsync(
                    sink,
                    LocalLlmStreamEventType.Error,
                    lastSequence,
                    parseDetail).ConfigureAwait(false);
                return Result(
                    LocalLlmGenerationStatus.InvalidIntent,
                    request.RequestId,
                    epoch,
                    parseDetail,
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }

            try
            {
                await sink.OnEventAsync(
                    new LocalLlmStreamEvent(
                        LocalLlmStreamEventType.Completed,
                        lastSequence,
                        string.Empty,
                        "Behavior intent validated."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result(
                    IsCurrentEpoch(epoch)
                        ? LocalLlmGenerationStatus.Cancelled
                        : LocalLlmGenerationStatus.Superseded,
                    request.RequestId,
                    epoch,
                    "The request was cancelled while publishing the terminal validated event.",
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }
            catch (Exception exception)
            {
                return Result(
                    LocalLlmGenerationStatus.ConsumerFailure,
                    request.RequestId,
                    epoch,
                    exception.Message,
                    terminal.EventStatus,
                    null,
                    metricsResult.Metrics);
            }

            return Result(
                LocalLlmGenerationStatus.Succeeded,
                request.RequestId,
                epoch,
                string.Empty,
                terminal.EventStatus,
                intent,
                metricsResult.Metrics);
        }

        private IReadOnlyList<LocalLlmRuntimeChatMessage> BuildRuntimeMessages(
            LocalLlmGenerationRequest request)
        {
            var messages = new List<LocalLlmRuntimeChatMessage>(request.Messages.Count + 1)
            {
                new LocalLlmRuntimeChatMessage("system", LocalLlmBehaviorContract.SystemPrompt),
            };
            for (int index = 0; index < request.Messages.Count; ++index)
            {
                LocalLlmChatMessage message = request.Messages[index];
                string content = message.Content;
                if (index == request.Messages.Count - 1)
                {
                    int finalLength = checked(
                        content.Length + 1 + LocalLlmBehaviorContract.UserPromptSuffix.Length);
                    if (finalLength > profile.MaximumMessageCharacters)
                    {
                        throw new ArgumentException(
                            "The final user message plus the selected model suffix exceeds the configured message limit.",
                            nameof(request));
                    }
                    content = content + "\n" + LocalLlmBehaviorContract.UserPromptSuffix;
                }
                messages.Add(new LocalLlmRuntimeChatMessage(
                    message.Role == LocalLlmChatRole.User ? "user" : "assistant",
                    content));
            }
            return messages;
        }

        private string? ValidateRequest(LocalLlmGenerationRequest request)
        {
            if (request.Messages.Count > profile.MaximumConversationMessages)
            {
                return "The local LLM request exceeds the configured conversation-message limit.";
            }
            for (int index = 0; index < request.Messages.Count; ++index)
            {
                if (request.Messages[index].Content.Length > profile.MaximumMessageCharacters)
                {
                    return "The local LLM request contains a message beyond the configured character limit.";
                }
            }
            return null;
        }

        private async Task<bool> CancelDrainReleaseAsync(ulong generationHandle)
        {
            LocalLlmRuntimeCallResult cancel = runtime.Cancel(generationHandle);
            if (!cancel.Succeeded && cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
            {
                return false;
            }
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed <= CancellationDrainTimeout)
            {
                LocalLlmRuntimePollResult poll = runtime.Poll(generationHandle);
                if (!poll.Succeeded)
                {
                    return false;
                }
                if (poll.Kind == LocalLlmRuntimePollKind.Cancelled ||
                    poll.Kind == LocalLlmRuntimePollKind.Completed ||
                    poll.Kind == LocalLlmRuntimePollKind.Error)
                {
                    LocalLlmRuntimeCallResult release = runtime.Release(generationHandle);
                    return release.Succeeded;
                }
                await Task.Delay(1).ConfigureAwait(false);
            }
            return false;
        }

        private async Task<bool> TryDrainAndReleaseAfterPollFailureAsync(
            ulong generationHandle)
        {
            return await CancelDrainReleaseAsync(generationHandle).ConfigureAwait(false);
        }

        private static async Task TryPublishTerminalAsync(
            ILocalLlmStreamSink sink,
            LocalLlmStreamEventType type,
            ulong sequence,
            string detail)
        {
            try
            {
                await sink.OnEventAsync(
                    new LocalLlmStreamEvent(
                        type,
                        sequence,
                        string.Empty,
                        LocalLlmGenerationResult.BoundDiagnostic(detail)),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The primary operation already has a terminal failure/cancel disposition.
                // A secondary terminal-notification failure must not rewrite that disposition.
            }
        }

        private bool IsCurrentEpoch(ulong epoch)
        {
            lock (sync)
            {
                return conversationEpoch == epoch && state != LocalLlmProviderState.Disposed;
            }
        }

        private void MarkFaulted()
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    state = LocalLlmProviderState.Faulted;
                }
            }
        }

        private void SetState(LocalLlmProviderState newState)
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    state = newState;
                }
            }
        }

        private void SetModelHandle(ulong handle, LocalLlmProviderState newState)
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    modelHandle = handle;
                    state = newState;
                }
            }
        }

        private LocalLlmGenerationResult CancellationResult(
            string requestId,
            ulong epoch)
        {
            return Result(
                IsCurrentEpoch(epoch)
                    ? LocalLlmGenerationStatus.Cancelled
                    : LocalLlmGenerationStatus.Superseded,
                requestId,
                epoch,
                IsCurrentEpoch(epoch)
                    ? "The local LLM generation was cancelled."
                    : "The conversation reset superseded this generation.",
                ReachyLlamaNativeContract.StatusCancelled);
        }

        private static LocalLlmGenerationResult RuntimeFailure(
            string requestId,
            ulong epoch,
            int nativeStatus,
            string detail,
            LocalLlmGenerationMetrics? metrics = null)
        {
            return Result(
                LocalLlmGenerationStatus.RuntimeFailure,
                requestId,
                epoch,
                detail.Length == 0 ? "The local LLM runtime failed." : detail,
                nativeStatus,
                null,
                metrics);
        }

        private static LocalLlmGenerationResult Result(
            LocalLlmGenerationStatus status,
            string requestId,
            ulong epoch,
            string detail,
            int nativeStatus = 0,
            LocalLlmBehaviorIntent? intent = null,
            LocalLlmGenerationMetrics? metrics = null)
        {
            return new LocalLlmGenerationResult(
                status,
                requestId,
                epoch,
                detail,
                nativeStatus,
                intent,
                metrics);
        }

        private static LocalLlmProviderCreationResult CreationFailure(
            LocalLlmProviderCreationStatus status,
            string detail,
            int nativeStatus,
            ILocalLlmRuntime runtime,
            bool ownsRuntime)
        {
            if (ownsRuntime)
            {
                runtime.Dispose();
            }
            return new LocalLlmProviderCreationResult(
                status,
                detail,
                nativeStatus,
                null);
        }

        private static LocalLlmReloadResult RuntimeReloadFailure(string detail)
        {
            return new LocalLlmReloadResult(
                LocalLlmReloadStatus.RuntimeFailure,
                detail,
                ReachyLlamaNativeContract.StatusInternalError);
        }

        private static ulong NextEpoch(ulong current)
        {
            ulong next = unchecked(current + 1UL);
            return next == 0UL ? 1UL : next;
        }
    }
}
