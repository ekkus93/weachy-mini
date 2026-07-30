#nullable enable

using System.Runtime.InteropServices;

namespace ReachyMini.Core.Tests
{
    internal static class Rma032NativeTestControls
    {
        private const string Library = "reachy_sim";

        [DllImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_reset_controls",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        private static extern void ResetControlsNative();

        [DllImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_set_step_blocked",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        private static extern void SetStepBlockedNative(
            [MarshalAs(UnmanagedType.I1)] bool blocked);

        [DllImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_step_entered",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool StepEnteredNative();

        internal static void ResetControls()
        {
            ResetControlsNative();
        }

        internal static void SetStepBlocked(bool blocked)
        {
            SetStepBlockedNative(blocked);
        }

        internal static bool StepEntered()
        {
            return StepEnteredNative();
        }
    }
}
