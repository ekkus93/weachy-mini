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
        private const string TestModelSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private GameObject? root;
        private ReachyAuthoritativeRenderer? renderer;
        private ReachyPresentationBody[] bodies =
            Array.Empty<ReachyPresentationBody>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ReachyRendererTest");
            renderer = root.AddComponent<ReachyAuthoritativeRenderer>();
            bodies = CreateBodies(root, 2);
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
        public void GeneratedMetadataConfiguresCanonicalDisabledRenderer()
        {
            GameObject generatedRoot = new GameObject("GeneratedPresentationTest");
            try
            {
                ReachyPresentationRoot metadata =
                    generatedRoot.AddComponent<ReachyPresentationRoot>();
                metadata.ConfigureGeneratedPresentation(
                    1,
                    TestModelSha256,
                    2,
                    1);
                ReachyPresentationBody[] generatedBodies =
                    CreateBodies(generatedRoot, 2);

                ReachyAuthoritativeRenderer generatedRenderer =
                    generatedRoot.GetComponent<ReachyAuthoritativeRenderer>();
                Assert.That(generatedRenderer, Is.Not.Null);
                Assert.That(
                    generatedRenderer.AuthoritativeBodyCount,
                    Is.EqualTo(generatedBodies.Length));
                Assert.That(
                    generatedRenderer.Status,
                    Is.EqualTo(ReachyAuthoritativeRendererStatus.Unbound));
                Assert.That(generatedRenderer.enabled, Is.False);
                Assert.That(
                    generatedRenderer.ValidateAuthoritativeStructure(),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(generatedRoot);
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
        public void CoordinateConversionRejectsFloatOverflow()
        {
            ReachyMujocoBodyPose pose = new ReachyMujocoBodyPose(
                0,
                "body",
                double.MaxValue,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                0.0);

            Assert.Throws<ArgumentException>(() =>
                ReachyCoordinateConverter.ToUnityPosition(pose));
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
        public void RenderCadenceDoesNotChangeTimestampResult()
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

            Assert.That(
                renderer!.RenderAtSimulationTime(older, newer, 1.0005),
                Is.True);
            Assert.That(
                renderer.RenderAtSimulationTime(older, newer, 1.001),
                Is.True);
            Vector3[] multiFramePositions = CaptureBodyPositions();

            Assert.That(
                renderer.RenderAtSimulationTime(older, newer, 1.0),
                Is.True);
            Assert.That(
                renderer.RenderAtSimulationTime(older, newer, 1.001),
                Is.True);
            Vector3[] directPositions = CaptureBodyPositions();

            Assert.That(directPositions, Is.EqualTo(multiFramePositions));
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
        public void AnimatorIsRejectedVisibly()
        {
            bodies[0].gameObject.AddComponent<Animator>();
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "prohibited transform writer Animator",
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

        private static ReachyPresentationBody[] CreateBodies(
            GameObject parent,
            int count)
        {
            ReachyPresentationBody[] result =
                new ReachyPresentationBody[count];
            for (int index = 0; index < count; ++index)
            {
                GameObject bodyObject = new GameObject($"body_{index}");
                bodyObject.transform.SetParent(parent.transform, false);
                ReachyPresentationBody body =
                    bodyObject.AddComponent<ReachyPresentationBody>();
                body.ConfigureGeneratedBody(
                    index,
                    $"/world/body_{index}",
                    $"body_{index}");
                result[index] = body;
            }
            return result;
        }

        private Vector3[] CaptureBodyPositions()
        {
            Vector3[] result = new Vector3[bodies.Length];
            for (int index = 0; index < bodies.Length; ++index)
            {
                result[index] = bodies[index].transform.position;
            }
            return result;
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
