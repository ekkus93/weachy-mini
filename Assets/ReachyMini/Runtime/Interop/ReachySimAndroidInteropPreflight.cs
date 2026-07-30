#nullable enable

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace ReachyMini.Interop
{
    internal static class ReachySimAndroidInteropPreflight
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ValidateBeforeSceneLoad()
        {
            ReachySimError? layoutError =
                ReachySimManagedAbiContract.ValidateCurrentProcessLayout();
            if (layoutError != null)
            {
                throw new InvalidOperationException(
                    $"Reachy managed/native layout validation failed: {layoutError.Message}");
            }

            uint nativeAbiVersion;
            try
            {
                nativeAbiVersion = NativeReachySim.AbiVersion();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Reachy native ABI preflight could not load or query libreachy_sim.",
                    exception);
            }

            ReachySimError? abiError =
                ReachySimManagedAbiContract.ValidateNativeAbi(nativeAbiVersion);
            if (abiError != null)
            {
                throw new InvalidOperationException(abiError.Message);
            }
        }
    }
}
#endif
