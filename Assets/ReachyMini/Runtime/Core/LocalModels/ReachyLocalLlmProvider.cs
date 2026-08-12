#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
