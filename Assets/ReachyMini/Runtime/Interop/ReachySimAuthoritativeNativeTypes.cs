#nullable enable

using System.Runtime.InteropServices;

namespace ReachyMini.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimStateRequest
    {
        internal ulong Magic;
        internal uint AbiVersion;
        internal uint StructSize;
        internal uint StateFormatVersion;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimStatePayloadHeader
    {
        internal uint StateFormatVersion;
        internal uint StructSize;
        internal ulong TotalSize;
        internal ulong ModelHash;
        internal ulong Sequence;
        internal double SimulationTime;
        internal uint ContinuityId;
        internal uint Reserved;
        internal uint QposCount;
        internal uint QvelCount;
        internal uint ActuatorObservationCount;
        internal uint BodyPoseCount;
        internal ulong QposOffset;
        internal ulong QvelOffset;
        internal ulong ActuatorObservationOffset;
        internal ulong BodyPoseOffset;
        internal ulong CalibrationProfileId;
        internal ulong WarningCount;
        internal uint ConstraintCount;
        internal uint EqualityConstraintCount;
        internal double MaximumConstraintResidual;
        internal double MaximumEqualityConstraintResidual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimActuatorObservation
    {
        internal uint ActuatorId;
        internal uint Reserved;
        internal double ControlValue;
        internal double ActuatorForce;
        internal double Length;
        internal double Velocity;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeReachySimBodyPose
    {
        internal uint BodyId;
        internal uint Reserved;
        internal double PositionX;
        internal double PositionY;
        internal double PositionZ;
        internal double QuaternionW;
        internal double QuaternionX;
        internal double QuaternionY;
        internal double QuaternionZ;
    }
}
