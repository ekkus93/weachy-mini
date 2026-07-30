using System;
using System.Linq;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Tests
{
    public sealed class ReachyGeneratedAuthoritativeMappingTests
    {
        private const string PrefabPath =
            "Assets/Generated/ReachyMini/UnityPresentation/Resources/" +
            "ReachyMiniPresentation.prefab";

        [Test]
        public void GeneratedDebugOverlayMapsEveryJointAndStartsDisabled()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationDebugOverlay overlay =
                    contents.GetComponent<ReachyPresentationDebugOverlay>();
                Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.IsVisible, Is.False);
                Assert.That(overlay.BodyCount, Is.EqualTo(18));
                Assert.That(overlay.JointCount, Is.EqualTo(16));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void GeneratedDiscontinuityAlignsHeadAndBothAntennas()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationBody[] bodies = GetCanonicalBodies(contents);
                ReachyAuthoritativeRenderer renderer =
                    contents.GetComponent<ReachyAuthoritativeRenderer>();
                Assert.That(renderer, Is.Not.Null);

                ReachyMujocoBodyPose[] olderPoses = CaptureMujocoPoses(bodies);
                ReachyMujocoBodyPose[] newerPoses =
                    (ReachyMujocoBodyPose[])olderPoses.Clone();

                ReachyPresentationBody head = RequireBody(bodies, "xl_330");
                ReachyPresentationBody rightAntenna = RequireBody(
                    bodies,
                    "dc15_a01_horn_dummy_7");
                ReachyPresentationBody leftAntenna = RequireBody(
                    bodies,
                    "dc15_a01_horn_dummy_8");

                Vector3 expectedHeadPosition =
                    head.transform.position + new Vector3(0.01f, -0.02f, 0.015f);
                Quaternion expectedHeadRotation =
                    Quaternion.Euler(12f, -8f, 5f) * head.transform.rotation;
                Quaternion expectedRightRotation =
                    Quaternion.AngleAxis(20f, rightAntenna.transform.up) *
                    rightAntenna.transform.rotation;
                Quaternion expectedLeftRotation =
                    Quaternion.AngleAxis(-15f, leftAntenna.transform.up) *
                    leftAntenna.transform.rotation;

                newerPoses[head.BodyIndex] = ToMujocoPose(
                    head,
                    expectedHeadPosition,
                    expectedHeadRotation);
                newerPoses[rightAntenna.BodyIndex] = ToMujocoPose(
                    rightAntenna,
                    rightAntenna.transform.position,
                    expectedRightRotation);
                newerPoses[leftAntenna.BodyIndex] = ToMujocoPose(
                    leftAntenna,
                    leftAntenna.transform.position,
                    expectedLeftRotation);

                ReachyAuthoritativePoseSnapshot awake =
                    new ReachyAuthoritativePoseSnapshot(
                        100,
                        1.0,
                        7,
                        olderPoses);
                ReachyAuthoritativePoseSnapshot sleep =
                    new ReachyAuthoritativePoseSnapshot(
                        101,
                        0.0,
                        8,
                        newerPoses);

                Assert.That(
                    renderer.RenderAtSimulationTime(awake, sleep, 0.5),
                    Is.True,
                    renderer.Fault);
                AssertVectorClose(
                    head.transform.position,
                    expectedHeadPosition,
                    1.0e-6f);
                Assert.That(
                    Quaternion.Angle(
                        head.transform.rotation,
                        expectedHeadRotation),
                    Is.LessThanOrEqualTo(1.0e-4f));
                Assert.That(
                    Quaternion.Angle(
                        rightAntenna.transform.rotation,
                        expectedRightRotation),
                    Is.LessThanOrEqualTo(1.0e-4f));
                Assert.That(
                    Quaternion.Angle(
                        leftAntenna.transform.rotation,
                        expectedLeftRotation),
                    Is.LessThanOrEqualTo(1.0e-4f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void GeneratedSteadyStateRenderLoopAllocatesNoManagedBytes()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationBody[] bodies = GetCanonicalBodies(contents);
                ReachyAuthoritativeRenderer renderer =
                    contents.GetComponent<ReachyAuthoritativeRenderer>();
                Assert.That(renderer, Is.Not.Null);

                ReachyMujocoBodyPose[] olderPoses = CaptureMujocoPoses(bodies);
                ReachyMujocoBodyPose[] newerPoses =
                    (ReachyMujocoBodyPose[])olderPoses.Clone();
                ReachyPresentationBody head = RequireBody(bodies, "xl_330");
                newerPoses[head.BodyIndex] = ToMujocoPose(
                    head,
                    head.transform.position + new Vector3(0.002f, 0.001f, -0.001f),
                    Quaternion.AngleAxis(2f, Vector3.up) * head.transform.rotation);

                ReachyAuthoritativePoseSnapshot older =
                    new ReachyAuthoritativePoseSnapshot(
                        200,
                        2.0,
                        9,
                        olderPoses);
                ReachyAuthoritativePoseSnapshot newer =
                    new ReachyAuthoritativePoseSnapshot(
                        201,
                        2.002,
                        9,
                        newerPoses);

                Assert.That(
                    renderer.RenderAtSimulationTime(older, newer, 2.001),
                    Is.True,
                    renderer.Fault);
                _ = GC.GetAllocatedBytesForCurrentThread();
                _ = GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 128; ++iteration)
                {
                    double targetTime = (iteration & 1) == 0
                        ? 2.0005
                        : 2.0015;
                    if (!renderer.RenderAtSimulationTime(
                            older,
                            newer,
                            targetTime))
                    {
                        Assert.Fail(renderer.Fault);
                    }
                }
                long allocatedBytes =
                    GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static ReachyPresentationBody[] GetCanonicalBodies(
            GameObject contents)
        {
            ReachyPresentationBody[] bodies = contents
                .GetComponentsInChildren<ReachyPresentationBody>(true)
                .OrderBy(body => body.BodyIndex)
                .ToArray();
            Assert.That(bodies, Has.Length.EqualTo(18));
            Assert.That(
                bodies.Select(body => body.BodyIndex),
                Is.EquivalentTo(Enumerable.Range(0, bodies.Length)));
            return bodies;
        }

        private static ReachyPresentationBody RequireBody(
            ReachyPresentationBody[] bodies,
            string name)
        {
            ReachyPresentationBody body = bodies.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.BodyName,
                    name,
                    StringComparison.Ordinal));
            Assert.That(body, Is.Not.Null, $"Generated body is missing: {name}");
            return body;
        }

        private static ReachyMujocoBodyPose[] CaptureMujocoPoses(
            ReachyPresentationBody[] bodies)
        {
            ReachyMujocoBodyPose[] poses =
                new ReachyMujocoBodyPose[bodies.Length];
            for (int index = 0; index < bodies.Length; ++index)
            {
                ReachyPresentationBody body = bodies[index];
                poses[index] = ToMujocoPose(
                    body,
                    body.transform.position,
                    body.transform.rotation);
            }
            return poses;
        }

        private static ReachyMujocoBodyPose ToMujocoPose(
            ReachyPresentationBody body,
            Vector3 unityPosition,
            Quaternion unityRotation)
        {
            return new ReachyMujocoBodyPose(
                body.BodyIndex,
                body.BodyName,
                unityPosition.x,
                unityPosition.z,
                unityPosition.y,
                unityRotation.w,
                -unityRotation.x,
                -unityRotation.z,
                -unityRotation.y);
        }

        private static void AssertVectorClose(
            Vector3 actual,
            Vector3 expected,
            float tolerance)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThanOrEqualTo(tolerance));
        }
    }
}
