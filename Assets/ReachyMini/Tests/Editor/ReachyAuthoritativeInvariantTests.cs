#nullable enable

using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyAuthoritativeInvariantTests
    {
        private GameObject? root;
        private ReachyAuthoritativeRenderer? renderer;
        private ReachyPresentationBody? body;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ReachyInvariantTest");
            renderer = root.AddComponent<ReachyAuthoritativeRenderer>();
            GameObject bodyObject = new GameObject("body_0");
            bodyObject.transform.SetParent(root.transform, false);
            body = bodyObject.AddComponent<ReachyPresentationBody>();
            body.ConfigureGeneratedBody(0, "/world/body_0", "body_0");
            renderer.ConfigureBodies(new[] { body });
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
        public void DevelopmentAssertionReportsExpectedActualAndTolerance()
        {
            RenderInitialPose();
            body!.transform.position += new Vector3(0.01f, 0.0f, 0.0f);
            LogAssert.Expect(
                LogType.Assert,
                new Regex(
                    "Development authoritative rendering assertion failed:.*" +
                    "position_tolerance=.*rotation_tolerance=",
                    RegexOptions.CultureInvariant));
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "Authoritative transform drift detected.*sequence=2.*" +
                    "simulation_time=0.001.*continuity=3",
                    RegexOptions.CultureInvariant));

            Assert.That(renderer!.AssertRenderedPoseInvariant(), Is.False);

            ReachyAuthoritativeInvariantReport report =
                renderer.LastInvariantReport;
            Assert.That(report.WasEvaluated, Is.True);
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Sequence, Is.EqualTo(2UL));
            Assert.That(report.SimulationTime, Is.EqualTo(0.001).Within(1.0e-12));
            Assert.That(report.DiscontinuityId, Is.EqualTo(3U));
            Assert.That(report.BodyIndex, Is.Zero);
            Assert.That(report.BodyName, Is.EqualTo("body_0"));
            Assert.That(report.PositionDriftMetres, Is.GreaterThan(0.009f));
            Assert.That(
                report.PositionToleranceMetres,
                Is.EqualTo(renderer.InvariantPositionToleranceMetres));
            Assert.That(
                report.RotationToleranceDegrees,
                Is.EqualTo(renderer.InvariantRotationToleranceDegrees));
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Faulted));
        }

        [Test]
        public void DriftWithinConfiguredToleranceRemainsValid()
        {
            renderer!.ConfigureInvariantTolerances(0.02f, 1.0f);
            RenderInitialPose();
            body!.transform.position += new Vector3(0.01f, 0.0f, 0.0f);

            Assert.That(renderer.ValidateRenderedPoseInvariant(), Is.True);

            ReachyAuthoritativeInvariantReport report =
                renderer.LastInvariantReport;
            Assert.That(report.WasEvaluated, Is.True);
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.BodyIndex, Is.Zero);
            Assert.That(report.PositionDriftMetres, Is.EqualTo(0.01f).Within(1.0e-6f));
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Rendering));
        }

        [TestCase(typeof(Rigidbody), "Rigidbody")]
        [TestCase(typeof(Rigidbody2D), "Rigidbody2D")]
        [TestCase(typeof(ArticulationBody), "ArticulationBody")]
        [TestCase(typeof(Animator), "Animator")]
        [TestCase(typeof(Animation), "Animation")]
        [TestCase(typeof(PlayableDirector), "PlayableDirector")]
        public void ProhibitedWriterOnVisualDescendantIsRejected(
            Type componentType,
            string expectedName)
        {
            GameObject visual = new GameObject("visual");
            visual.transform.SetParent(body!.transform, false);
            visual.AddComponent(componentType);
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    $"prohibited transform writer {expectedName}",
                    RegexOptions.CultureInvariant));

            Assert.That(renderer!.ValidateAuthoritativeStructure(), Is.False);
            Assert.That(
                renderer.Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Faulted));
        }

        [TestCase(0.0f, 0.05f)]
        [TestCase(-0.001f, 0.05f)]
        [TestCase(float.NaN, 0.05f)]
        [TestCase(0.001f, 0.0f)]
        [TestCase(0.001f, -0.05f)]
        [TestCase(0.001f, float.PositiveInfinity)]
        public void InvalidInvariantToleranceIsRejected(
            float positionTolerance,
            float rotationTolerance)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                renderer!.ConfigureInvariantTolerances(
                    positionTolerance,
                    rotationTolerance));
        }

        private void RenderInitialPose()
        {
            ReachyAuthoritativePoseSnapshot older = Snapshot(
                1UL,
                0.0,
                3U,
                0.0);
            ReachyAuthoritativePoseSnapshot newer = Snapshot(
                2UL,
                0.002,
                3U,
                0.002);
            Assert.That(
                renderer!.RenderAtSimulationTime(older, newer, 0.001),
                Is.True,
                renderer.Fault);
        }

        private static ReachyAuthoritativePoseSnapshot Snapshot(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            double x)
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
                        x,
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
