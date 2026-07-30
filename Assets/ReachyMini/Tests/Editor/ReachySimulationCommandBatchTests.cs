#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.Core;
using ReachyMini.Simulation;

namespace ReachyMini.Tests
{
    public sealed class ReachySimulationCommandBatchTests
    {
        [Test]
        public void PositionTargetsMatchNativeCommandLayout()
        {
            byte[] bytes = ReachySimulationCommandBatch.CreatePositionTargets(
                7UL,
                new[] { 0.25, -0.5 });

            Assert.That(bytes, Has.Length.EqualTo(72));
            Assert.That(ReadUInt32(bytes, 0), Is.EqualTo(ProjectMetadata.NativeAbiVersion));
            Assert.That(ReadUInt32(bytes, 4), Is.EqualTo(24U));
            Assert.That(ReadUInt64(bytes, 8), Is.EqualTo(7UL));
            Assert.That(ReadUInt32(bytes, 16), Is.EqualTo(2U));
            Assert.That(ReadUInt32(bytes, 20), Is.EqualTo(72U));

            AssertCommand(bytes, 24, 0U, 0.25);
            AssertCommand(bytes, 48, 1U, -0.5);
        }

        [Test]
        public void PositionTargetsRejectInvalidInputs()
        {
            Assert.That(
                () => ReachySimulationCommandBatch.CreatePositionTargets(
                    0UL,
                    new[] { 0.0 }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ReachySimulationCommandBatch.CreatePositionTargets(
                    1UL,
                    Array.Empty<double>()),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => ReachySimulationCommandBatch.CreatePositionTargets(
                    1UL,
                    new[] { double.NaN }),
                Throws.InstanceOf<ArgumentException>());
        }

        private static void AssertCommand(
            byte[] bytes,
            int offset,
            uint expectedActuatorId,
            double expectedValue)
        {
            Assert.That(
                ReadUInt32(bytes, offset),
                Is.EqualTo(ProjectMetadata.NativeAbiVersion));
            Assert.That(ReadUInt32(bytes, offset + 4), Is.EqualTo(24U));
            Assert.That(ReadUInt32(bytes, offset + 8), Is.EqualTo(expectedActuatorId));
            Assert.That(ReadUInt32(bytes, offset + 12), Is.EqualTo(0U));
            Assert.That(ReadDouble(bytes, offset + 16), Is.EqualTo(expectedValue));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(
                bytes[offset] |
                bytes[offset + 1] << 8 |
                bytes[offset + 2] << 16 |
                bytes[offset + 3] << 24);
        }

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (int index = 0; index < sizeof(ulong); ++index)
            {
                value |= (ulong)bytes[offset + index] << (index * 8);
            }
            return value;
        }

        private static double ReadDouble(byte[] bytes, int offset)
        {
            return BitConverter.Int64BitsToDouble(
                unchecked((long)ReadUInt64(bytes, offset)));
        }
    }
}
