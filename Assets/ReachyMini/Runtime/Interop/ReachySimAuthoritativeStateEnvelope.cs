#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    public static partial class ReachySimAuthoritativeStateParser
    {
        private static Envelope ReadAndValidateEnvelope(
            IntPtr bytes,
            int byteCount,
            ulong? expectedModelHash)
        {
            if (bytes == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "The native state pointer cannot be zero.",
                    nameof(bytes));
            }
            int minimumSize = LegacyHeaderSize + PayloadHeaderSize;
            if (byteCount < minimumSize)
            {
                throw new ReachySimStateFormatException(
                    $"Authoritative state contains {byteCount} bytes, " +
                    $"smaller than the {minimumSize}-byte envelope.");
            }

            uint abiVersion = ReadUInt32(bytes, 0);
            uint legacySize = ReadUInt32(bytes, 4);
            ulong sequence = ReadUInt64(bytes, 8);
            double simulationTime = ReadDouble(bytes, 16);
            uint bodyCount = ReadUInt32(bytes, 24);
            uint jointCount = ReadUInt32(bytes, 28);
            uint actuatorCount = ReadUInt32(bytes, 32);
            uint contactCount = ReadUInt32(bytes, 36);
            uint healthFlags = ReadUInt32(bytes, 40);
            uint legacyReserved = ReadUInt32(bytes, 44);
            int payloadBase = LegacyHeaderSize;
            uint stateFormatVersion = ReadUInt32(bytes, payloadBase);
            uint payloadSize = ReadUInt32(bytes, payloadBase + 4);
            ulong totalSize = ReadUInt64(bytes, payloadBase + 8);
            ulong modelHash = ReadUInt64(bytes, payloadBase + 16);
            ulong payloadSequence = ReadUInt64(bytes, payloadBase + 24);
            double payloadTime = ReadDouble(bytes, payloadBase + 32);
            uint continuityId = ReadUInt32(bytes, payloadBase + 40);
            uint payloadReserved = ReadUInt32(bytes, payloadBase + 44);
            uint qposCount = ReadUInt32(bytes, payloadBase + 48);
            uint qvelCount = ReadUInt32(bytes, payloadBase + 52);
            uint observationCount = ReadUInt32(bytes, payloadBase + 56);
            uint poseCount = ReadUInt32(bytes, payloadBase + 60);
            ulong qposOffset = ReadUInt64(bytes, payloadBase + 64);
            ulong qvelOffset = ReadUInt64(bytes, payloadBase + 72);
            ulong actuatorOffset = ReadUInt64(bytes, payloadBase + 80);
            ulong bodyOffset = ReadUInt64(bytes, payloadBase + 88);
            ulong calibrationProfileId = ReadUInt64(bytes, payloadBase + 96);
            ulong warningCount = ReadUInt64(bytes, payloadBase + 104);
            uint constraintCount = ReadUInt32(bytes, payloadBase + 112);
            uint equalityConstraintCount = ReadUInt32(
                bytes,
                payloadBase + 116);
            double maximumConstraintResidual = ReadDouble(
                bytes,
                payloadBase + 120);
            double maximumEqualityResidual = ReadDouble(
                bytes,
                payloadBase + 128);

            if (abiVersion != ProjectMetadata.NativeAbiVersion ||
                legacySize != LegacyHeaderSize ||
                stateFormatVersion != ProjectMetadata.NativeStateFormatVersion ||
                payloadSize != PayloadHeaderSize ||
                totalSize != checked((ulong)byteCount) ||
                sequence != payloadSequence ||
                simulationTime != payloadTime ||
                bodyCount != poseCount ||
                actuatorCount != observationCount ||
                continuityId == 0U ||
                legacyReserved != 0U ||
                payloadReserved != 0U)
            {
                throw new ReachySimStateFormatException(
                    "The authoritative state envelope is inconsistent or unsupported.");
            }
            if (expectedModelHash.HasValue &&
                modelHash != expectedModelHash.Value)
            {
                throw new ReachySimStateFormatException(
                    $"Authoritative state model hash {modelHash} differs " +
                    $"from expected hash {expectedModelHash.Value}.");
            }
            if (!IsFiniteNonnegative(simulationTime) ||
                !IsFiniteNonnegative(maximumConstraintResidual) ||
                !IsFiniteNonnegative(maximumEqualityResidual) ||
                equalityConstraintCount > constraintCount)
            {
                throw new ReachySimStateFormatException(
                    "The authoritative state envelope contains invalid numeric diagnostics.");
            }

            int qposCountInt = CheckedCount(qposCount, "qpos");
            int qvelCountInt = CheckedCount(qvelCount, "qvel");
            int actuatorCountInt = CheckedCount(observationCount, "actuator");
            int bodyCountInt = CheckedCount(poseCount, "body");
            ulong expectedQposOffset = checked(
                (ulong)(LegacyHeaderSize + PayloadHeaderSize));
            ulong expectedQvelOffset = checked(
                expectedQposOffset + (ulong)qposCountInt * sizeof(double));
            ulong expectedActuatorOffset = checked(
                expectedQvelOffset + (ulong)qvelCountInt * sizeof(double));
            ulong expectedBodyOffset = checked(
                expectedActuatorOffset +
                (ulong)actuatorCountInt * ActuatorObservationSize);
            ulong expectedTotalSize = checked(
                expectedBodyOffset + (ulong)bodyCountInt * BodyPoseSize);
            if (qposOffset != expectedQposOffset ||
                qvelOffset != expectedQvelOffset ||
                actuatorOffset != expectedActuatorOffset ||
                bodyOffset != expectedBodyOffset ||
                expectedTotalSize != totalSize ||
                bodyOffset > int.MaxValue ||
                actuatorOffset > int.MaxValue ||
                qvelOffset > int.MaxValue ||
                qposOffset > int.MaxValue)
            {
                throw new ReachySimStateFormatException(
                    "The authoritative state array offsets are not the canonical bounded layout.");
            }

            return new Envelope(
                sequence,
                simulationTime,
                continuityId,
                jointCount,
                contactCount,
                healthFlags,
                modelHash,
                calibrationProfileId,
                warningCount,
                constraintCount,
                equalityConstraintCount,
                maximumConstraintResidual,
                maximumEqualityResidual,
                qposCountInt,
                qvelCountInt,
                actuatorCountInt,
                bodyCountInt,
                checked((int)qposOffset),
                checked((int)qvelOffset),
                checked((int)actuatorOffset),
                checked((int)bodyOffset));
        }

    }
}
