#nullable enable

using System;
using System.Runtime.InteropServices;

namespace ReachyMini.Interop
{
    internal static class NativeReachyLlama
    {
        internal const uint AbiVersionExpected = 2U;
        internal const int ErrorMessageCapacity = 384;
        internal const int StatusOk = 0;
        internal const int StatusBufferTooSmall = 5;
        internal const uint ConstraintGbnf = 1U;

        private const string LibraryName = "reachy_llama";

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_abi_version",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_status_string",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr StatusString(int status);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_model_load",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ModelLoad(
            IntPtr modelPathUtf8,
            in NativeReachyLlamaModelConfig config,
            out ulong model,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_model_unload",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ModelUnload(
            ulong model,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_apply_chat_template",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ApplyChatTemplate(
            ulong model,
            IntPtr templateUtf8,
            [In] NativeReachyLlamaChatMessage[] messages,
            UIntPtr messageCount,
            uint addAssistant,
            IntPtr outputUtf8,
            UIntPtr outputCapacity,
            out UIntPtr requiredBytes,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_tokenize",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Tokenize(
            ulong model,
            IntPtr textUtf8,
            uint addSpecial,
            uint parseSpecial,
            IntPtr tokens,
            UIntPtr tokenCapacity,
            out UIntPtr requiredTokens,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_generation_start_constrained",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerationStartConstrained(
            ulong model,
            IntPtr promptUtf8,
            in NativeReachyLlamaGenerationConfig config,
            in NativeReachyLlamaGenerationConstraint constraint,
            out ulong generation,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_generation_poll",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerationPoll(
            ulong generation,
            ref NativeReachyLlamaGenerationEvent generationEvent,
            IntPtr textUtf8,
            UIntPtr textCapacity,
            out UIntPtr requiredBytes,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_generation_cancel",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerationCancel(
            ulong generation,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_generation_get_metrics",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerationGetMetrics(
            ulong generation,
            ref NativeReachyLlamaGenerationMetrics metrics,
            ref NativeReachyLlamaErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_generation_release",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerationRelease(
            ulong generation,
            ref NativeReachyLlamaErrorInfo error);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaErrorInfo
    {
        internal uint StructSize;
        internal int Status;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeReachyLlama.ErrorMessageCapacity)]
        internal byte[] Message;

        internal static NativeReachyLlamaErrorInfo Create()
        {
            return new NativeReachyLlamaErrorInfo
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaErrorInfo>()),
                Status = 0,
                Message = new byte[NativeReachyLlama.ErrorMessageCapacity],
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaModelConfig
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint CheckTensors;
        internal uint Reserved;

        internal static NativeReachyLlamaModelConfig Create()
        {
            return new NativeReachyLlamaModelConfig
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaModelConfig>()),
                AbiVersion = NativeReachyLlama.AbiVersionExpected,
                CheckTensors = 1U,
                Reserved = 0U,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaGenerationConfig
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint ContextTokens;
        internal uint BatchTokens;
        internal uint MicroBatchTokens;
        internal uint MaximumGeneratedTokens;
        internal int Threads;
        internal int BatchThreads;
        internal float Temperature;
        internal float MinimumProbability;
        internal uint Seed;
        internal uint StreamQueueCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaGenerationConstraint
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint Type;
        internal uint Reserved;
        internal IntPtr GrammarUtf8;
        internal UIntPtr GrammarBytes;
        internal IntPtr RootUtf8;
        internal UIntPtr RootBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaChatMessage
    {
        internal IntPtr RoleUtf8;
        internal IntPtr ContentUtf8;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaGenerationEvent
    {
        internal uint StructSize;
        internal uint Type;
        internal int Status;
        internal uint Reserved;
        internal ulong Sequence;

        internal static NativeReachyLlamaGenerationEvent Create()
        {
            return new NativeReachyLlamaGenerationEvent
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationEvent>()),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaGenerationMetrics
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint State;
        internal uint QueueDepth;
        internal ulong PromptTokens;
        internal ulong GeneratedTokens;
        internal ulong StartedMonotonicMicroseconds;
        internal ulong FirstTextMonotonicMicroseconds;
        internal ulong FinishedMonotonicMicroseconds;
        internal uint ContextTokens;
        internal uint BatchTokens;
        internal int Threads;
        internal int BatchThreads;

        internal static NativeReachyLlamaGenerationMetrics Create()
        {
            return new NativeReachyLlamaGenerationMetrics
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaGenerationMetrics>()),
                AbiVersion = NativeReachyLlama.AbiVersionExpected,
            };
        }
    }
}
