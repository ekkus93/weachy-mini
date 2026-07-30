#nullable enable

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Rendering;

namespace ReachyMini.Tests
{
    public sealed partial class ReachySimAuthoritativeStateTests
    {
        private const ulong ModelHash = 0x123456789abcdef0UL;
        private const int LegacyHeaderSize = 48;
        private const int PayloadHeaderSize = 136;
        private const int ActuatorSize = 40;
        private const int BodyPoseSize = 64;

        [TestCase(typeof(NativeReachySimStateRequest), 24)]
        [TestCase(typeof(NativeReachySimStatePayloadHeader), 136)]
        [TestCase(typeof(NativeReachySimActuatorObservation), 40)]
        [TestCase(typeof(NativeReachySimBodyPose), 64)]
        public void AuthoritativeStateLayoutsMatchNative(
            Type structureType,
            int expectedSize)
        {
            Assert.That(Marshal.SizeOf(structureType), Is.EqualTo(expectedSize));
        }

        [Test]
        public void ParserDecodesCanonicalPayload()
        {
            byte[] bytes = BuildPayload(
                sequence: 7UL,
                simulationTime: 0.014,
                continuityId: 3U,
                secondBodyX: 0.25);
            using (PinnedPayload payload = new PinnedPayload(bytes))
            {
                ReachySimAuthoritativeStateLayout layout =
                    ReachySimAuthoritativeStateParser.Inspect(
                        payload.Pointer,
                        bytes.Length);
                ReachySimAuthoritativeStateFrame frame =
                    new ReachySimAuthoritativeStateFrame(layout);
                ReachySimAuthoritativeStateParser.Decode(
                    payload.Pointer,
                    bytes.Length,
                    frame,
                    ModelHash);

                Assert.That(layout.ModelHash, Is.EqualTo(ModelHash));
                Assert.That(frame.Sequence, Is.EqualTo(7UL));
                Assert.That(frame.SimulationTime, Is.EqualTo(0.014));
                Assert.That(frame.ContinuityId, Is.EqualTo(3U));
                Assert.That(frame.QposCount, Is.EqualTo(2));
                Assert.That(frame.GetQpos(1), Is.EqualTo(-0.2));
                Assert.That(frame.GetQvel(0), Is.EqualTo(0.3));
                Assert.That(
                    frame.GetActuatorObservation(0).ActuatorForce,
                    Is.EqualTo(1.5));
                Assert.That(frame.GetBodyPose(0).BodyId, Is.EqualTo(1U));
                Assert.That(
                    frame.GetBodyPose(1).PositionX,
                    Is.EqualTo(0.25));
                Assert.That(frame.ConstraintCount, Is.EqualTo(4U));
                Assert.That(frame.EqualityConstraintCount, Is.EqualTo(2U));
            }
        }

        [Test]
        public void ParserRejectsMalformedPayloads()
        {
            AssertMalformed(
                bytes => WriteUInt64(bytes, LegacyHeaderSize + 8, 1UL),
                "wrong total size");
            AssertMalformed(
                bytes => WriteDouble(bytes, LegacyHeaderSize + PayloadHeaderSize, double.NaN),
                "non-finite qpos");
            AssertMalformed(
                bytes =>
                {
                    int bodyOffset = BodyOffset();
                    WriteUInt32(bytes, bodyOffset + BodyPoseSize, 1U);
                },
                "duplicate body identifier");

            byte[] valid = BuildPayload(1UL, 0.0, 1U, 0.0);
            using (PinnedPayload payload = new PinnedPayload(valid))
            {
                ReachySimAuthoritativeStateLayout layout =
                    ReachySimAuthoritativeStateParser.Inspect(
                        payload.Pointer,
                        valid.Length);
                ReachySimAuthoritativeStateFrame frame =
                    new ReachySimAuthoritativeStateFrame(layout);
                Assert.That(
                    () => ReachySimAuthoritativeStateParser.Decode(
                        payload.Pointer,
                        valid.Length,
                        frame,
                        ModelHash ^ 1UL),
                    Throws.InstanceOf<InvalidOperationException>(),
                    "wrong model hash");
            }
        }

        [Test]
        public void ParserReusesFrameWithoutManagedAllocation()
        {
            byte[] bytes = BuildPayload(2UL, 0.004, 1U, 0.1);
            using (PinnedPayload payload = new PinnedPayload(bytes))
            {
                ReachySimAuthoritativeStateLayout layout =
                    ReachySimAuthoritativeStateParser.Inspect(
                        payload.Pointer,
                        bytes.Length);
                ReachySimAuthoritativeStateFrame frame =
                    new ReachySimAuthoritativeStateFrame(layout);
                ReachySimAuthoritativeStateParser.Decode(
                    payload.Pointer,
                    bytes.Length,
                    frame,
                    ModelHash);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 100; ++iteration)
                {
                    ReachySimAuthoritativeStateParser.Decode(
                        payload.Pointer,
                        bytes.Length,
                        frame,
                        ModelHash);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(after - before, Is.EqualTo(0L));
            }
        }

        [Test]
        public void PoseSourcePublishesOrderedPairsAndDiscontinuities()
        {
            byte[][] frames =
            {
                BuildPayload(1UL, 0.002, 1U, 0.1),
                BuildPayload(2UL, 0.004, 1U, 0.2),
                BuildPayload(0UL, 0.0, 2U, 0.3),
            };
            using (FakeStateReader reader = new FakeStateReader(frames))
            using (ReachySimAuthoritativePoseSource source =
                new ReachySimAuthoritativePoseSource(
                    reader,
                    new[] { "base", "head" }))
            {
                Assert.That(
                    source.TryGetLatestPair(out _, out _),
                    Is.False);
                Assert.That(
                    source.TryGetLatestPair(
                        out ReachyAuthoritativePoseSnapshot first,
                        out ReachyAuthoritativePoseSnapshot second),
                    Is.True);
                Assert.That(first.Sequence, Is.EqualTo(1UL));
                Assert.That(second.Sequence, Is.EqualTo(2UL));
                Assert.That(second.GetBodyPose(1).BodyName, Is.EqualTo("head"));
                Assert.That(second.GetBodyPose(1).PositionX, Is.EqualTo(0.2));

                Assert.That(
                    source.TryGetLatestPair(
                        out ReachyAuthoritativePoseSnapshot beforeReset,
                        out ReachyAuthoritativePoseSnapshot afterReset),
                    Is.True);
                Assert.That(beforeReset.DiscontinuityId, Is.EqualTo(1U));
                Assert.That(afterReset.DiscontinuityId, Is.EqualTo(2U));
                Assert.That(afterReset.Sequence, Is.EqualTo(0UL));
                Assert.That(afterReset.SimulationTime, Is.EqualTo(0.0));
            }
        }
    }
}
