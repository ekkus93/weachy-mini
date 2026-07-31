#!/usr/bin/env python3
"""Apply the guarded RMA-052 authoritative-rendering invariant patch."""

from pathlib import Path


RENDERER_PATH = Path(
    "Assets/ReachyMini/Runtime/Rendering/ReachyAuthoritativeRenderer.cs"
)
REPORT_PATH = Path(
    "Assets/ReachyMini/Runtime/Rendering/ReachyAuthoritativeInvariantReport.cs"
)
TEST_PATH = Path(
    "Assets/ReachyMini/Tests/Editor/ReachyAuthoritativeInvariantTests.cs"
)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected one {label} block, found {count}.")
    return source.replace(old, new)


def main() -> None:
    if REPORT_PATH.exists() or TEST_PATH.exists():
        raise SystemExit("RMA-052 output files already exist.")

    source = RENDERER_PATH.read_text(encoding="utf-8")

    source = replace_once(
        source,
        """        private Vector3[] expectedPositions = Array.Empty<Vector3>();
        private Quaternion[] expectedRotations = Array.Empty<Quaternion>();
        private bool hasAppliedPose;
        private string fault = string.Empty;
""",
        """        private Vector3[] expectedPositions = Array.Empty<Vector3>();
        private Quaternion[] expectedRotations = Array.Empty<Quaternion>();
        private ulong expectedSequence;
        private double expectedSimulationTime;
        private uint expectedDiscontinuityId;
        private bool hasAppliedPose;
        private string fault = string.Empty;
""",
        "renderer invariant fields",
    )

    source = replace_once(
        source,
        """        public bool UsesReusablePoseBuffers =>
            reusablePoseSource != null &&
            reusableOlderPose != null &&
            reusableNewerPose != null;

        public void ConfigureBodies(ReachyPresentationBody[] bodies)
""",
        """        public bool UsesReusablePoseBuffers =>
            reusablePoseSource != null &&
            reusableOlderPose != null &&
            reusableNewerPose != null;

        public float InvariantPositionToleranceMetres =>
            invariantPositionToleranceMetres;

        public float InvariantRotationToleranceDegrees =>
            invariantRotationToleranceDegrees;

        public ReachyAuthoritativeInvariantReport LastInvariantReport
        {
            get;
            private set;
        } = ReachyAuthoritativeInvariantReport.NotEvaluated;

        public void ConfigureInvariantTolerances(
            float positionToleranceMetres,
            float rotationToleranceDegrees)
        {
            ValidateInvariantTolerancesOrThrow(
                positionToleranceMetres,
                rotationToleranceDegrees);
            invariantPositionToleranceMetres = positionToleranceMetres;
            invariantRotationToleranceDegrees = rotationToleranceDegrees;
            if (hasAppliedPose)
            {
                LastInvariantReport = ReachyAuthoritativeInvariantReport.Valid(
                    expectedSequence,
                    expectedSimulationTime,
                    expectedDiscontinuityId,
                    invariantPositionToleranceMetres,
                    invariantRotationToleranceDegrees);
            }
        }

        public void ConfigureBodies(ReachyPresentationBody[] bodies)
""",
        "renderer invariant properties",
    )

    source = replace_once(
        source,
        """            Array.Copy(bodies, copy, bodies.Length);
            ValidateBindingsOrThrow(copy);
            authoritativeBodies = copy;
""",
        """            Array.Copy(bodies, copy, bodies.Length);
            ValidateBindingsOrThrow(copy);
            ValidateInvariantTolerancesOrThrow(
                invariantPositionToleranceMetres,
                invariantRotationToleranceDegrees);
            authoritativeBodies = copy;
""",
        "body configuration validation",
    )

    reset_count = source.count("            hasAppliedPose = false;\n")
    if reset_count != 2:
        raise SystemExit(
            f"Expected two renderer invariant reset sites, found {reset_count}."
        )
    source = source.replace(
        "            hasAppliedPose = false;\n",
        "            ResetInvariantState();\n",
    )

    source = replace_once(
        source,
        """            if (!ValidatePreviousApplication() ||
                !ValidateAuthoritativeStructure())
""",
        """            if (!ValidateRenderedPoseInvariant() ||
                !ValidateAuthoritativeStructure())
""",
        "render-time invariant validation",
    )

    source = replace_once(
        source,
        """            hasAppliedPose = true;
            Status = ReachyAuthoritativeRendererStatus.Rendering;
""",
        """            expectedSequence = newer.Sequence;
            expectedSimulationTime = targetSimulationTime;
            expectedDiscontinuityId = newer.DiscontinuityId;
            hasAppliedPose = true;
            LastInvariantReport = ReachyAuthoritativeInvariantReport.Valid(
                expectedSequence,
                expectedSimulationTime,
                expectedDiscontinuityId,
                invariantPositionToleranceMetres,
                invariantRotationToleranceDegrees);
            Status = ReachyAuthoritativeRendererStatus.Rendering;
""",
        "rendered invariant identity",
    )

    source = replace_once(
        source,
        """                    ValidateBindingsOrThrow(authoritativeBodies);
                    EnsureExpectedStorage();
""",
        """                    ValidateBindingsOrThrow(authoritativeBodies);
                    ValidateInvariantTolerancesOrThrow(
                        invariantPositionToleranceMetres,
                        invariantRotationToleranceDegrees);
                    EnsureExpectedStorage();
""",
        "serialized tolerance validation",
    )

    source = replace_once(
        source,
        """        private void LateUpdate()
""",
        """        private void OnEnable()
        {
            Application.onBeforeRender += ValidateBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ValidateBeforeRender;
        }

        private void LateUpdate()
""",
        "pre-render subscription",
    )

    old_validation = """        private bool ValidatePreviousApplication()
        {
            if (!hasAppliedPose)
            {
                return true;
            }

            for (int index = 0; index < authoritativeBodies.Length; ++index)
            {
                Transform bodyTransform = authoritativeBodies[index].transform;
                float positionDrift = Vector3.Distance(
                    bodyTransform.position,
                    expectedPositions[index]);
                float rotationDrift = Quaternion.Angle(
                    bodyTransform.rotation,
                    expectedRotations[index]);
                if (positionDrift > invariantPositionToleranceMetres ||
                    rotationDrift > invariantRotationToleranceDegrees)
                {
                    return EnterFault(
                        $\"Authoritative transform drift detected for \" +
                        $\"{authoritativeBodies[index].BodyName}: \" +
                        $\"position={positionDrift:R}m \" +
                        $\"rotation={rotationDrift:R}deg.\");
                }
            }

            return true;
        }
"""
    new_validation = """        public bool ValidateRenderedPoseInvariant()
        {
            return ValidateRenderedPoseInvariantCore(
                assertInDevelopmentBuild: false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool AssertRenderedPoseInvariant()
        {
            return ValidateRenderedPoseInvariantCore(
                assertInDevelopmentBuild: true);
        }
#endif

        private void ValidateBeforeRender()
        {
            if (!hasAppliedPose ||
                Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssertRenderedPoseInvariant();
#else
            ValidateRenderedPoseInvariant();
#endif
        }

        private bool ValidateRenderedPoseInvariantCore(
            bool assertInDevelopmentBuild)
        {
            if (!hasAppliedPose)
            {
                return true;
            }

            float maximumSeverity = -1.0f;
            int maximumBodyIndex = -1;
            Vector3 maximumExpectedPosition = default;
            Vector3 maximumActualPosition = default;
            Quaternion maximumExpectedRotation = default;
            Quaternion maximumActualRotation = default;
            float maximumPositionDrift = 0.0f;
            float maximumRotationDrift = 0.0f;

            for (int index = 0; index < authoritativeBodies.Length; ++index)
            {
                Transform bodyTransform = authoritativeBodies[index].transform;
                Vector3 actualPosition = bodyTransform.position;
                Quaternion actualRotation = bodyTransform.rotation;
                Vector3 expectedPosition = expectedPositions[index];
                Quaternion expectedRotation = expectedRotations[index];
                float positionDrift = Vector3.Distance(
                    actualPosition,
                    expectedPosition);
                float rotationDrift = Quaternion.Angle(
                    actualRotation,
                    expectedRotation);
                float severity = Math.Max(
                    positionDrift / invariantPositionToleranceMetres,
                    rotationDrift / invariantRotationToleranceDegrees);
                if (severity > maximumSeverity)
                {
                    maximumSeverity = severity;
                    maximumBodyIndex = index;
                    maximumExpectedPosition = expectedPosition;
                    maximumActualPosition = actualPosition;
                    maximumExpectedRotation = expectedRotation;
                    maximumActualRotation = actualRotation;
                    maximumPositionDrift = positionDrift;
                    maximumRotationDrift = rotationDrift;
                }

                if (positionDrift > invariantPositionToleranceMetres ||
                    rotationDrift > invariantRotationToleranceDegrees)
                {
                    string message =
                        $\"Authoritative transform drift detected for \" +
                        $\"{authoritativeBodies[index].BodyName}: \" +
                        $\"sequence={expectedSequence} \" +
                        $\"simulation_time={expectedSimulationTime:R}s \" +
                        $\"continuity={expectedDiscontinuityId} \" +
                        $\"position={positionDrift:R}m \" +
                        $\"position_tolerance={invariantPositionToleranceMetres:R}m \" +
                        $\"rotation={rotationDrift:R}deg \" +
                        $\"rotation_tolerance={invariantRotationToleranceDegrees:R}deg.\";
                    LastInvariantReport =
                        ReachyAuthoritativeInvariantReport.Violation(
                            expectedSequence,
                            expectedSimulationTime,
                            expectedDiscontinuityId,
                            index,
                            authoritativeBodies[index].BodyName,
                            expectedPosition,
                            actualPosition,
                            expectedRotation,
                            actualRotation,
                            positionDrift,
                            rotationDrift,
                            invariantPositionToleranceMetres,
                            invariantRotationToleranceDegrees);
                    if (assertInDevelopmentBuild)
                    {
                        AssertDevelopmentInvariant(message);
                    }
                    return EnterFault(message);
                }
            }

            string maximumBodyName = maximumBodyIndex >= 0
                ? authoritativeBodies[maximumBodyIndex].BodyName
                : string.Empty;
            LastInvariantReport = ReachyAuthoritativeInvariantReport.Valid(
                expectedSequence,
                expectedSimulationTime,
                expectedDiscontinuityId,
                invariantPositionToleranceMetres,
                invariantRotationToleranceDegrees,
                maximumBodyIndex,
                maximumBodyName,
                maximumExpectedPosition,
                maximumActualPosition,
                maximumExpectedRotation,
                maximumActualRotation,
                maximumPositionDrift,
                maximumRotationDrift);
            return true;
        }

        private void AssertDevelopmentInvariant(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Assert(
                false,
                $\"Development authoritative rendering assertion failed: {message}\",
                this);
#endif
        }
"""
    source = replace_once(
        source,
        old_validation,
        new_validation,
        "renderer drift validator",
    )

    source = replace_once(
        source,
        """        private static bool ContainsProhibitedWriter(
""",
        """        private static void ValidateInvariantTolerancesOrThrow(
            float positionToleranceMetres,
            float rotationToleranceDegrees)
        {
            if (float.IsNaN(positionToleranceMetres) ||
                float.IsInfinity(positionToleranceMetres) ||
                positionToleranceMetres <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionToleranceMetres),
                    \"The invariant position tolerance must be finite and positive.\");
            }
            if (float.IsNaN(rotationToleranceDegrees) ||
                float.IsInfinity(rotationToleranceDegrees) ||
                rotationToleranceDegrees <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rotationToleranceDegrees),
                    \"The invariant rotation tolerance must be finite and positive.\");
            }
        }

        private void ResetInvariantState()
        {
            expectedSequence = 0UL;
            expectedSimulationTime = 0.0;
            expectedDiscontinuityId = 0U;
            hasAppliedPose = false;
            LastInvariantReport = ReachyAuthoritativeInvariantReport.NotEvaluated;
        }

        private static bool ContainsProhibitedWriter(
""",
        "tolerance and state helpers",
    )

    RENDERER_PATH.write_text(source, encoding="utf-8")

    REPORT_PATH.write_text(
        """#nullable enable

using UnityEngine;

namespace ReachyMini.Rendering
{
    public readonly struct ReachyAuthoritativeInvariantReport
    {
        private ReachyAuthoritativeInvariantReport(
            bool wasEvaluated,
            bool isValid,
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            int bodyIndex,
            string bodyName,
            Vector3 expectedPosition,
            Vector3 actualPosition,
            Quaternion expectedRotation,
            Quaternion actualRotation,
            float positionDriftMetres,
            float rotationDriftDegrees,
            float positionToleranceMetres,
            float rotationToleranceDegrees)
        {
            WasEvaluated = wasEvaluated;
            IsValid = isValid;
            Sequence = sequence;
            SimulationTime = simulationTime;
            DiscontinuityId = discontinuityId;
            BodyIndex = bodyIndex;
            BodyName = bodyName;
            ExpectedPosition = expectedPosition;
            ActualPosition = actualPosition;
            ExpectedRotation = expectedRotation;
            ActualRotation = actualRotation;
            PositionDriftMetres = positionDriftMetres;
            RotationDriftDegrees = rotationDriftDegrees;
            PositionToleranceMetres = positionToleranceMetres;
            RotationToleranceDegrees = rotationToleranceDegrees;
        }

        public static ReachyAuthoritativeInvariantReport NotEvaluated =>
            new ReachyAuthoritativeInvariantReport(
                false,
                true,
                0UL,
                0.0,
                0U,
                -1,
                string.Empty,
                default,
                default,
                default,
                default,
                0.0f,
                0.0f,
                0.0f,
                0.0f);

        public bool WasEvaluated { get; }

        public bool IsValid { get; }

        public ulong Sequence { get; }

        public double SimulationTime { get; }

        public uint DiscontinuityId { get; }

        public int BodyIndex { get; }

        public string BodyName { get; }

        public Vector3 ExpectedPosition { get; }

        public Vector3 ActualPosition { get; }

        public Quaternion ExpectedRotation { get; }

        public Quaternion ActualRotation { get; }

        public float PositionDriftMetres { get; }

        public float RotationDriftDegrees { get; }

        public float PositionToleranceMetres { get; }

        public float RotationToleranceDegrees { get; }

        internal static ReachyAuthoritativeInvariantReport Valid(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            float positionToleranceMetres,
            float rotationToleranceDegrees,
            int bodyIndex = -1,
            string? bodyName = null,
            Vector3 expectedPosition = default,
            Vector3 actualPosition = default,
            Quaternion expectedRotation = default,
            Quaternion actualRotation = default,
            float positionDriftMetres = 0.0f,
            float rotationDriftDegrees = 0.0f)
        {
            return new ReachyAuthoritativeInvariantReport(
                true,
                true,
                sequence,
                simulationTime,
                discontinuityId,
                bodyIndex,
                bodyName ?? string.Empty,
                expectedPosition,
                actualPosition,
                expectedRotation,
                actualRotation,
                positionDriftMetres,
                rotationDriftDegrees,
                positionToleranceMetres,
                rotationToleranceDegrees);
        }

        internal static ReachyAuthoritativeInvariantReport Violation(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            int bodyIndex,
            string bodyName,
            Vector3 expectedPosition,
            Vector3 actualPosition,
            Quaternion expectedRotation,
            Quaternion actualRotation,
            float positionDriftMetres,
            float rotationDriftDegrees,
            float positionToleranceMetres,
            float rotationToleranceDegrees)
        {
            return new ReachyAuthoritativeInvariantReport(
                true,
                false,
                sequence,
                simulationTime,
                discontinuityId,
                bodyIndex,
                bodyName,
                expectedPosition,
                actualPosition,
                expectedRotation,
                actualRotation,
                positionDriftMetres,
                rotationDriftDegrees,
                positionToleranceMetres,
                rotationToleranceDegrees);
        }
    }
}
""",
        encoding="utf-8",
    )

    TEST_PATH.write_text(
        """#nullable enable

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
            root = new GameObject(\"ReachyInvariantTest\");
            renderer = root.AddComponent<ReachyAuthoritativeRenderer>();
            GameObject bodyObject = new GameObject(\"body_0\");
            bodyObject.transform.SetParent(root.transform, false);
            body = bodyObject.AddComponent<ReachyPresentationBody>();
            body.ConfigureGeneratedBody(0, \"/world/body_0\", \"body_0\");
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
                    \"Development authoritative rendering assertion failed:.*\" +
                    \"position_tolerance=.*rotation_tolerance=\",
                    RegexOptions.CultureInvariant));
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    \"Authoritative transform drift detected.*sequence=2.*\" +
                    \"simulation_time=0.001.*continuity=3\",
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
            Assert.That(report.BodyName, Is.EqualTo(\"body_0\"));
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

        [TestCase(typeof(Rigidbody), \"Rigidbody\")]
        [TestCase(typeof(Rigidbody2D), \"Rigidbody2D\")]
        [TestCase(typeof(ArticulationBody), \"ArticulationBody\")]
        [TestCase(typeof(Animator), \"Animator\")]
        [TestCase(typeof(Animation), \"Animation\")]
        [TestCase(typeof(PlayableDirector), \"PlayableDirector\")]
        public void ProhibitedWriterOnVisualDescendantIsRejected(
            Type componentType,
            string expectedName)
        {
            GameObject visual = new GameObject(\"visual\");
            visual.transform.SetParent(body!.transform, false);
            visual.AddComponent(componentType);
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    $\"prohibited transform writer {expectedName}\",
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
                        \"body_0\",
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
""",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
