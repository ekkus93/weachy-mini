using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static ReachyPublishedSimulationSnapshot WaitForSnapshot(
            ReachySimulationWorker worker,
            Func<ReachyPublishedSimulationSnapshot, bool> predicate,
            TimeSpan timeout,
            string description)
        {
            long deadline = Stopwatch.GetTimestamp() + checked(
                (long)Math.Ceiling(
                    timeout.TotalSeconds * Stopwatch.Frequency));
            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (worker.TryGetLatestSnapshot(
                        out ReachyPublishedSimulationSnapshot snapshot) &&
                    predicate(snapshot))
                {
                    return snapshot;
                }

                ReachySimulationFault? fault = worker.Fault;
                if (fault != null)
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: worker faulted in {fault.Operation}: {fault.Error.Code}: {fault.Error.Message}");
                }

                Thread.Sleep(1);
            }

            throw new InvalidOperationException(
                $"Managed test timed out waiting for {description}.");
        }

        private static void AssertTrajectoryInvariant(
            ReachyPublishedSimulationSnapshot snapshot,
            string description)
        {
            double expectedSimulationTime = snapshot.State.Sequence *
                ProjectMetadata.InitialPhysicsTimestepSeconds;
            double error = Math.Abs(
                snapshot.State.SimulationTime - expectedSimulationTime);
            if (error > 1.0e-9)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: sequence {snapshot.State.Sequence} implies time {expectedSimulationTime}, actual {snapshot.State.SimulationTime}.");
            }
        }

        private static byte[] CreateCommandBatch(ulong sequence)
        {
            byte[] bytes = new byte[24];
            WriteUInt32(bytes, 0, ProjectMetadata.NativeAbiVersion);
            WriteUInt32(bytes, 4, 24U);
            WriteUInt64(bytes, 8, sequence);
            WriteUInt32(bytes, 16, 0U);
            WriteUInt32(bytes, 20, checked((uint)bytes.Length));
            return bytes;
        }

        private static void WriteUInt32(
            byte[] destination,
            int offset,
            uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(
            byte[] destination,
            int offset,
            ulong value)
        {
            WriteUInt32(destination, offset, (uint)value);
            WriteUInt32(destination, offset + 4, (uint)(value >> 32));
        }

        private static void AssertBytesEqual(
            byte[] expected,
            byte[] actual,
            string description)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected.Length} bytes, actual {actual.Length}.");
            }

            for (int index = 0; index < expected.Length; ++index)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(
                        $"Managed test failed for {description}: byte {index} differs.");
                }
            }
        }

        private static void AssertControlSuccess(
            ReachySimulationControlResult result,
            string description)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: {result.Error.Code}: {result.Error.Message}");
            }
        }

        private static void AssertEqual<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }

        private static void AssertThrows<TException>(
            Action action,
            string description)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Managed test failed for {description}: expected {typeof(TException).Name}.");
        }
    }
}
