#nullable enable

using System;
using ReachyMini.Core;

namespace ReachyMini.Simulation
{
    public static class ReachySimulationCommandBatch
    {
        private const int HeaderSize = 24;
        private const int CommandSize = 24;

        public static byte[] CreatePositionTargets(
            ulong sequence,
            ReadOnlySpan<double> targetsRadians)
        {
            if (sequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "A command sequence must be nonzero.");
            }
            if (targetsRadians.Length == 0)
            {
                throw new ArgumentException(
                    "At least one actuator target is required.",
                    nameof(targetsRadians));
            }

            int byteCount = checked(
                HeaderSize + targetsRadians.Length * CommandSize);
            byte[] bytes = new byte[byteCount];
            WriteUInt32(bytes, 0, ProjectMetadata.NativeAbiVersion);
            WriteUInt32(bytes, 4, HeaderSize);
            WriteUInt64(bytes, 8, sequence);
            WriteUInt32(bytes, 16, checked((uint)targetsRadians.Length));
            WriteUInt32(bytes, 20, checked((uint)byteCount));

            for (int index = 0; index < targetsRadians.Length; ++index)
            {
                double value = targetsRadians[index];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentException(
                        $"Actuator target {index} is not finite.",
                        nameof(targetsRadians));
                }

                int offset = checked(HeaderSize + index * CommandSize);
                WriteUInt32(bytes, offset, ProjectMetadata.NativeAbiVersion);
                WriteUInt32(bytes, offset + 4, CommandSize);
                WriteUInt32(bytes, offset + 8, checked((uint)index));
                WriteUInt32(bytes, offset + 12, 0U);
                WriteUInt64(
                    bytes,
                    offset + 16,
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
            }
            return bytes;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = unchecked((byte)value);
            bytes[offset + 1] = unchecked((byte)(value >> 8));
            bytes[offset + 2] = unchecked((byte)(value >> 16));
            bytes[offset + 3] = unchecked((byte)(value >> 24));
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (int index = 0; index < sizeof(ulong); ++index)
            {
                bytes[offset + index] = unchecked((byte)(value >> (index * 8)));
            }
        }
    }
}
