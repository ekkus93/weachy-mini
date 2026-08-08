#nullable enable

using System;
using System.Runtime.InteropServices;

namespace ReachyMini.Interop
{
    internal static class ReachyLlamaNativeContract
    {
        internal const uint AbiVersion = 2U;
        internal const int ErrorMessageCapacity = 384;
        internal const int StatusOk = 0;
        internal const int StatusInvalidArgument = 1;
        internal const int StatusAbiMismatch = 2;
        internal const int StatusNotFound = 3;
        internal const int StatusBusy = 4;
        internal const int StatusBufferTooSmall = 5;
        internal const int StatusModelLoadFailed = 6;
        internal const int StatusTokenizeFailed = 7;
        internal const int StatusTemplateFailed = 8;
        internal const int StatusContextCreateFailed = 9;
        internal const int StatusContextLimit = 10;
        internal const int StatusDecodeFailed = 11;
        internal const int StatusUnsupportedModel = 12;
        internal const int StatusCancelled = 13;
        internal const int StatusInternalError = 14;
        internal const int StatusInvalidConstraint = 15;
        internal const int StatusConstraintInitFailed = 16;

        internal const uint ConstraintGbnf = 1U;

        internal const uint EventNone = 0U;
        internal const uint EventText = 1U;
        internal const uint EventCompleted = 2U;
        internal const uint EventCancelled = 3U;
        internal const uint EventError = 4U;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaErrorInfo
    {
        internal uint StructSize;
        internal int Status;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = ReachyLlamaNativeContract.ErrorMessageCapacity,
            ArraySubType = UnmanagedType.I1)]
        internal byte[] Message;

        internal static NativeReachyLlamaErrorInfo Create()
        {
            return new NativeReachyLlamaErrorInfo
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeReachyLlamaErrorInfo>()),
                Status = ReachyLlamaNativeContract.StatusOk,
                Message = new byte[ReachyLlamaNativeContract.ErrorMessageCapacity],
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
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachyLlamaGenerationConfig
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint ContextTokens;
        internal uint BatchTokens;
        internal uint MicroBatchTokens;
        internal uint MaxGeneratedTokens;
        internal int Threads;
        internal int BatchThreads;
        internal float Temperature;
        internal float MinP;
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
                AbiVersion = ReachyLlamaNativeContract.AbiVersion,
            };
        }
    }
}
