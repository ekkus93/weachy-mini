using System;
using Microsoft.Win32.SafeHandles;

namespace ReachyMini.Interop
{
    internal sealed class ReachySimSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ReachySimSafeHandle()
            : base(ownsHandle: true)
        {
        }

        internal ulong Token
        {
            get
            {
                return unchecked((ulong)handle.ToInt64());
            }
        }

        internal static ReachySimSafeHandle FromToken(ulong token)
        {
            if (IntPtr.Size != sizeof(ulong))
            {
                throw new PlatformNotSupportedException(
                    "The Reachy simulation ABI requires a 64-bit process.");
            }
            if (token == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(token),
                    "A native simulator handle cannot be zero.");
            }

            ReachySimSafeHandle safeHandle = new ReachySimSafeHandle();
            safeHandle.SetHandle(new IntPtr(unchecked((long)token)));
            return safeHandle;
        }

        internal int CloseExplicitly()
        {
            if (IsClosed || IsInvalid)
            {
                return (int)NativeReachySimStatus.Ok;
            }

            int status = NativeReachySim.Destroy(Token);
            if (status == (int)NativeReachySimStatus.Ok)
            {
                SetHandleAsInvalid();
            }
            return status;
        }

        protected override bool ReleaseHandle()
        {
            return NativeReachySim.Destroy(Token) ==
                (int)NativeReachySimStatus.Ok;
        }
    }
}
