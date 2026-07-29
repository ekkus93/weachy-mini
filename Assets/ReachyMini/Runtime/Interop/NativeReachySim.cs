using System;
using System.Runtime.InteropServices;

namespace ReachyMini.Interop
{
    internal static class NativeReachySim
    {
        private const string LibraryName = "reachy_sim";

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_abi_version",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_version_string",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr VersionString();

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_status_string",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr StatusString(int status);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_status_recoverability",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint StatusRecoverability(int status);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_default_config",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeReachySimConfig DefaultConfig();

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_get_capabilities",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetCapabilities(
            ref NativeReachySimCapabilities capabilities);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_get_handle_capabilities",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetHandleCapabilities(
            ulong handle,
            ref NativeReachySimCapabilities capabilities);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_create",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Create(
            [In] byte[] modelBytes,
            UIntPtr modelSize,
            in NativeReachySimConfig config,
            out ulong handle,
            ref NativeReachySimErrorInfo error);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_destroy",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Destroy(ulong handle);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_reset",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Reset(ulong handle, uint resetId);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_step",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Step(ulong handle, uint stepCount);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_submit_commands",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SubmitCommands(
            ulong handle,
            IntPtr bytes,
            UIntPtr byteCount);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_copy_state",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int CopyState(
            ulong handle,
            IntPtr bytes,
            UIntPtr byteCapacity,
            out UIntPtr requiredSize);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_apply_wrench",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ApplyWrench(
            ulong handle,
            in NativeReachySimWrenchCommand command);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_copy_snapshot",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int CopySnapshot(
            ulong handle,
            IntPtr bytes,
            UIntPtr byteCapacity,
            out UIntPtr requiredSize);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_restore_snapshot",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int RestoreSnapshot(
            ulong handle,
            IntPtr bytes,
            UIntPtr byteCount);

        [DllImport(
            LibraryName,
            EntryPoint = "reachy_sim_get_last_error",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetLastError(
            ulong handle,
            ref NativeReachySimErrorInfo error);
    }
}
