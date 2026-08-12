#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider
    {
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
    }
}
