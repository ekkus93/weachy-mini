#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ReachyMini.Language;
using ReachyMini.LocalModels;

namespace ReachyMini.Interop
{
    public sealed class ReachyLlamaNativeRuntimeFactory : ILocalLlmRuntimeFactory
    {
        public uint AbiVersion => NativeReachyLlama.AbiVersion();

        public ILocalLlmModelSession LoadModel(
            LocalModelApprovedArtifact artifact,
            LocalModelManifest manifest)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (AbiVersion != NativeReachyLlama.AbiVersionExpected)
            {
                throw new InvalidOperationException(
                    "The installed reachy_llama runtime does not expose ABI 2.");
            }
            if (!File.Exists(artifact.FullPath))
            {
                throw new FileNotFoundException(
                    "The approved local-model artifact no longer exists.");
            }

            using var path = Utf8Allocation.NullTerminated(artifact.FullPath);
            NativeReachyLlamaModelConfig config = NativeReachyLlamaModelConfig.Create();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.ModelLoad(
                path.Pointer,
                in config,
                out ulong model,
                ref error);
            RequireStatus(status, NativeReachyLlama.StatusOk, error, "model load");
            if (model == 0UL)
            {
                throw new InvalidOperationException(
                    "reachy_llama reported successful model loading without a model handle.");
            }
            return new ReachyLlamaNativeModelSession(model);
        }

        internal static void RequireStatus(
            int actual,
            int expected,
            NativeReachyLlamaErrorInfo error,
            string operation)
        {
            if (actual == expected)
            {
                return;
            }
            string detail = DecodeError(error);
            throw new InvalidOperationException(
                $"reachy_llama {operation} failed with status {actual}" +
                (string.IsNullOrEmpty(detail) ? "." : $": {detail}"));
        }

        internal static string DecodeError(NativeReachyLlamaErrorInfo error)
        {
            byte[] message = error.Message ?? Array.Empty<byte>();
            int length = Array.IndexOf(message, (byte)0);
            if (length < 0)
            {
                length = message.Length;
            }
            return length == 0
                ? string.Empty
                : Encoding.UTF8.GetString(message, 0, length);
        }
    }

    internal sealed class ReachyLlamaNativeModelSession : ILocalLlmModelSession
    {
        private ulong model;

        public ReachyLlamaNativeModelSession(ulong model)
        {
            if (model == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(model));
            }
            this.model = model;
        }

        public string RenderChatTemplate(IReadOnlyList<LocalLlmChatMessage> messages)
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }
            if (messages.Count == 0 || messages.Count > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(messages));
            }
            ulong activeModel = RequireModel();
            var allocations = new List<Utf8Allocation>(messages.Count * 2);
            try
            {
                var nativeMessages = new NativeReachyLlamaChatMessage[messages.Count];
                for (int index = 0; index < messages.Count; ++index)
                {
                    LocalLlmChatMessage message = messages[index] ??
                        throw new ArgumentException(
                            "Chat template messages cannot contain null entries.",
                            nameof(messages));
                    Utf8Allocation role = Utf8Allocation.NullTerminated(message.Role);
                    Utf8Allocation content = Utf8Allocation.NullTerminated(message.Content);
                    allocations.Add(role);
                    allocations.Add(content);
                    nativeMessages[index] = new NativeReachyLlamaChatMessage
                    {
                        RoleUtf8 = role.Pointer,
                        ContentUtf8 = content.Pointer,
                    };
                }

                NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
                int status = NativeReachyLlama.ApplyChatTemplate(
                    activeModel,
                    IntPtr.Zero,
                    nativeMessages,
                    checked((UIntPtr)(uint)messages.Count),
                    1U,
                    IntPtr.Zero,
                    UIntPtr.Zero,
                    out UIntPtr required,
                    ref error);
                if (status != NativeReachyLlama.StatusBufferTooSmall || required == UIntPtr.Zero)
                {
                    ReachyLlamaNativeRuntimeFactory.RequireStatus(
                        status,
                        NativeReachyLlama.StatusBufferTooSmall,
                        error,
                        "chat-template sizing");
                    throw new InvalidOperationException(
                        "reachy_llama chat-template sizing returned an empty buffer requirement.");
                }

                int capacity = CheckedSize(required, "chat-template output");
                IntPtr output = Marshal.AllocHGlobal(capacity);
                try
                {
                    error = NativeReachyLlamaErrorInfo.Create();
                    status = NativeReachyLlama.ApplyChatTemplate(
                        activeModel,
                        IntPtr.Zero,
                        nativeMessages,
                        checked((UIntPtr)(uint)messages.Count),
                        1U,
                        output,
                        required,
                        out UIntPtr written,
                        ref error);
                    ReachyLlamaNativeRuntimeFactory.RequireStatus(
                        status,
                        NativeReachyLlama.StatusOk,
                        error,
                        "chat-template render");
                    return ReadUtf8(output, CheckedSize(written, "chat-template output"));
                }
                finally
                {
                    Marshal.FreeHGlobal(output);
                }
            }
            finally
            {
                for (int index = allocations.Count - 1; index >= 0; --index)
                {
                    allocations[index].Dispose();
                }
            }
        }

        public int CountTokens(string prompt)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException(nameof(prompt));
            }
            ulong activeModel = RequireModel();
            using var text = Utf8Allocation.NullTerminated(prompt);
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.Tokenize(
                activeModel,
                text.Pointer,
                0U,
                1U,
                IntPtr.Zero,
                UIntPtr.Zero,
                out UIntPtr required,
                ref error);
            if (status != NativeReachyLlama.StatusBufferTooSmall || required == UIntPtr.Zero)
            {
                ReachyLlamaNativeRuntimeFactory.RequireStatus(
                    status,
                    NativeReachyLlama.StatusBufferTooSmall,
                    error,
                    "tokenize sizing");
                throw new InvalidOperationException(
                    "reachy_llama tokenize sizing returned an empty token requirement.");
            }
            return CheckedSize(required, "token count");
        }

        public ILocalLlmGeneration StartConstrained(
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException(nameof(prompt));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (string.IsNullOrWhiteSpace(grammar))
            {
                throw new ArgumentException("GBNF grammar is required.", nameof(grammar));
            }
            if (string.IsNullOrWhiteSpace(grammarRoot))
            {
                throw new ArgumentException("GBNF grammar root is required.", nameof(grammarRoot));
            }

            ulong activeModel = RequireModel();
            using var promptBytes = Utf8Allocation.NullTerminated(prompt);
            using var grammarBytes = Utf8Allocation.Raw(grammar);
            using var rootBytes = Utf8Allocation.Raw(grammarRoot);
            var config = new NativeReachyLlamaGenerationConfig
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationConfig>()),
                AbiVersion = NativeReachyLlama.AbiVersionExpected,
                ContextTokens = profile.ContextTokens,
                BatchTokens = profile.BatchTokens,
                MicroBatchTokens = profile.MicroBatchTokens,
                MaximumGeneratedTokens = profile.MaximumGeneratedTokens,
                Threads = profile.Threads,
                BatchThreads = profile.BatchThreads,
                Temperature = profile.Temperature,
                MinimumProbability = profile.MinimumProbability,
                Seed = profile.Seed,
                StreamQueueCapacity = profile.StreamQueueCapacity,
            };
            var constraint = new NativeReachyLlamaGenerationConstraint
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationConstraint>()),
                AbiVersion = NativeReachyLlama.AbiVersionExpected,
                Type = NativeReachyLlama.ConstraintGbnf,
                Reserved = 0U,
                GrammarUtf8 = grammarBytes.Pointer,
                GrammarBytes = checked((UIntPtr)(uint)grammarBytes.ByteCount),
                RootUtf8 = rootBytes.Pointer,
                RootBytes = checked((UIntPtr)(uint)rootBytes.ByteCount),
            };
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationStartConstrained(
                activeModel,
                promptBytes.Pointer,
                in config,
                in constraint,
                out ulong generation,
                ref error);
            ReachyLlamaNativeRuntimeFactory.RequireStatus(
                status,
                NativeReachyLlama.StatusOk,
                error,
                "constrained generation start");
            if (generation == 0UL)
            {
                throw new InvalidOperationException(
                    "reachy_llama returned no constrained-generation handle.");
            }
            return new ReachyLlamaNativeGeneration(generation);
        }

        public void Dispose()
        {
            ulong activeModel = model;
            if (activeModel == 0UL)
            {
                return;
            }
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.ModelUnload(activeModel, ref error);
            ReachyLlamaNativeRuntimeFactory.RequireStatus(
                status,
                NativeReachyLlama.StatusOk,
                error,
                "model unload");
            model = 0UL;
        }

        private ulong RequireModel()
        {
            ulong activeModel = model;
            if (activeModel == 0UL)
            {
                throw new ObjectDisposedException(nameof(ReachyLlamaNativeModelSession));
            }
            return activeModel;
        }

        internal static int CheckedSize(UIntPtr value, string name)
        {
            ulong size = value.ToUInt64();
            if (size == 0UL || size > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"reachy_llama {name} is outside the managed buffer bound.");
            }
            return checked((int)size);
        }

        internal static string ReadUtf8(IntPtr pointer, int capacity)
        {
            if (pointer == IntPtr.Zero || capacity <= 0)
            {
                return string.Empty;
            }
            byte[] bytes = new byte[capacity];
            Marshal.Copy(pointer, bytes, 0, capacity);
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
            {
                length = bytes.Length;
            }
            return Encoding.UTF8.GetString(bytes, 0, length);
        }
    }

    internal sealed class ReachyLlamaNativeGeneration : ILocalLlmGeneration
    {
        private const int InitialTextCapacity = 512;
        private ulong generation;

        public ReachyLlamaNativeGeneration(ulong generation)
        {
            if (generation == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
            this.generation = generation;
        }

        public LocalLlmRuntimeEvent Poll()
        {
            ulong active = RequireGeneration();
            NativeReachyLlamaGenerationEvent generationEvent =
                NativeReachyLlamaGenerationEvent.Create();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            IntPtr text = Marshal.AllocHGlobal(InitialTextCapacity);
            try
            {
                int status = NativeReachyLlama.GenerationPoll(
                    active,
                    ref generationEvent,
                    text,
                    checked((UIntPtr)(uint)InitialTextCapacity),
                    out UIntPtr required,
                    ref error);
                if (status == NativeReachyLlama.StatusBufferTooSmall)
                {
                    int requiredCapacity = ReachyLlamaNativeModelSession.CheckedSize(
                        required,
                        "generation event text");
                    Marshal.FreeHGlobal(text);
                    text = Marshal.AllocHGlobal(requiredCapacity);
                    generationEvent = NativeReachyLlamaGenerationEvent.Create();
                    error = NativeReachyLlamaErrorInfo.Create();
                    status = NativeReachyLlama.GenerationPoll(
                        active,
                        ref generationEvent,
                        text,
                        required,
                        out required,
                        ref error);
                }
                ReachyLlamaNativeRuntimeFactory.RequireStatus(
                    status,
                    NativeReachyLlama.StatusOk,
                    error,
                    "generation poll");
                string output = generationEvent.Type == 1U
                    ? ReachyLlamaNativeModelSession.ReadUtf8(
                        text,
                        ReachyLlamaNativeModelSession.CheckedSize(
                            required,
                            "generation event text"))
                    : string.Empty;
                return new LocalLlmRuntimeEvent(
                    (LocalLlmRuntimeEventType)generationEvent.Type,
                    generationEvent.Status,
                    generationEvent.Sequence,
                    output);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        public void Cancel()
        {
            ulong active = RequireGeneration();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationCancel(active, ref error);
            ReachyLlamaNativeRuntimeFactory.RequireStatus(
                status,
                NativeReachyLlama.StatusOk,
                error,
                "generation cancel");
        }

        public LocalLlmGenerationMetrics GetMetrics()
        {
            ulong active = RequireGeneration();
            NativeReachyLlamaGenerationMetrics metrics =
                NativeReachyLlamaGenerationMetrics.Create();
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationGetMetrics(
                active,
                ref metrics,
                ref error);
            ReachyLlamaNativeRuntimeFactory.RequireStatus(
                status,
                NativeReachyLlama.StatusOk,
                error,
                "generation metrics");
            ulong timeToFirst =
                metrics.FirstTextMonotonicMicroseconds >= metrics.StartedMonotonicMicroseconds
                    ? metrics.FirstTextMonotonicMicroseconds - metrics.StartedMonotonicMicroseconds
                    : 0UL;
            ulong decode =
                metrics.FinishedMonotonicMicroseconds >= metrics.FirstTextMonotonicMicroseconds
                    ? metrics.FinishedMonotonicMicroseconds - metrics.FirstTextMonotonicMicroseconds
                    : 0UL;
            return new LocalLlmGenerationMetrics(
                metrics.PromptTokens,
                metrics.GeneratedTokens,
                timeToFirst,
                decode);
        }

        public void Dispose()
        {
            ulong active = generation;
            if (active == 0UL)
            {
                return;
            }
            NativeReachyLlamaErrorInfo error = NativeReachyLlamaErrorInfo.Create();
            int status = NativeReachyLlama.GenerationRelease(active, ref error);
            ReachyLlamaNativeRuntimeFactory.RequireStatus(
                status,
                NativeReachyLlama.StatusOk,
                error,
                "generation release");
            generation = 0UL;
        }

        private ulong RequireGeneration()
        {
            ulong active = generation;
            if (active == 0UL)
            {
                throw new ObjectDisposedException(nameof(ReachyLlamaNativeGeneration));
            }
            return active;
        }
    }

    internal sealed class Utf8Allocation : IDisposable
    {
        private IntPtr pointer;

        private Utf8Allocation(string value, bool nullTerminate)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? throw new ArgumentNullException(nameof(value)));
            ByteCount = bytes.Length;
            int allocationSize = checked(ByteCount + (nullTerminate ? 1 : 0));
            if (allocationSize == 0)
            {
                allocationSize = 1;
            }
            pointer = Marshal.AllocHGlobal(allocationSize);
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            if (nullTerminate)
            {
                Marshal.WriteByte(pointer, bytes.Length, 0);
            }
        }

        public IntPtr Pointer => pointer != IntPtr.Zero
            ? pointer
            : throw new ObjectDisposedException(nameof(Utf8Allocation));

        public int ByteCount { get; }

        public static Utf8Allocation NullTerminated(string value)
        {
            return new Utf8Allocation(value, nullTerminate: true);
        }

        public static Utf8Allocation Raw(string value)
        {
            return new Utf8Allocation(value, nullTerminate: false);
        }

        public void Dispose()
        {
            IntPtr active = pointer;
            if (active == IntPtr.Zero)
            {
                return;
            }
            Marshal.FreeHGlobal(active);
            pointer = IntPtr.Zero;
        }
    }
}
