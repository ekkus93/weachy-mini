#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyPhase6AcceptanceTests
    {
        [Test]
        public void ThirtyAndSixtyFpsSamplingProduceTheSameTrajectoryPoint()
        {
            Vector3 thirtyFps = RenderCadence(30, 0.5);
            Vector3 sixtyFps = RenderCadence(60, 0.5);

            Assert.That(
                thirtyFps.x,
                Is.EqualTo(sixtyFps.x).Within(1.0e-6f));
            Assert.That(
                thirtyFps.y,
                Is.EqualTo(sixtyFps.y).Within(1.0e-6f));
            Assert.That(
                thirtyFps.z,
                Is.EqualTo(sixtyFps.z).Within(1.0e-6f));
            Assert.That(thirtyFps.x, Is.EqualTo(0.5f).Within(1.0e-6f));
        }

        [TestCase(typeof(Rigidbody), "Rigidbody")]
        [TestCase(typeof(Rigidbody2D), "Rigidbody2D")]
        [TestCase(typeof(ArticulationBody), "ArticulationBody")]
        [TestCase(typeof(Animator), "Animator")]
        [TestCase(typeof(Animation), "Animation")]
        [TestCase(typeof(PlayableDirector), "PlayableDirector")]
        public void KnownUnityTransformWritersAreRejected(
            Type componentType,
            string expectedComponentName)
        {
            GameObject root = new GameObject("AuthoritativeWriterTest");
            try
            {
                ReachyAuthoritativeRenderer renderer =
                    root.AddComponent<ReachyAuthoritativeRenderer>();
                ReachyPresentationBody body = CreateBody(root);
                renderer.ConfigureBodies(new[] { body });
                body.gameObject.AddComponent(componentType);
                bool valid = InvokeWithExpectedStructuredError(
                    renderer.ValidateAuthoritativeStructure);

                Assert.That(valid, Is.False);
                Assert.That(
                    renderer.Status,
                    Is.EqualTo(ReachyAuthoritativeRendererStatus.Faulted));
                StringAssert.Contains(expectedComponentName, renderer.Fault);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static T InvokeWithExpectedStructuredError<T>(Func<T> action)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                return action();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        private static Vector3 RenderCadence(
            int framesPerSecond,
            double targetSimulationTime)
        {
            GameObject root = new GameObject($"Cadence_{framesPerSecond}");
            try
            {
                ReachyAuthoritativeRenderer renderer =
                    root.AddComponent<ReachyAuthoritativeRenderer>();
                ReachyPresentationBody body = CreateBody(root);
                renderer.ConfigureBodies(new[] { body });
                ReachyAuthoritativePoseSnapshot older = Snapshot(
                    1UL,
                    0.0,
                    0.0);
                ReachyAuthoritativePoseSnapshot newer = Snapshot(
                    2UL,
                    1.0,
                    1.0);
                int frameCount = checked((int)Math.Round(
                    targetSimulationTime * framesPerSecond));
                for (int frame = 0; frame <= frameCount; ++frame)
                {
                    double sampleTime = Math.Min(
                        targetSimulationTime,
                        (double)frame / framesPerSecond);
                    Assert.That(
                        renderer.RenderAtSimulationTime(
                            older,
                            newer,
                            sampleTime),
                        Is.True);
                }
                return body.transform.position;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ReachyPresentationBody CreateBody(GameObject root)
        {
            GameObject bodyObject = new GameObject("body_0");
            bodyObject.transform.SetParent(root.transform, false);
            ReachyPresentationBody body =
                bodyObject.AddComponent<ReachyPresentationBody>();
            body.ConfigureGeneratedBody(
                0,
                "/world/body_0",
                "body_0");
            return body;
        }

        private static ReachyAuthoritativePoseSnapshot Snapshot(
            ulong sequence,
            double simulationTime,
            double positionX)
        {
            return new ReachyAuthoritativePoseSnapshot(
                sequence,
                simulationTime,
                1U,
                new[]
                {
                    new ReachyMujocoBodyPose(
                        0,
                        "body_0",
                        positionX,
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
