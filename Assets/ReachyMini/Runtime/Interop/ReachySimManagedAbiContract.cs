#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    internal static class ReachySimManagedAbiContract
    {
        internal static ReachySimError? ValidateNativeAbi(uint nativeAbiVersion)
        {
            if (nativeAbiVersion == ProjectMetadata.NativeAbiVersion)
            {
                return null;
            }

            return new ReachySimError(
                ReachySimErrorCode.AbiMismatch,
                ReachySimRecoverability.FatalConfiguration,
                $"Managed ABI {ProjectMetadata.NativeAbiVersion} does not match native ABI {nativeAbiVersion}.");
        }

        internal static ReachySimError? ValidateCurrentProcessLayout()
        {
            if (IntPtr.Size != sizeof(ulong))
            {
                return LayoutFailure(
                    $"The Reachy simulation ABI requires a 64-bit process; pointer size is {IntPtr.Size} bytes.");
            }

            ReachySimError? error = ValidateSize<NativeReachySimConfig>(24);
            error ??= ValidateSize<NativeReachySimCapabilities>(40);
            error ??= ValidateSize<NativeReachySimStateHeader>(48);
            error ??= ValidateSize<NativeReachySimCommandBatchHeader>(24);
            error ??= ValidateSize<NativeReachySimWrenchCommand>(96);
            error ??= ValidateSize<NativeReachySimSnapshotHeader>(48);
            error ??= ValidateSize<NativeReachySimErrorInfo>(272);
            error ??= ValidateSize<NativeReachySimStateRequest>(24);
            error ??= ValidateSize<NativeReachySimStatePayloadHeader>(136);
            error ??= ValidateSize<NativeReachySimActuatorObservation>(40);
            error ??= ValidateSize<NativeReachySimBodyPose>(64);
            if (error != null)
            {
                return error;
            }

            error = ValidateOffset<NativeReachySimConfig>(
                nameof(NativeReachySimConfig.TimestepSeconds),
                8);
            error ??= ValidateOffset<NativeReachySimStateHeader>(
                nameof(NativeReachySimStateHeader.SimulationTime),
                16);
            error ??= ValidateOffset<NativeReachySimSnapshotHeader>(
                nameof(NativeReachySimSnapshotHeader.CalibrationProfileId),
                40);
            error ??= ValidateOffset<NativeReachySimErrorInfo>(
                nameof(NativeReachySimErrorInfo.Message),
                16);
            error ??= ValidateOffset<NativeReachySimStatePayloadHeader>(
                nameof(NativeReachySimStatePayloadHeader.QposOffset),
                64);
            error ??= ValidateOffset<NativeReachySimStatePayloadHeader>(
                nameof(NativeReachySimStatePayloadHeader.MaximumConstraintResidual),
                120);
            return error;
        }

        private static ReachySimError? ValidateSize<T>(int expectedSize)
            where T : struct
        {
            int actualSize = Marshal.SizeOf<T>();
            return actualSize == expectedSize
                ? null
                : LayoutFailure(
                    $"Managed {typeof(T).Name} size is {actualSize} bytes; native ABI requires {expectedSize} bytes.");
        }

        private static ReachySimError? ValidateOffset<T>(
            string fieldName,
            int expectedOffset)
            where T : struct
        {
            int actualOffset = checked((int)Marshal.OffsetOf<T>(fieldName));
            return actualOffset == expectedOffset
                ? null
                : LayoutFailure(
                    $"Managed {typeof(T).Name}.{fieldName} offset is {actualOffset}; native ABI requires {expectedOffset}.");
        }

        private static ReachySimError LayoutFailure(string message)
        {
            return new ReachySimError(
                ReachySimErrorCode.ManagedInteropFailure,
                ReachySimRecoverability.FatalConfiguration,
                message);
        }
    }
}
