using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    internal enum NativeReachySimStatus
    {
        Ok = 0,
        InvalidArgument = 1,
        AbiMismatch = 2,
        StructSizeMismatch = 3,
        ModelEmpty = 4,
        ModelTooLarge = 5,
        AllocationFailed = 6,
        ResourceExhausted = 7,
        BackendUnavailable = 8,
        BackendError = 9,
        InvalidHandle = 10,
        StaleHandle = 11,
        HandleBusy = 12,
        BufferTooSmall = 13,
        CommandFormatError = 14,
        SnapshotIncompatible = 15,
        Unsupported = 16,
        NumericFailure = 17,
    }

    internal enum NativeReachySimRecoverability : uint
    {
        None = 0,
        Retry = 1,
        RecreateHandle = 2,
        ReloadModel = 3,
        FatalConfiguration = 4,
    }

    [Flags]
    internal enum NativeReachySimCapabilityFlags : ulong
    {
        None = 0,
        Reset = 1UL << 0,
        Step = 1UL << 1,
        Commands = 1UL << 2,
        State = 1UL << 3,
        Wrench = 1UL << 4,
        Snapshot = 1UL << 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimConfig
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal double TimestepSeconds;
        internal uint MaxCommandCount;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimCapabilities
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal ulong CapabilityFlags;
        internal ulong MaxModelBytes;
        internal ulong MaxCommandBytes;
        internal ulong MaxSnapshotBytes;

        internal static NativeReachySimCapabilities Create()
        {
            return new NativeReachySimCapabilities
            {
                AbiVersion = ProjectMetadata.NativeAbiVersion,
                StructSize = checked((uint)Marshal.SizeOf<NativeReachySimCapabilities>()),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimStateHeader
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal ulong Sequence;
        internal double SimulationTime;
        internal uint BodyCount;
        internal uint JointCount;
        internal uint ActuatorCount;
        internal uint ContactCount;
        internal uint HealthFlags;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimCommandBatchHeader
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal ulong Sequence;
        internal uint CommandCount;
        internal uint ByteCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimWrenchCommand
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal ulong BodyId;
        internal double ForceX;
        internal double ForceY;
        internal double ForceZ;
        internal double TorqueX;
        internal double TorqueY;
        internal double TorqueZ;
        internal double ApplicationPointX;
        internal double ApplicationPointY;
        internal double ApplicationPointZ;
        internal double DurationSeconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimSnapshotHeader
    {
        internal uint AbiVersion;
        internal uint StructSize;
        internal ulong ModelHash;
        internal ulong Sequence;
        internal double SimulationTime;
        internal uint PayloadSize;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct NativeReachySimErrorInfo
    {
        private const int MessageCapacity = 256;

        internal uint AbiVersion;
        internal uint StructSize;
        internal int Status;
        internal uint Recoverability;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MessageCapacity)]
        internal string Message;

        internal static NativeReachySimErrorInfo Create()
        {
            return new NativeReachySimErrorInfo
            {
                AbiVersion = ProjectMetadata.NativeAbiVersion,
                StructSize = checked((uint)Marshal.SizeOf<NativeReachySimErrorInfo>()),
                Message = string.Empty,
            };
        }
    }
}
