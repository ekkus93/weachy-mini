#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    public static partial class ReachySimAuthoritativeStateParser
    {
        private const int LegacyHeaderSize = 48;
        private const int PayloadHeaderSize = 136;
        private const int ActuatorObservationSize = 40;
        private const int BodyPoseSize = 64;

        public static ReachySimAuthoritativeStateLayout Inspect(
            IntPtr bytes,
            int byteCount)
        {
            Envelope envelope = ReadAndValidateEnvelope(
                bytes,
                byteCount,
                expectedModelHash: null);
            ValidateVariableData(bytes, envelope);
            return new ReachySimAuthoritativeStateLayout(
                byteCount,
                envelope.ModelHash,
                envelope.QposCount,
                envelope.QvelCount,
                envelope.ActuatorCount,
                envelope.BodyCount);
        }

        public static void Decode(
            IntPtr bytes,
            int byteCount,
            ReachySimAuthoritativeStateFrame destination,
            ulong expectedModelHash)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            Envelope envelope = ReadAndValidateEnvelope(
                bytes,
                byteCount,
                expectedModelHash);
            if (destination.Layout.ByteCount != byteCount ||
                destination.Layout.QposCount != envelope.QposCount ||
                destination.Layout.QvelCount != envelope.QvelCount ||
                destination.Layout.ActuatorObservationCount !=
                    envelope.ActuatorCount ||
                destination.Layout.BodyPoseCount != envelope.BodyCount)
            {
                throw new ReachySimStateFormatException(
                    "The destination frame does not match the native state envelope.");
            }

            ValidateVariableData(bytes, envelope);
            Marshal.Copy(
                IntPtr.Add(bytes, envelope.QposOffset),
                destination.QposStorage,
                0,
                envelope.QposCount);
            Marshal.Copy(
                IntPtr.Add(bytes, envelope.QvelOffset),
                destination.QvelStorage,
                0,
                envelope.QvelCount);

            for (int index = 0; index < envelope.ActuatorCount; ++index)
            {
                int offset = checked(
                    envelope.ActuatorOffset +
                    index * ActuatorObservationSize);
                destination.SetActuatorObservation(
                    index,
                    new ReachySimActuatorObservationSnapshot(
                        ReadUInt32(bytes, offset),
                        ReadDouble(bytes, offset + 8),
                        ReadDouble(bytes, offset + 16),
                        ReadDouble(bytes, offset + 24),
                        ReadDouble(bytes, offset + 32)));
            }

            for (int index = 0; index < envelope.BodyCount; ++index)
            {
                int offset = checked(
                    envelope.BodyOffset + index * BodyPoseSize);
                destination.SetBodyPose(
                    index,
                    new ReachySimBodyPoseSnapshot(
                        ReadUInt32(bytes, offset),
                        ReadDouble(bytes, offset + 8),
                        ReadDouble(bytes, offset + 16),
                        ReadDouble(bytes, offset + 24),
                        ReadDouble(bytes, offset + 32),
                        ReadDouble(bytes, offset + 40),
                        ReadDouble(bytes, offset + 48),
                        ReadDouble(bytes, offset + 56)));
            }

            destination.Sequence = envelope.Sequence;
            destination.SimulationTime = envelope.SimulationTime;
            destination.ContinuityId = envelope.ContinuityId;
            destination.JointCount = envelope.JointCount;
            destination.ContactCount = envelope.ContactCount;
            destination.HealthFlags = envelope.HealthFlags;
            destination.CalibrationProfileId = envelope.CalibrationProfileId;
            destination.WarningCount = envelope.WarningCount;
            destination.ConstraintCount = envelope.ConstraintCount;
            destination.EqualityConstraintCount =
                envelope.EqualityConstraintCount;
            destination.MaximumConstraintResidual =
                envelope.MaximumConstraintResidual;
            destination.MaximumEqualityConstraintResidual =
                envelope.MaximumEqualityConstraintResidual;
        }

    }
}
