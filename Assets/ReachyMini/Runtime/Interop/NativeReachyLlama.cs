using System;
using System.Runtime.InteropServices;

namespace ReachyMini.Interop
{
    internal static class NativeReachyLlama
    {
        private const string LibraryName = "reachy_llama";

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_llama_abi_version",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

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
            IntPtr messages,
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
}
