#nullable enable

using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyAuthoritativeRenderingTests
    {
        private GameObject? root;
        private ReachyAuthoritativeRenderer? renderer;
        private ReachyPresentationBody[] bodies =
            Array.Empty<ReachyPresentationBody>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ReachyRendererTest");
            renderer = root.AddComponent<ReachyAuthoritativeRenderer>();
            bodies = new ReachyPresentationBody[2];
            for (int index = 0; index < bodies.Length; ++index)
            {
                GameObject bodyObject = new GameObject($"body_{index}");
                bodyObject.transform.SetParent(root.transform, false);
                ReachyPresentationBody body =
                    bodyObject.AddComponent<ReachyPresentationBody>();
                body.ConfigureGeneratedBody(
                    index,
                    $"/world/body_{index}",
                    $"body_{index}");
                bodies[index] = body;
            }
            renderer.ConfigureBodies(bodies);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CoordinateConversionMatchesPinnedManifestRule()
        {
            ReachyMujocoBodyPose pose = new ReachyMujocoBodyPose(
                0,
                "body",
                1.0,
                2.0,
                3.0,
                Math.Sqrt(0.5),
                0.0,
                0.0,
                Math.Sqrt(0.5));

            Vector3 position =
                ReachyCoordinateConverter.ToUnityPosition(pose);
            Quaternion rotation =
                ReachyCoordinateConverter.ToUnityRotation(pose);

            Assert.That(
                position,
                Is.EqualTo(new Vector3(1.0f, 3.0f, 2.0f)));
            Vector3 rotatedForward = rotation * Vector3.forward;
            Assert.That(
                rotatedForward.x,
                Is.EqualTo(-1.0f).Within(1.0e-5f));
            Assert.That(
                rotatedForward.y,
                Is.EqualTo(0.0f).Within(1.0e-5f));
            Assert.That(
                rotatedForward.z,
                Is.EqualTo(0.0f).Within(1.0e-5f));
        }

        [Test]
        public void InterpolationUsesSimulationTimestamps()
        {
            ReachyAuthoritativePoseSnapshot older = Snapshot(
                sequence: 10UL,
                simulationTime: 1.0,
                discontinuityId: 4U,
                xOffset: 0.0);
            ReachyAuthoritativePoseSnapshot newer = Snapshot(
                sequence: 11UL,
                simulationTime: 1.002,
                discontinuityId: 4U,
                xOffset: 2.0);

            bool rendered = renderer!.RenderAtSimulationTime(
                older,
                newer,
                targetSimulationTime: 1.001);

            Assert.That(rendered, Is.True);
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Rendering));
            Assert.That(
                bodies[0].transform.position.x,
                Is.EqualTo(1.0f).Within(1.0e-5f));
            Assert.That(
                bodies[1].transform.position.x,
                Is.EqualTo(2.0f).Within(1.0e-5f));
        }

        [Test]
        public void ResetDiscontinuitySnapsToNewSnapshot()
        {
            ReachyAuthoritativePoseSnapshot older = Snapshot(
                sequence: 100UL,
                simulationTime: 5.0,
                discontinuityId: 7U,
                xOffset: 20.0);
            ReachyAuthoritativePoseSnapshot newer = Snapshot(
                sequence: 0UL,
                simulationTime: 0.0,
                discontinuityId: 8U,
                xOffset: 2.0);

            bool rendered = renderer!.RenderAtSimulationTime(
                older,
                newer,
                targetSimulationTime: 4.0);

            Assert.That(rendered, Is.True);
            Assert.That(
                bodies[0].transform.position.x,
                Is.EqualTo(2.0f).Within(1.0e-5f));
        }

        [Test]
        public void ExternalTransformWriteFaultsInsteadOfBeingOverwritten()
        {
            ReachyAuthoritativePoseSnapshot older = Snapshot(
                sequence: 1UL,
                simulationTime: 0.0,
                discontinuityId: 1U,
                xOffset: 0.0);
            ReachyAuthoritativePoseSnapshot newer = Snapshot(
                sequence: 2UL,
                simulationTime: 0.002,
                discontinuityId: 1U,
                xOffset: 1.0);
            Assert.That(
                renderer!.RenderAtSimulationTime(
                    older,
                    newer,
                    0.001),
                Is.True);

            bodies[0].transform.position += Vector3.one;
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "Authoritative transform drift detected",
                    RegexOptions.CultureInvariant));

            bool rendered = renderer.RenderAtSimulationTime(
                older,
                newer,
                0.0015);

            Assert.That(rendered, Is.False);
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Faulted));
            StringAssert.Contains("transform drift", renderer.Fault);
        }

        [Test]
        public void UnityPhysicsComponentIsRejectedVisibly()
        {
            bodies[0].gameObject.AddComponent<Rigidbody>();
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "prohibited transform writer Rigidbody",
                    RegexOptions.CultureInvariant));

            bool valid = renderer!.ValidateAuthoritativeStructure();

            Assert.That(valid, Is.False);
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Faulted));
        }

        [Test]
        public void PoseBufferRequiresOrderedPublicationWithinEpoch()
        {
            ReachyAuthoritativePoseBuffer buffer =
                new ReachyAuthoritativePoseBuffer();
            buffer.Publish(Snapshot(1UL, 0.002, 1U, 0.0));
            buffer.Publish(Snapshot(2UL, 0.004, 1U, 1.0));

            Assert.That(
                buffer.TryGetLatestPair(
                    out ReachyAuthoritativePoseSnapshot older,
                    out ReachyAuthoritativePoseSnapshot newer),
                Is.True);
            Assert.That(older.Sequence, Is.EqualTo(1UL));
            Assert.That(newer.Sequence, Is.EqualTo(2UL));
            Assert.Throws<InvalidOperationException>(() =>
                buffer.Publish(Snapshot(2UL, 0.006, 1U, 2.0)));
        }

        private static ReachyAuthoritativePoseSnapshot Snapshot(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            double xOffset)
        {
            return new ReachyAuthoritativePoseSnapshot(
                sequence,
                simulationTime,
                discontinuityId,
                new[]
                {
                    new ReachyMujocoBodyPose(
                        0,
                        "body_0",
                        xOffset,
                        0.0,
                        0.0,
                        1.0,
                        0.0,
                        0.0,
                        0.0),
                    new ReachyMujocoBodyPose(
                        1,
                        "body_1",
                        xOffset + 1.0,
                        0.0,
                        0.0,
                        1.0,
                        0.0,
                        0.0,
                        0.0),
                });
        }
    }
}
