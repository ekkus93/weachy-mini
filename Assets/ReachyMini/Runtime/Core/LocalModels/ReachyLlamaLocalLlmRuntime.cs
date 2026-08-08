#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    internal sealed class ReachyLlamaLocalLlmRuntime : ILocalLlmRuntime
    {
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public uint GetAbiVersion()
        {
            return NativeReachyLlama.AbiVersion();
        }

        public LocalLlmRuntimeLoadResult LoadModel(
            string fullPath,
            bool checkTensors)
        {
            using var path = new Utf8Buffer(fullPath, nameof(fullPath));
            var config = new NativeReachyLlamaModelConfig
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaModelConfig>()),
                AbiVersion = ReachyLlamaNativeContract.AbiVersion,
                CheckTensors = checkTensors ? 1U : 0U,
                Reserved = 0U,
            };
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.ModelLoad(
                path.Pointer,
                in config,
                out ulong handle,
                ref error);
            return new LocalLlmRuntimeLoadResult(
                status,
                ErrorDetail(status, error),
                status == ReachyLlamaNativeContract.StatusOk ? handle : 0UL);
        }

        public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
        {
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.ModelUnload(modelHandle, ref error);
            return new LocalLlmRuntimeCallResult(status, ErrorDetail(status, error));
        }

        public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
            ulong modelHandle,
            string chatTemplate,
            IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }
            using var template = new Utf8Buffer(chatTemplate, nameof(chatTemplate));
            using var nativeMessages = new NativeChatMessageArray(messages);
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.ApplyChatTemplate(
                modelHandle,
                template.Pointer,
                nativeMessages.Pointer,
                new UIntPtr(checked((uint)messages.Count)),
                1U,
                IntPtr.Zero,
                UIntPtr.Zero,
                out UIntPtr requiredBytes,
                ref error);
            if (status != ReachyLlamaNativeContract.StatusBufferTooSmall &&
                status != ReachyLlamaNativeContract.StatusOk)
            {
                return new LocalLlmRuntimeTemplateResult(
                    status,
                    ErrorDetail(status, error),
                    string.Empty);
            }

            int capacity;
            try
            {
                capacity = RequiredCapacity(requiredBytes, "chat-template output");
            }
            catch (InvalidOperationException exception)
            {
                return new LocalLlmRuntimeTemplateResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    exception.Message,
                    string.Empty);
            }

            IntPtr output = Marshal.AllocHGlobal(capacity);
            try
            {
                error = NativeReachyLlamaErrorInfo.Create();
                status = NativeReachyLlama.ApplyChatTemplate(
                    modelHandle,
                    template.Pointer,
                    nativeMessages.Pointer,
                    new UIntPtr(checked((uint)messages.Count)),
                    1U,
                    output,
                    new UIntPtr(checked((uint)capacity)),
                    out UIntPtr copiedRequiredBytes,
                    ref error);
                if (status != ReachyLlamaNativeContract.StatusOk)
                {
                    return new LocalLlmRuntimeTemplateResult(
                        status,
                        ErrorDetail(status, error),
                        string.Empty);
                }
                int copiedCapacity = RequiredCapacity(
                    copiedRequiredBytes,
                    "copied chat-template output");
                if (copiedCapacity != capacity)
                {
                    return new LocalLlmRuntimeTemplateResult(
                        ReachyLlamaNativeContract.StatusInternalError,
                        "reachy_llama changed the required chat-template capacity between query and copy.",
                        string.Empty);
                }
                string prompt = DecodeNulTerminated(output, capacity, "chat-template output");
                return new LocalLlmRuntimeTemplateResult(
                    ReachyLlamaNativeContract.StatusOk,
                    string.Empty,
                    prompt);
            }
            finally
            {
                Marshal.FreeHGlobal(output);
            }
        }

        public LocalLlmRuntimeTokenCountResult CountTokens(
            ulong modelHandle,
            string prompt)
        {
            using var text = new Utf8Buffer(prompt, nameof(prompt));
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.Tokenize(
                modelHandle,
                text.Pointer,
                1U,
                1U,
                IntPtr.Zero,
                UIntPtr.Zero,
                out UIntPtr requiredTokens,
                ref error);
            if (status != ReachyLlamaNativeContract.StatusBufferTooSmall &&
                status != ReachyLlamaNativeContract.StatusOk)
            {
                return new LocalLlmRuntimeTokenCountResult(
                    status,
                    ErrorDetail(status, error),
                    0);
            }

            ulong count = requiredTokens.ToUInt64();
            if (count > int.MaxValue)
            {
                return new LocalLlmRuntimeTokenCountResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "reachy_llama reported an unsupported prompt token count.",
                    0);
            }
            return new LocalLlmRuntimeTokenCountResult(
                ReachyLlamaNativeContract.StatusOk,
                string.Empty,
                checked((int)count));
        }

        public LocalLlmRuntimeStartResult StartConstrained(
            ulong modelHandle,
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            using var promptBuffer = new Utf8Buffer(prompt, nameof(prompt));
            using var grammarBuffer = new Utf8Buffer(grammar, nameof(grammar));
            using var rootBuffer = new Utf8Buffer(grammarRoot, nameof(grammarRoot));

            var config = new NativeReachyLlamaGenerationConfig
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationConfig>()),
                AbiVersion = ReachyLlamaNativeContract.AbiVersion,
                ContextTokens = checked((uint)profile.ContextTokens),
                BatchTokens = checked((uint)profile.BatchTokens),
                MicroBatchTokens = checked((uint)profile.MicroBatchTokens),
                MaxGeneratedTokens = checked((uint)profile.MaximumGeneratedTokens),
                Threads = profile.Threads,
                BatchThreads = profile.BatchThreads,
                Temperature = profile.Temperature,
                MinP = profile.MinP,
                Seed = profile.Seed,
                StreamQueueCapacity = checked((uint)profile.StreamQueueCapacity),
            };
            var constraint = new NativeReachyLlamaGenerationConstraint
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationConstraint>()),
                AbiVersion = ReachyLlamaNativeContract.AbiVersion,
                Type = ReachyLlamaNativeContract.ConstraintGbnf,
                Reserved = 0U,
                GrammarUtf8 = grammarBuffer.Pointer,
                GrammarBytes = new UIntPtr(checked((uint)grammarBuffer.ByteCount)),
                RootUtf8 = rootBuffer.Pointer,
                RootBytes = new UIntPtr(checked((uint)rootBuffer.ByteCount)),
            };
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationStartConstrained(
                modelHandle,
                promptBuffer.Pointer,
                in config,
                in constraint,
                out ulong generationHandle,
                ref error);
            return new LocalLlmRuntimeStartResult(
                status,
                ErrorDetail(status, error),
                status == ReachyLlamaNativeContract.StatusOk ? generationHandle : 0UL);
        }

        public LocalLlmRuntimePollResult Poll(ulong generationHandle)
        {
            NativeReachyLlamaGenerationEvent generationEvent =
                NativeReachyLlamaGenerationEvent.Create();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationPoll(
                generationHandle,
                ref generationEvent,
                IntPtr.Zero,
                UIntPtr.Zero,
                out UIntPtr requiredBytes,
                ref error);
            if (status == ReachyLlamaNativeContract.StatusBufferTooSmall)
            {
                int capacity;
                try
                {
                    capacity = RequiredCapacity(requiredBytes, "generation text");
                }
                catch (InvalidOperationException exception)
                {
                    return RuntimePollFailure(exception.Message);
                }
                IntPtr output = Marshal.AllocHGlobal(capacity);
                try
                {
                    generationEvent = NativeReachyLlamaGenerationEvent.Create();
                    error = NativeReachyLlamaErrorInfo.Create();
                    status = NativeReachyLlama.GenerationPoll(
                        generationHandle,
                        ref generationEvent,
                        output,
                        new UIntPtr(checked((uint)capacity)),
                        out UIntPtr copiedRequiredBytes,
                        ref error);
                    if (status != ReachyLlamaNativeContract.StatusOk)
                    {
                        return new LocalLlmRuntimePollResult(
                            status,
                            ErrorDetail(status, error),
                            LocalLlmRuntimePollKind.Error,
                            status,
                            0UL,
                            string.Empty);
                    }
                    int copiedCapacity = RequiredCapacity(
                        copiedRequiredBytes,
                        "copied generation text");
                    if (copiedCapacity != capacity)
                    {
                        return RuntimePollFailure(
                            "reachy_llama changed the required generation-text capacity between query and copy.");
                    }
                    if (generationEvent.Type != ReachyLlamaNativeContract.EventText)
                    {
                        return RuntimePollFailure(
                            "reachy_llama returned non-text event metadata for a text-buffer query.");
                    }
                    string text = DecodeNulTerminated(output, capacity, "generation text");
                    return new LocalLlmRuntimePollResult(
                        ReachyLlamaNativeContract.StatusOk,
                        string.Empty,
                        LocalLlmRuntimePollKind.Text,
                        generationEvent.Status,
                        generationEvent.Sequence,
                        text);
                }
                finally
                {
                    Marshal.FreeHGlobal(output);
                }
            }
            if (status != ReachyLlamaNativeContract.StatusOk)
            {
                return new LocalLlmRuntimePollResult(
                    status,
                    ErrorDetail(status, error),
                    LocalLlmRuntimePollKind.Error,
                    status,
                    0UL,
                    string.Empty);
            }

            LocalLlmRuntimePollKind kind;
            switch (generationEvent.Type)
            {
                case ReachyLlamaNativeContract.EventNone:
                    kind = LocalLlmRuntimePollKind.None;
                    break;
                case ReachyLlamaNativeContract.EventCompleted:
                    kind = LocalLlmRuntimePollKind.Completed;
                    break;
                case ReachyLlamaNativeContract.EventCancelled:
                    kind = LocalLlmRuntimePollKind.Cancelled;
                    break;
                case ReachyLlamaNativeContract.EventError:
                    kind = LocalLlmRuntimePollKind.Error;
                    break;
                default:
                    return RuntimePollFailure("reachy_llama returned an unknown generation event type.");
            }
            return new LocalLlmRuntimePollResult(
                ReachyLlamaNativeContract.StatusOk,
                ErrorDetail(generationEvent.Status, error),
                kind,
                generationEvent.Status,
                generationEvent.Sequence,
                string.Empty);
        }

        public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
        {
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationCancel(generationHandle, ref error);
            return new LocalLlmRuntimeCallResult(status, ErrorDetail(status, error));
        }

        public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
        {
            NativeReachyLlamaGenerationMetrics nativeMetrics =
                NativeReachyLlamaGenerationMetrics.Create();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationGetMetrics(
                generationHandle,
                ref nativeMetrics,
                ref error);
            if (status != ReachyLlamaNativeContract.StatusOk)
            {
                return new LocalLlmRuntimeMetricsResult(
                    status,
                    ErrorDetail(status, error),
                    null);
            }
            return new LocalLlmRuntimeMetricsResult(
                ReachyLlamaNativeContract.StatusOk,
                string.Empty,
                new LocalLlmGenerationMetrics(
                    nativeMetrics.PromptTokens,
                    nativeMetrics.GeneratedTokens,
                    nativeMetrics.StartedMonotonicMicroseconds,
                    nativeMetrics.FirstTextMonotonicMicroseconds,
                    nativeMetrics.FinishedMonotonicMicroseconds,
                    checked((int)nativeMetrics.ContextTokens),
                    checked((int)nativeMetrics.BatchTokens),
                    nativeMetrics.Threads,
                    nativeMetrics.BatchThreads));
        }

        public LocalLlmRuntimeCallResult Release(ulong generationHandle)
        {
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationRelease(generationHandle, ref error);
            return new LocalLlmRuntimeCallResult(status, ErrorDetail(status, error));
        }

        public void Dispose()
        {
        }

        private static int RequiredCapacity(UIntPtr requiredBytes, string operation)
        {
            ulong required = requiredBytes.ToUInt64();
            if (required == 0UL || required > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "reachy_llama reported an invalid " + operation + " capacity.");
            }
            return checked((int)required);
        }

        private static string DecodeNulTerminated(
            IntPtr pointer,
            int capacity,
            string operation)
        {
            var bytes = new byte[capacity];
            Marshal.Copy(pointer, bytes, 0, capacity);
            if (bytes[capacity - 1] != 0)
            {
                throw new InvalidOperationException(
                    "reachy_llama returned a non-terminated " + operation + " buffer.");
            }
            try
            {
                return StrictUtf8.GetString(bytes, 0, capacity - 1);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    "reachy_llama returned invalid UTF-8 for " + operation + ".",
                    exception);
            }
        }

        private static string ErrorDetail(
            int status,
            NativeReachyLlamaErrorInfo error)
        {
            byte[]? message = error.Message;
            if (message != null && message.Length > 0)
            {
                int length = Array.IndexOf(message, (byte)0);
                if (length < 0)
                {
                    length = Math.Min(
                        message.Length,
                        ReachyLlamaNativeContract.ErrorMessageCapacity);
                }
                if (length > 0)
                {
                    try
                    {
                        return LocalLlmGenerationResult.BoundDiagnostic(
                            StrictUtf8.GetString(message, 0, length));
                    }
                    catch (DecoderFallbackException)
                    {
                        return "reachy_llama returned an invalid UTF-8 error message.";
                    }
                }
            }
            return status == ReachyLlamaNativeContract.StatusOk
                ? string.Empty
                : "reachy_llama failed with status " + status + ".";
        }

        private static LocalLlmRuntimePollResult RuntimePollFailure(string detail)
        {
            return new LocalLlmRuntimePollResult(
                ReachyLlamaNativeContract.StatusInternalError,
                detail,
                LocalLlmRuntimePollKind.Error,
                ReachyLlamaNativeContract.StatusInternalError,
                0UL,
                string.Empty);
        }

        private sealed class Utf8Buffer : IDisposable
        {
            internal Utf8Buffer(string text, string name)
            {
                if (text == null)
                {
                    throw new ArgumentNullException(name);
                }
                if (text.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException(
                        "Native reachy_llama strings cannot contain embedded NUL characters.",
                        name);
                }
                byte[] bytes = StrictUtf8.GetBytes(text);
                ByteCount = bytes.Length;
                Pointer = Marshal.AllocHGlobal(checked(bytes.Length + 1));
                if (bytes.Length > 0)
                {
                    Marshal.Copy(bytes, 0, Pointer, bytes.Length);
                }
                Marshal.WriteByte(Pointer, bytes.Length, 0);
            }

            internal IntPtr Pointer { get; private set; }

            internal int ByteCount { get; }

            public void Dispose()
            {
                IntPtr pointer = Pointer;
                Pointer = IntPtr.Zero;
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
        }

        private sealed class NativeChatMessageArray : IDisposable
        {
            private readonly List<Utf8Buffer> buffers = new List<Utf8Buffer>();

            internal NativeChatMessageArray(
                IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
            {
                if (messages.Count == 0)
                {
                    Pointer = IntPtr.Zero;
                    return;
                }

                int structSize = Marshal.SizeOf<NativeReachyLlamaChatMessage>();
                Pointer = Marshal.AllocHGlobal(checked(structSize * messages.Count));
                try
                {
                    for (int index = 0; index < messages.Count; ++index)
                    {
                        LocalLlmRuntimeChatMessage message = messages[index] ??
                            throw new ArgumentException(
                                "Native chat messages cannot contain null entries.",
                                nameof(messages));
                        var role = new Utf8Buffer(message.Role, nameof(messages));
                        var content = new Utf8Buffer(message.Content, nameof(messages));
                        buffers.Add(role);
                        buffers.Add(content);
                        var native = new NativeReachyLlamaChatMessage
                        {
                            RoleUtf8 = role.Pointer,
                            ContentUtf8 = content.Pointer,
                        };
                        Marshal.StructureToPtr(
                            native,
                            IntPtr.Add(Pointer, checked(index * structSize)),
                            fDeleteOld: false);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal IntPtr Pointer { get; private set; }

            public void Dispose()
            {
                for (int index = buffers.Count - 1; index >= 0; --index)
                {
                    buffers[index].Dispose();
                }
                buffers.Clear();
                IntPtr pointer = Pointer;
                Pointer = IntPtr.Zero;
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
        }
    }
}
