#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma032NativeTestControls
    {
        private const string Library = "reachy_sim";

        [LibraryImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_reset_controls")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial void ResetControlsNative();

        [LibraryImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_set_step_blocked")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial void SetStepBlockedNative(byte blocked);

        [LibraryImport(
            Library,
            EntryPoint = "reachy_sim_blocking_backend_step_entered")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial byte StepEnteredNative();

        internal static void ResetControls()
        {
            ResetControlsNative();
        }

        internal static void SetStepBlocked(bool blocked)
        {
            SetStepBlockedNative(blocked ? (byte)1 : (byte)0);
        }

        internal static bool StepEntered()
        {
            return StepEnteredNative() != 0;
        }
    }
}
