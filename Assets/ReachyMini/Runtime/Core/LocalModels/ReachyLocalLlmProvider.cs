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
    public sealed partial class LocalLlmProvider : IAsyncDisposable
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

        public static async Task<LocalLlmProviderCreationResult> CreateAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            CancellationToken cancellationToken)
        {
            ReachyLlamaLocalLlmRuntime? ownedRuntime = new ReachyLlamaLocalLlmRuntime();
            try
            {
                LocalLlmProviderCreationResult result = await CreateCoreAsync(
                    manifest,
                    artifact,
                    profile,
                    ownedRuntime,
                    ownsRuntime: true,
                    cancellationToken).ConfigureAwait(false);
                if (result.Provider != null)
                {
                    ownedRuntime = null;
                }
                return result;
            }
            finally
            {
                ownedRuntime?.Dispose();
            }
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
                CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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

        public async Task<LocalLlmReloadResult> ReloadAsync(CancellationToken cancellationToken)
        {
            ulong handleToUnload;
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Disposed,
                        "The local LLM provider is disposed.",
                        ReachyLlamaNativeContract.StatusNotFound);
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
                ValidateConfiguration();
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
                            "Failed to unload the current local LLM before reload: " + unload.Detail,
                            unload.Status);
                    }
                    SetModelHandle(0UL, LocalLlmProviderState.Loading);
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
                    if (!cancelledUnload.Succeeded)
                    {
                        SetState(LocalLlmProviderState.Faulted);
                        return new LocalLlmReloadResult(
                            LocalLlmReloadStatus.RuntimeFailure,
                            "Reload cancellation could not clean up the newly loaded model: " +
                                cancelledUnload.Detail,
                            cancelledUnload.Status);
                    }
                    SetModelHandle(0UL, LocalLlmProviderState.Unavailable);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Cancelled,
                        "Local LLM reload was cancelled and the newly loaded model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                SetModelHandle(load.ModelHandle, LocalLlmProviderState.Ready);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.Reloaded,
                    string.Empty,
                    ReachyLlamaNativeContract.StatusOk);
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
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (EntryPointNotFoundException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (BadImageFormatException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (Exception exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(
                    "Unexpected local LLM reload failure: " + exception.Message,
                    ReachyLlamaNativeContract.StatusInternalError);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task<LocalLlmGenerationResult>? activeTask;
            CancellationTokenSource? cancellation;
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
            }

            if (activeTask != null)
            {
                await activeTask.ConfigureAwait(false);
            }

            ulong handle;
            lock (sync)
            {
                handle = modelHandle;
                modelHandle = 0UL;
            }

            try
            {
                if (handle != 0UL)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(handle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Failed to unload the local LLM during provider disposal: " + unload.Detail);
                    }
                }
            }
            finally
            {
                if (ownsRuntime)
                {
                    runtime.Dispose();
                }
                cancellation?.Dispose();
                GC.SuppressFinalize(this);
            }
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
            if (manifest == null || artifact == null || profile == null)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    "Local LLM creation requires a manifest, approved artifact, and execution profile.",
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }

            try
            {
                LocalLlmBehaviorContract.ValidateFrozenBytes();
                LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
                profile.ValidateAgainst(manifest);
                if (string.IsNullOrWhiteSpace(artifact.FullPath) ||
                    artifact.FullPath.Contains('\0') ||
                    !Path.IsPathRooted(artifact.FullPath))
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.InvalidConfiguration,
                        "The approved local-model artifact path is not an absolute safe path.",
                        ReachyLlamaNativeContract.StatusInvalidArgument);
                }
                if (runtime.GetAbiVersion() != ReachyLlamaNativeContract.AbiVersion)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Unavailable,
                        "The installed reachy_llama runtime does not expose ABI 2.",
                        ReachyLlamaNativeContract.StatusAbiMismatch);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Cancelled,
                        "Local LLM provider creation was cancelled before model load.",
                        ReachyLlamaNativeContract.StatusCancelled);
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
                        load.Status);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(load.ModelHandle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        return CreationFailure(
                            LocalLlmProviderCreationStatus.RuntimeFailure,
                            "Creation cancellation could not clean up the loaded model: " + unload.Detail,
                            unload.Status);
                    }
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Cancelled,
                        "Local LLM provider creation was cancelled and the loaded model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                LocalLlmProvider? provider = null;
                try
                {
                    provider = new LocalLlmProvider(
                        runtime,
                        manifest,
                        artifact,
                        profile,
                        load.ModelHandle,
                        ownsRuntime);
                    LocalLlmProviderCreationResult result = new LocalLlmProviderCreationResult(
                        LocalLlmProviderCreationStatus.Created,
                        string.Empty,
                        ReachyLlamaNativeContract.StatusOk,
                        provider);
                    provider = null;
                    return result;
                }
                finally
                {
                    if (provider != null)
                    {
                        await provider.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (ArgumentException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (InvalidOperationException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (DllNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (EntryPointNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (BadImageFormatException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (Exception exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.RuntimeFailure,
                    "Unexpected local LLM creation failure: " + exception.Message,
                    ReachyLlamaNativeContract.StatusInternalError);
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
            catch (OutOfMemoryException exception)
            {
                MarkFaulted();
                return ResourceExhausted(
                    request.RequestId,
                    epoch,
                    "Local LLM generation exhausted memory before a safe terminal result: " +
                        exception.Message);
            }
            catch (Exception exception)
            {
                MarkFaulted();
                return Result(
                    LocalLlmGenerationStatus.RuntimeFailure,
                    request.RequestId,
                    epoch,
                    "Unexpected local LLM generation failure: " + exception.Message,
                    ReachyLlamaNativeContract.StatusInternalError);
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

            List<LocalLlmRuntimeChatMessage> messages;
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

            LocalLlmRuntimeTemplateResult template;
            try
            {
                // Passing a null template deliberately selects the chat template embedded
                // in the exact SHA-pinned GGUF, matching the accepted RMA-133 V6 path.
                template = runtime.ApplyChatTemplate(modelHandle, null, messages);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM chat-template application threw: " + exception.Message);
            }
            if (!template.Succeeded)
            {
                return RuntimeFailure(request.RequestId, epoch, template.Status, template.Detail);
            }

            LocalLlmRuntimeTokenCountResult tokenCount;
            try
            {
                tokenCount = runtime.CountTokens(modelHandle, template.Prompt);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM token preflight threw: " + exception.Message);
            }
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

            LocalLlmRuntimeStartResult start;
            try
            {
                start = runtime.StartConstrained(
                    modelHandle,
                    template.Prompt,
                    profile,
                    LocalLlmBehaviorContract.Grammar,
                    LocalLlmBehaviorContract.GrammarRoot);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM constrained start threw: " + exception.Message);
            }
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

            return await RunStartedGenerationAsync(
                request,
                sink,
                epoch,
                start.GenerationHandle,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<LocalLlmGenerationResult> RunStartedGenerationAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            ulong epoch,
            ulong generationHandle,
            CancellationToken cancellationToken)
        {
            StringBuilder response = new StringBuilder();
            int responseUtf8Bytes = 0;
            bool cancellationIssued = false;
            bool outputLimit = false;
            bool consumerFailure = false;
            string consumerFailureDetail = string.Empty;
            bool sequenceSeen = false;
            ulong lastSequence = 0UL;
            Stopwatch? cancellationDrain = null;
            LocalLlmRuntimePollResult? terminal = null;
            bool generationReleased = false;

            try
            {
                while (terminal == null)
                {
                    bool superseded = !IsCurrentEpoch(epoch);
                    if ((cancellationToken.IsCancellationRequested || superseded ||
                        outputLimit || consumerFailure) && !cancellationIssued)
                    {
                        LocalLlmRuntimeCallResult cancel = SafeCancel(generationHandle);
                        cancellationIssued = true;
                        cancellationDrain = Stopwatch.StartNew();
                        if (!cancel.Succeeded &&
                            cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
                        {
                            bool cleaned = await DrainAndReleaseAsync(
                                generationHandle).ConfigureAwait(false);
                            generationReleased = cleaned;
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

                    LocalLlmRuntimePollResult poll = SafePoll(generationHandle);
                    if (!poll.Succeeded)
                    {
                        bool cleaned = await CancelDrainReleaseAsync(
                            generationHandle).ConfigureAwait(false);
                        generationReleased = cleaned;
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
                            await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case LocalLlmRuntimePollKind.Text:
                            if (sequenceSeen && poll.Sequence <= lastSequence)
                            {
                                bool cleaned = await CancelDrainReleaseAsync(
                                    generationHandle).ConfigureAwait(false);
                                generationReleased = cleaned;
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
                            catch (OperationCanceledException)
                                when (cancellationToken.IsCancellationRequested)
                            {
                            }
                            catch (OutOfMemoryException)
                            {
                                throw;
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
                            generationReleased = releasedUnknown;
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

                LocalLlmRuntimeMetricsResult metricsResult = SafeGetMetrics(
                    generationHandle);
                LocalLlmRuntimeCallResult release = SafeRelease(generationHandle);
                generationReleased = release.Succeeded;
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
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.OutputLimit,
                            request.RequestId,
                            epoch,
                            "The local LLM response exceeded the managed output-byte limit.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (!IsCurrentEpoch(epoch))
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Superseded,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.Superseded,
                            request.RequestId,
                            epoch,
                            "The conversation reset superseded this generation.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (cancellationToken.IsCancellationRequested ||
                    terminal.Kind == LocalLlmRuntimePollKind.Cancelled)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Cancelled,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.Cancelled,
                            request.RequestId,
                            epoch,
                            "The local LLM generation was cancelled.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (terminal.Kind == LocalLlmRuntimePollKind.Error)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        RuntimeFailure(
                            request.RequestId,
                            epoch,
                            terminal.EventStatus,
                            terminal.Detail,
                            metricsResult.Metrics)).ConfigureAwait(false);
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
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.InvalidIntent,
                            request.RequestId,
                            epoch,
                            parseDetail,
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }

                return await PublishTerminalResultAsync(
                    sink,
                    LocalLlmStreamEventType.Completed,
                    lastSequence,
                    Result(
                        LocalLlmGenerationStatus.Succeeded,
                        request.RequestId,
                        epoch,
                        "Behavior intent validated.",
                        terminal.EventStatus,
                        intent,
                        metricsResult.Metrics)).ConfigureAwait(false);
            }
            catch (OutOfMemoryException exception)
            {
                bool cleaned = generationReleased;
                if (!generationReleased)
                {
                    try
                    {
                        cleaned = terminal == null
                            ? await CancelDrainReleaseAsync(
                                generationHandle).ConfigureAwait(false)
                            : SafeRelease(generationHandle).Succeeded;
                    }
                    catch (Exception)
                    {
                        cleaned = false;
                    }
                }
                MarkFaulted();
                return ResourceExhausted(
                    request.RequestId,
                    epoch,
                    cleaned
                        ? "Local LLM generation exhausted memory; the generation handle was cleaned and the provider was faulted pending explicit recovery: " + exception.Message
                        : "Local LLM generation exhausted memory and cleanup could not be proven complete; the provider was faulted: " + exception.Message);
            }
            catch (Exception exception)
            {
                bool cleaned = generationReleased;
                if (!generationReleased)
                {
                    cleaned = terminal == null
                        ? await CancelDrainReleaseAsync(
                            generationHandle).ConfigureAwait(false)
                        : SafeRelease(generationHandle).Succeeded;
                }
                if (!cleaned)
                {
                    MarkFaulted();
                }
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Unexpected local LLM post-start failure: " + exception.Message);
            }
        }

        private List<LocalLlmRuntimeChatMessage> BuildRuntimeMessages(
            LocalLlmGenerationRequest request)
        {
            List<LocalLlmRuntimeChatMessage> messages =
                new List<LocalLlmRuntimeChatMessage>(request.Messages.Count + 1)
                {
                    new LocalLlmRuntimeChatMessage(
                        "system",
                        LocalLlmBehaviorContract.SystemPrompt),
                };
            for (int index = 0; index < request.Messages.Count; ++index)
            {
                LocalLlmChatMessage message = request.Messages[index];
                string content = message.Content;
                if (index == request.Messages.Count - 1)
                {
                    int finalLength = checked(
                        content.Length + 1 +
                        LocalLlmBehaviorContract.UserPromptSuffix.Length);
                    if (finalLength > profile.MaximumMessageCharacters)
                    {
                        throw new ArgumentException(
                            "The final user message plus the selected model suffix exceeds the configured message limit.",
                            nameof(request));
                    }
                    content = content + "\n" +
                        LocalLlmBehaviorContract.UserPromptSuffix;
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
                if (request.Messages[index].Content.Length >
                    profile.MaximumMessageCharacters)
                {
                    return "The local LLM request contains a message beyond the configured character limit.";
                }
            }
            return null;
        }

        private void ValidateConfiguration()
        {
            LocalLlmBehaviorContract.ValidateFrozenBytes();
            LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
            profile.ValidateAgainst(manifest);
            if (string.IsNullOrWhiteSpace(artifact.FullPath) ||
                artifact.FullPath.Contains('\0') ||
                !Path.IsPathRooted(artifact.FullPath))
            {
                throw new ArgumentException(
                    "The approved local-model artifact path is not an absolute safe path.",
                    nameof(artifact));
            }
        }

        private async Task<bool> CancelDrainReleaseAsync(ulong generationHandle)
        {
            LocalLlmRuntimeCallResult cancel = SafeCancel(generationHandle);
            if (!cancel.Succeeded &&
                cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
            {
                return false;
            }
            return await DrainAndReleaseAsync(generationHandle).ConfigureAwait(false);
        }

        private async Task<bool> DrainAndReleaseAsync(ulong generationHandle)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed <= CancellationDrainTimeout)
            {
                LocalLlmRuntimePollResult poll = SafePoll(generationHandle);
                if (!poll.Succeeded)
                {
                    return false;
                }
                if (poll.Kind == LocalLlmRuntimePollKind.Cancelled ||
                    poll.Kind == LocalLlmRuntimePollKind.Completed ||
                    poll.Kind == LocalLlmRuntimePollKind.Error)
                {
                    return SafeRelease(generationHandle).Succeeded;
                }
                await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
            }
            return false;
        }

        private LocalLlmRuntimePollResult SafePoll(ulong generationHandle)
        {
            try
            {
                return runtime.Poll(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimePollResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM poll threw: " + exception.Message,
                    LocalLlmRuntimePollKind.Error,
                    ReachyLlamaNativeContract.StatusInternalError,
                    0UL,
                    string.Empty);
            }
        }

        private LocalLlmRuntimeCallResult SafeCancel(ulong generationHandle)
        {
            try
            {
                return runtime.Cancel(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeCallResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM cancel threw: " + exception.Message);
            }
        }

        private LocalLlmRuntimeMetricsResult SafeGetMetrics(ulong generationHandle)
        {
            try
            {
                return runtime.GetGenerationMetrics(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeMetricsResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM metrics collection threw: " + exception.Message,
                    null);
            }
        }

        private LocalLlmRuntimeCallResult SafeRelease(ulong generationHandle)
        {
            try
            {
                return runtime.Release(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeCallResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM generation release threw: " + exception.Message);
            }
        }

        private static async Task<LocalLlmGenerationResult> PublishTerminalResultAsync(
            ILocalLlmStreamSink sink,
            LocalLlmStreamEventType type,
            ulong sequence,
            LocalLlmGenerationResult baseResult)
        {
            try
            {
                await sink.OnEventAsync(
                    new LocalLlmStreamEvent(
                        type,
                        sequence,
                        string.Empty,
                        baseResult.Detail),
                    CancellationToken.None).ConfigureAwait(false);
                return baseResult;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Result(
                    LocalLlmGenerationStatus.ConsumerFailure,
                    baseResult.RequestId,
                    baseResult.ConversationEpoch,
                    "Terminal stream notification failed after " +
                        baseResult.Status + ": " + exception.Message,
                    baseResult.NativeStatus,
                    null,
                    baseResult.Metrics);
            }
        }

        private static LocalLlmProviderCreationResult CreationFailure(
            LocalLlmProviderCreationStatus status,
            string detail,
            int nativeStatus)
        {
            return new LocalLlmProviderCreationResult(
                status,
                detail,
                nativeStatus,
                null);
        }

        private static LocalLlmReloadResult RuntimeReloadFailure(
            string detail,
            int nativeStatus)
        {
            return new LocalLlmReloadResult(
                LocalLlmReloadStatus.RuntimeFailure,
                detail,
                nativeStatus);
        }

    }
}
