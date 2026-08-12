#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Simulation
{
    internal sealed class ReachySimulationBoundedCommandQueue
    {
        internal const int CommandHeaderSize = 24;

        private readonly object gate = new object();
        private readonly byte[][] buffers;
        private readonly int[] lengths;
        private readonly int maximumCommandBytes;
        private int readIndex;
        private int writeIndex;
        private int count;

        internal ReachySimulationBoundedCommandQueue(
            int capacity,
            int maximumCommandBytes)
        {
            buffers = new byte[capacity][];
            lengths = new int[capacity];
            this.maximumCommandBytes = maximumCommandBytes;
            for (int index = 0; index < capacity; ++index)
            {
                buffers[index] = new byte[maximumCommandBytes];
            }
        }

        internal ReachySimulationCommandEnqueueResult Enqueue(
            byte[] commandBatch)
        {
            if (commandBatch.Length > maximumCommandBytes)
            {
                return ReachySimulationCommandEnqueueResult.CommandTooLarge;
            }
            if (!ValidateCommandBatch(commandBatch))
            {
                return ReachySimulationCommandEnqueueResult.InvalidFormat;
            }

            lock (gate)
            {
                if (count == buffers.Length)
                {
                    return ReachySimulationCommandEnqueueResult.QueueFull;
                }

                Buffer.BlockCopy(
                    commandBatch,
                    0,
                    buffers[writeIndex],
                    0,
                    commandBatch.Length);
                lengths[writeIndex] = commandBatch.Length;
                writeIndex = (writeIndex + 1) % buffers.Length;
                ++count;
                return ReachySimulationCommandEnqueueResult.Accepted;
            }
        }

        internal bool TryCopyNext(
            IntPtr destination,
            int destinationCapacity,
            out int byteCount)
        {
            lock (gate)
            {
                if (count == 0)
                {
                    byteCount = 0;
                    return false;
                }

                byteCount = lengths[readIndex];
                if (byteCount > destinationCapacity)
                {
                    throw new InvalidOperationException(
                        $"Queued command size {byteCount} exceeds destination capacity {destinationCapacity}.");
                }

                Marshal.Copy(
                    buffers[readIndex],
                    0,
                    destination,
                    byteCount);
                lengths[readIndex] = 0;
                readIndex = (readIndex + 1) % buffers.Length;
                --count;
                return true;
            }
        }

        internal int Clear()
        {
            lock (gate)
            {
                int discarded = count;
                Array.Clear(lengths, 0, lengths.Length);
                readIndex = 0;
                writeIndex = 0;
                count = 0;
                return discarded;
            }
        }

        private static bool ValidateCommandBatch(byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian ||
                bytes.Length < CommandHeaderSize)
            {
                return false;
            }

            uint abiVersion = BitConverter.ToUInt32(bytes, 0);
            uint structureSize = BitConverter.ToUInt32(bytes, 4);
            uint declaredByteCount = BitConverter.ToUInt32(bytes, 20);
            return abiVersion == ProjectMetadata.NativeAbiVersion &&
                structureSize == CommandHeaderSize &&
                declaredByteCount == checked((uint)bytes.Length);
        }
    }
}
