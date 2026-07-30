#nullable enable

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Rendering;
using ReachyMini.Simulation;

namespace ReachyMini.Tests
{
    public sealed partial class ReachySimAuthoritativeStateTests
    {
        private static void AssertMalformed(
            Action<byte[]> mutation,
            string message)
        {
            byte[] bytes = BuildPayload(1UL, 0.0, 1U, 0.0);
            mutation(bytes);
            using (PinnedPayload payload = new PinnedPayload(bytes))
            {
                Assert.That(
                    () => ReachySimAuthoritativeStateParser.Inspect(
                        payload.Pointer,
                        bytes.Length),
                    Throws.InstanceOf<InvalidOperationException>(),
                    message);
            }
        }

        private static byte[] BuildPayload(
            ulong sequence,
            double simulationTime,
            uint continuityId,
            double secondBodyX)
        {
            const uint qposCount = 2U;
            const uint qvelCount = 2U;
            const uint actuatorCount = 1U;
            const uint bodyCount = 2U;
            int qposOffset = LegacyHeaderSize + PayloadHeaderSize;
            int qvelOffset = qposOffset + checked((int)qposCount * sizeof(double));
            int actuatorOffset = qvelOffset + checked((int)qvelCount * sizeof(double));
            int bodyOffset = actuatorOffset + checked((int)actuatorCount * ActuatorSize);
            int totalSize = bodyOffset + checked((int)bodyCount * BodyPoseSize);
            byte[] bytes = new byte[totalSize];

            WriteUInt32(bytes, 0, ProjectMetadata.NativeAbiVersion);
            WriteUInt32(bytes, 4, LegacyHeaderSize);
            WriteUInt64(bytes, 8, sequence);
            WriteDouble(bytes, 16, simulationTime);
            WriteUInt32(bytes, 24, bodyCount);
            WriteUInt32(bytes, 28, 3U);
            WriteUInt32(bytes, 32, actuatorCount);
            WriteUInt32(bytes, 36, 1U);
            WriteUInt32(bytes, 40, 0U);
            WriteUInt32(bytes, 44, 0U);

            int payload = LegacyHeaderSize;
            WriteUInt32(
                bytes,
                payload,
                ProjectMetadata.NativeStateFormatVersion);
            WriteUInt32(bytes, payload + 4, PayloadHeaderSize);
            WriteUInt64(bytes, payload + 8, checked((ulong)totalSize));
            WriteUInt64(bytes, payload + 16, ModelHash);
            WriteUInt64(bytes, payload + 24, sequence);
            WriteDouble(bytes, payload + 32, simulationTime);
            WriteUInt32(bytes, payload + 40, continuityId);
            WriteUInt32(bytes, payload + 44, 0U);
            WriteUInt32(bytes, payload + 48, qposCount);
            WriteUInt32(bytes, payload + 52, qvelCount);
            WriteUInt32(bytes, payload + 56, actuatorCount);
            WriteUInt32(bytes, payload + 60, bodyCount);
            WriteUInt64(bytes, payload + 64, checked((ulong)qposOffset));
            WriteUInt64(bytes, payload + 72, checked((ulong)qvelOffset));
            WriteUInt64(bytes, payload + 80, checked((ulong)actuatorOffset));
            WriteUInt64(bytes, payload + 88, checked((ulong)bodyOffset));
            WriteUInt64(
                bytes,
                payload + 96,
                ProjectMetadata.UncalibratedCalibrationProfileId);
            WriteUInt64(bytes, payload + 104, 0UL);
            WriteUInt32(bytes, payload + 112, 4U);
            WriteUInt32(bytes, payload + 116, 2U);
            WriteDouble(bytes, payload + 120, 0.001);
            WriteDouble(bytes, payload + 128, 0.0005);

            WriteDouble(bytes, qposOffset, 0.1);
            WriteDouble(bytes, qposOffset + 8, -0.2);
            WriteDouble(bytes, qvelOffset, 0.3);
            WriteDouble(bytes, qvelOffset + 8, -0.4);

            WriteUInt32(bytes, actuatorOffset, 0U);
            WriteUInt32(bytes, actuatorOffset + 4, 0U);
            WriteDouble(bytes, actuatorOffset + 8, 0.5);
            WriteDouble(bytes, actuatorOffset + 16, 1.5);
            WriteDouble(bytes, actuatorOffset + 24, 0.6);
            WriteDouble(bytes, actuatorOffset + 32, 0.7);

            WriteBodyPose(bytes, bodyOffset, 1U, 0.0, 0.0, 0.0);
            WriteBodyPose(
                bytes,
                bodyOffset + BodyPoseSize,
                2U,
                secondBodyX,
                0.2,
                0.3);
            return bytes;
        }

        private static int BodyOffset()
        {
            return LegacyHeaderSize + PayloadHeaderSize +
                2 * sizeof(double) +
                2 * sizeof(double) +
                ActuatorSize;
        }

        private static void WriteBodyPose(
            byte[] bytes,
            int offset,
            uint bodyId,
            double x,
            double y,
            double z)
        {
            WriteUInt32(bytes, offset, bodyId);
            WriteUInt32(bytes, offset + 4, 0U);
            WriteDouble(bytes, offset + 8, x);
            WriteDouble(bytes, offset + 16, y);
            WriteDouble(bytes, offset + 24, z);
            WriteDouble(bytes, offset + 32, 1.0);
            WriteDouble(bytes, offset + 40, 0.0);
            WriteDouble(bytes, offset + 48, 0.0);
            WriteDouble(bytes, offset + 56, 0.0);
        }

        private static void WriteUInt32(
            byte[] bytes,
            int offset,
            uint value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            Buffer.BlockCopy(encoded, 0, bytes, offset, encoded.Length);
        }

        private static void WriteUInt64(
            byte[] bytes,
            int offset,
            ulong value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            Buffer.BlockCopy(encoded, 0, bytes, offset, encoded.Length);
        }

        private static void WriteDouble(
            byte[] bytes,
            int offset,
            double value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            Buffer.BlockCopy(encoded, 0, bytes, offset, encoded.Length);
        }

        private sealed class PinnedPayload : IDisposable
        {
            internal PinnedPayload(byte[] bytes)
            {
                Pointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, Pointer, bytes.Length);
            }

            internal IntPtr Pointer { get; private set; }

            public void Dispose()
            {
                if (Pointer == IntPtr.Zero)
                {
                    return;
                }
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        private sealed class FakePublishedStateSource :
            IReachyPublishedAuthoritativeStateSource,
            IDisposable
        {
            private readonly FakeStateReader reader;

            internal FakePublishedStateSource(byte[][] frames)
            {
                reader = new FakeStateReader(frames);
            }

            public ReachySimAuthoritativeStateLayout AuthoritativeStateLayout =>
                reader.Layout;

            public ReachySimAuthoritativeStateFrame CreateAuthoritativeStateFrame()
            {
                return reader.CreateFrame();
            }

            public bool TryCaptureLatestAuthoritativeState(
                ReachySimAuthoritativeStateFrame destination)
            {
                reader.Capture(destination);
                return true;
            }

            public void Dispose()
            {
                reader.Dispose();
            }
        }

        private sealed class FakeStateReader :
            IReachySimAuthoritativeStateReader
        {
            private readonly byte[][] frames;
            private readonly IntPtr buffer;
            private int nextFrame;

            internal FakeStateReader(byte[][] frames)
            {
                this.frames = frames;
                buffer = Marshal.AllocHGlobal(frames[0].Length);
                Marshal.Copy(frames[0], 0, buffer, frames[0].Length);
                Layout = ReachySimAuthoritativeStateParser.Inspect(
                    buffer,
                    frames[0].Length);
            }

            public ReachySimAuthoritativeStateLayout Layout { get; }

            public ReachySimAuthoritativeStateFrame CreateFrame()
            {
                return new ReachySimAuthoritativeStateFrame(Layout);
            }

            public void Capture(ReachySimAuthoritativeStateFrame frame)
            {
                byte[] current = frames[Math.Min(nextFrame, frames.Length - 1)];
                ++nextFrame;
                Marshal.Copy(current, 0, buffer, current.Length);
                ReachySimAuthoritativeStateParser.Decode(
                    buffer,
                    current.Length,
                    frame,
                    ModelHash);
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
