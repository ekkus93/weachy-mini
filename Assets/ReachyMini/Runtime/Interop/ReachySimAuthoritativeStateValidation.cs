#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    public static partial class ReachySimAuthoritativeStateParser
    {
        private static void ValidateVariableData(
            IntPtr bytes,
            Envelope envelope)
        {
            for (int index = 0; index < envelope.QposCount; ++index)
            {
                RequireFinite(
                    ReadDouble(bytes, envelope.QposOffset + index * 8),
                    "qpos",
                    index);
            }
            for (int index = 0; index < envelope.QvelCount; ++index)
            {
                RequireFinite(
                    ReadDouble(bytes, envelope.QvelOffset + index * 8),
                    "qvel",
                    index);
            }
            for (int index = 0; index < envelope.ActuatorCount; ++index)
            {
                int offset = checked(
                    envelope.ActuatorOffset +
                    index * ActuatorObservationSize);
                if (ReadUInt32(bytes, offset) != checked((uint)index) ||
                    ReadUInt32(bytes, offset + 4) != 0U)
                {
                    throw new ReachySimStateFormatException(
                        $"Actuator observation {index} is not in canonical order.");
                }
                RequireFinite(ReadDouble(bytes, offset + 8), "actuator control", index);
                RequireFinite(ReadDouble(bytes, offset + 16), "actuator force", index);
                RequireFinite(ReadDouble(bytes, offset + 24), "actuator length", index);
                RequireFinite(ReadDouble(bytes, offset + 32), "actuator velocity", index);
            }
            for (int index = 0; index < envelope.BodyCount; ++index)
            {
                int offset = checked(
                    envelope.BodyOffset + index * BodyPoseSize);
                if (ReadUInt32(bytes, offset) != checked((uint)index + 1U) ||
                    ReadUInt32(bytes, offset + 4) != 0U)
                {
                    throw new ReachySimStateFormatException(
                        $"Body pose {index} is not in canonical non-world-body order.");
                }
                RequireFinite(ReadDouble(bytes, offset + 8), "body position", index);
                RequireFinite(ReadDouble(bytes, offset + 16), "body position", index);
                RequireFinite(ReadDouble(bytes, offset + 24), "body position", index);
                double w = ReadDouble(bytes, offset + 32);
                double x = ReadDouble(bytes, offset + 40);
                double y = ReadDouble(bytes, offset + 48);
                double z = ReadDouble(bytes, offset + 56);
                RequireFinite(w, "body quaternion", index);
                RequireFinite(x, "body quaternion", index);
                RequireFinite(y, "body quaternion", index);
                RequireFinite(z, "body quaternion", index);
                double normSquared = w * w + x * x + y * y + z * z;
                if (!IsFinite(normSquared) || normSquared <= 1.0e-24)
                {
                    throw new ReachySimStateFormatException(
                        $"Body pose {index} has an invalid quaternion.");
                }
            }
        }

        private static uint ReadUInt32(IntPtr bytes, int offset)
        {
            return unchecked((uint)Marshal.ReadInt32(bytes, offset));
        }

        private static ulong ReadUInt64(IntPtr bytes, int offset)
        {
            return unchecked((ulong)Marshal.ReadInt64(bytes, offset));
        }

        private static double ReadDouble(IntPtr bytes, int offset)
        {
            return BitConverter.Int64BitsToDouble(
                Marshal.ReadInt64(bytes, offset));
        }

        private static int CheckedCount(uint count, string name)
        {
            if (count > int.MaxValue)
            {
                throw new ReachySimStateFormatException(
                    $"The native {name} count {count} exceeds the managed range.");
            }
            return checked((int)count);
        }

        private static void RequireFinite(
            double value,
            string field,
            int index)
        {
            if (!IsFinite(value))
            {
                throw new ReachySimStateFormatException(
                    $"Authoritative {field} value {index} is NaN or infinity.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFiniteNonnegative(double value)
        {
            return IsFinite(value) && value >= 0.0;
        }

        private readonly struct Envelope
        {
            internal Envelope(
                ulong sequence,
                double simulationTime,
                uint continuityId,
                uint jointCount,
                uint contactCount,
                uint healthFlags,
                ulong modelHash,
                ulong calibrationProfileId,
                ulong warningCount,
                uint constraintCount,
                uint equalityConstraintCount,
                double maximumConstraintResidual,
                double maximumEqualityConstraintResidual,
                int qposCount,
                int qvelCount,
                int actuatorCount,
                int bodyCount,
                int qposOffset,
                int qvelOffset,
                int actuatorOffset,
                int bodyOffset)
            {
                Sequence = sequence;
                SimulationTime = simulationTime;
                ContinuityId = continuityId;
                JointCount = jointCount;
                ContactCount = contactCount;
                HealthFlags = healthFlags;
                ModelHash = modelHash;
                CalibrationProfileId = calibrationProfileId;
                WarningCount = warningCount;
                ConstraintCount = constraintCount;
                EqualityConstraintCount = equalityConstraintCount;
                MaximumConstraintResidual = maximumConstraintResidual;
                MaximumEqualityConstraintResidual = maximumEqualityConstraintResidual;
                QposCount = qposCount;
                QvelCount = qvelCount;
                ActuatorCount = actuatorCount;
                BodyCount = bodyCount;
                QposOffset = qposOffset;
                QvelOffset = qvelOffset;
                ActuatorOffset = actuatorOffset;
                BodyOffset = bodyOffset;
            }

            internal ulong Sequence { get; }
            internal double SimulationTime { get; }
            internal uint ContinuityId { get; }
            internal uint JointCount { get; }
            internal uint ContactCount { get; }
            internal uint HealthFlags { get; }
            internal ulong ModelHash { get; }
            internal ulong CalibrationProfileId { get; }
            internal ulong WarningCount { get; }
            internal uint ConstraintCount { get; }
            internal uint EqualityConstraintCount { get; }
            internal double MaximumConstraintResidual { get; }
            internal double MaximumEqualityConstraintResidual { get; }
            internal int QposCount { get; }
            internal int QvelCount { get; }
            internal int ActuatorCount { get; }
            internal int BodyCount { get; }
            internal int QposOffset { get; }
            internal int QvelOffset { get; }
            internal int ActuatorOffset { get; }
            internal int BodyOffset { get; }
        }
    }
}
