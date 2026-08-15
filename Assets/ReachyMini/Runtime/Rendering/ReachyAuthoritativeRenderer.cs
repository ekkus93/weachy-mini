#nullable enable

using System;
using ReachyMini.Presentation;
using UnityEngine;
using UnityEngine.Playables;
using ReachyMini.Diagnostics;
using ReachyMini.RuntimeDiagnostics;

namespace ReachyMini.Rendering
{
    public enum ReachyAuthoritativeRendererStatus
    {
        Unbound = 0,
        WaitingForSnapshots = 1,
        Rendering = 2,
        Faulted = 3,
    }

    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    public sealed class ReachyAuthoritativeRenderer : MonoBehaviour
    {
        [SerializeField]
        private ReachyPresentationBody[] authoritativeBodies =
            Array.Empty<ReachyPresentationBody>();

        [SerializeField]
        private double interpolationBackTimeSeconds = 0.001;

        [SerializeField]
        private float invariantPositionToleranceMetres = 1.0e-5f;

        [SerializeField]
        private float invariantRotationToleranceDegrees = 0.05f;

        private IReachyAuthoritativePoseSource? poseSource;
        private IReachyReusableAuthoritativePoseSource? reusablePoseSource;
        private ReachyReusableAuthoritativePoseFrame? reusableOlderPose;
        private ReachyReusableAuthoritativePoseFrame? reusableNewerPose;
        private Vector3[] expectedPositions = Array.Empty<Vector3>();
        private Quaternion[] expectedRotations = Array.Empty<Quaternion>();
        private ulong expectedSequence;
        private double expectedSimulationTime;
        private uint expectedDiscontinuityId;
        private bool hasAppliedPose;
        private string fault = string.Empty;

        public ReachyAuthoritativeRendererStatus Status { get; private set; } =
            ReachyAuthoritativeRendererStatus.Unbound;

        public string Fault => fault;

        public int AuthoritativeBodyCount => authoritativeBodies.Length;

        public bool UsesReusablePoseBuffers =>
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
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }

            ReachyPresentationBody[] copy =
                new ReachyPresentationBody[bodies.Length];
            Array.Copy(bodies, copy, bodies.Length);
            ValidateBindingsOrThrow(copy);
            ValidateInvariantTolerancesOrThrow(
                invariantPositionToleranceMetres,
                invariantRotationToleranceDegrees);
            authoritativeBodies = copy;
            expectedPositions = new Vector3[copy.Length];
            expectedRotations = new Quaternion[copy.Length];
            ConfigureReusablePoseBuffers();
            ResetInvariantState();
            fault = string.Empty;
            Status = poseSource == null
                ? ReachyAuthoritativeRendererStatus.Unbound
                : ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
        }

        public void BindPoseSource(IReachyAuthoritativePoseSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            IReachyReusableAuthoritativePoseSource? reusable =
                source as IReachyReusableAuthoritativePoseSource;
            if (reusable != null &&
                authoritativeBodies.Length > 0 &&
                reusable.BodyCount != authoritativeBodies.Length)
            {
                throw new ArgumentException(
                    $"The reusable pose source contains {reusable.BodyCount} bodies, " +
                    $"but the generated presentation contains " +
                    $"{authoritativeBodies.Length}.",
                    nameof(source));
            }

            poseSource = source;
            reusablePoseSource = reusable;
            ConfigureReusablePoseBuffers();
            fault = string.Empty;
            ResetInvariantState();
            Status = ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
            enabled = true;
        }

        public bool ValidateAuthoritativeStructure()
        {
            if (Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                return false;
            }

            for (int index = 0; index < authoritativeBodies.Length; ++index)
            {
                ReachyPresentationBody body = authoritativeBodies[index];
                if (body == null)
                {
                    return EnterFault(
                        $"Authoritative body binding {index} is missing.");
                }
                if (body.BodyIndex != index)
                {
                    return EnterFault(
                        $"Authoritative body {body.BodyName} has index " +
                        $"{body.BodyIndex}, expected {index}.");
                }
                if (ContainsProhibitedWriter(
                    body.gameObject,
                    out string componentName))
                {
                    return EnterFault(
                        $"Authoritative body {body.BodyName} contains " +
                        $"prohibited transform writer {componentName}.");
                }
            }

            return true;
        }

        public bool RenderAtSimulationTime(
            ReachyAuthoritativePoseSnapshot older,
            ReachyAuthoritativePoseSnapshot newer,
            double targetSimulationTime)
        {
            if (older == null)
            {
                throw new ArgumentNullException(nameof(older));
            }
            if (newer == null)
            {
                throw new ArgumentNullException(nameof(newer));
            }
            return RenderAtSimulationTimeCore(
                older,
                newer,
                targetSimulationTime);
        }

        private bool RenderAtSimulationTimeCore(
            IReachyAuthoritativePoseFrame older,
            IReachyAuthoritativePoseFrame newer,
            double targetSimulationTime)
        {
            if (Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                return false;
            }
            if (!ValidateRenderedPoseInvariant() ||
                !ValidateAuthoritativeStructure())
            {
                return false;
            }
            if (!ValidateSnapshotPair(older, newer))
            {
                return false;
            }

            EnsureExpectedStorage();
            float alpha = CalculateInterpolationAlpha(
                older,
                newer,
                targetSimulationTime);
            for (int index = 0; index < authoritativeBodies.Length; ++index)
            {
                ReachyMujocoBodyPose olderPose = older.GetBodyPose(index);
                ReachyMujocoBodyPose newerPose = newer.GetBodyPose(index);
                Vector3 olderPosition =
                    ReachyCoordinateConverter.ToUnityPosition(olderPose);
                Vector3 newerPosition =
                    ReachyCoordinateConverter.ToUnityPosition(newerPose);
                Quaternion olderRotation =
                    ReachyCoordinateConverter.ToUnityRotation(olderPose);
                Quaternion newerRotation =
                    ReachyCoordinateConverter.ToUnityRotation(newerPose);
                Vector3 position = Vector3.LerpUnclamped(
                    olderPosition,
                    newerPosition,
                    alpha);
                Quaternion rotation = Quaternion.SlerpUnclamped(
                    olderRotation,
                    newerRotation,
                    alpha);

                Transform bodyTransform = authoritativeBodies[index].transform;
                bodyTransform.SetPositionAndRotation(position, rotation);
                expectedPositions[index] = position;
                expectedRotations[index] = rotation;
            }

            expectedSequence = newer.Sequence;
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
            return true;
        }

        private void Awake()
        {
            if (authoritativeBodies.Length > 0)
            {
                try
                {
                    ValidateBindingsOrThrow(authoritativeBodies);
                    ValidateInvariantTolerancesOrThrow(
                        invariantPositionToleranceMetres,
                        invariantRotationToleranceDegrees);
                    EnsureExpectedStorage();
                    ConfigureReusablePoseBuffers();
                }
                catch (ArgumentException exception)
                {
                    EnterFault(exception.Message);
                }
            }
        }

        private void OnEnable()
        {
            Application.onBeforeRender += ValidateBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ValidateBeforeRender;
        }

        private void LateUpdate()
        {
            if (Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                return;
            }
            if (poseSource == null)
            {
                Status = ReachyAuthoritativeRendererStatus.Unbound;
                return;
            }

            IReachyReusableAuthoritativePoseSource? reusable = reusablePoseSource;
            if (reusable != null)
            {
                ReachyReusableAuthoritativePoseFrame? older = reusableOlderPose;
                ReachyReusableAuthoritativePoseFrame? newer = reusableNewerPose;
                if (older == null || newer == null)
                {
                    EnterFault(
                        "Reusable authoritative pose buffers were not initialized.");
                    return;
                }
                if (!reusable.TryCopyLatestPair(older, newer))
                {
                    Status = ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
                    return;
                }

                double reusableTargetTime = Math.Max(
                    older.SimulationTime,
                    newer.SimulationTime - interpolationBackTimeSeconds);
                RenderAtSimulationTimeCore(
                    older,
                    newer,
                    reusableTargetTime);
                return;
            }

            if (!poseSource.TryGetLatestPair(
                out ReachyAuthoritativePoseSnapshot legacyOlder,
                out ReachyAuthoritativePoseSnapshot legacyNewer))
            {
                Status = ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
                return;
            }

            double targetTime = Math.Max(
                legacyOlder.SimulationTime,
                legacyNewer.SimulationTime - interpolationBackTimeSeconds);
            RenderAtSimulationTimeCore(
                legacyOlder,
                legacyNewer,
                targetTime);
        }

        private bool ValidateSnapshotPair(
            IReachyAuthoritativePoseFrame older,
            IReachyAuthoritativePoseFrame newer)
        {
            if (authoritativeBodies.Length == 0)
            {
                return EnterFault(
                    "The authoritative renderer has no body bindings.");
            }
            if (older.BodyCount != authoritativeBodies.Length ||
                newer.BodyCount != authoritativeBodies.Length)
            {
                return EnterFault(
                    "Authoritative snapshot body count does not match " +
                    "the generated prefab mapping.");
            }
            if (older.DiscontinuityId == newer.DiscontinuityId &&
                (newer.Sequence <= older.Sequence ||
                 newer.SimulationTime <= older.SimulationTime))
            {
                return EnterFault(
                    "Authoritative snapshots are not strictly ordered " +
                    "within a continuity epoch.");
            }

            for (int index = 0; index < authoritativeBodies.Length; ++index)
            {
                ReachyPresentationBody binding = authoritativeBodies[index];
                ReachyMujocoBodyPose olderPose = older.GetBodyPose(index);
                ReachyMujocoBodyPose newerPose = newer.GetBodyPose(index);
                if (olderPose.BodyIndex != binding.BodyIndex ||
                    newerPose.BodyIndex != binding.BodyIndex ||
                    (!string.IsNullOrEmpty(binding.BodyName) &&
                     (!string.Equals(
                         olderPose.BodyName,
                         binding.BodyName,
                         StringComparison.Ordinal) ||
                      !string.Equals(
                          newerPose.BodyName,
                          binding.BodyName,
                          StringComparison.Ordinal))))
                {
                    return EnterFault(
                        $"Authoritative snapshot body identity differs " +
                        $"at index {index}; expected model index " +
                        $"{binding.BodyIndex} name {binding.BodyName}.");
                }
            }

            return true;
        }

        public bool ValidateRenderedPoseInvariant()
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
                        $"Authoritative transform drift detected for " +
                        $"{authoritativeBodies[index].BodyName}: " +
                        $"sequence={expectedSequence} " +
                        $"simulation_time={expectedSimulationTime:R}s " +
                        $"continuity={expectedDiscontinuityId} " +
                        $"position={positionDrift:R}m " +
                        $"position_tolerance={invariantPositionToleranceMetres:R}m " +
                        $"rotation={rotationDrift:R}deg " +
                        $"rotation_tolerance={invariantRotationToleranceDegrees:R}deg.";
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
                $"Development authoritative rendering assertion failed: {message}",
                this);
#endif
        }

        private static float CalculateInterpolationAlpha(
            IReachyAuthoritativePoseFrame older,
            IReachyAuthoritativePoseFrame newer,
            double targetSimulationTime)
        {
            if (older.DiscontinuityId != newer.DiscontinuityId)
            {
                return 1.0f;
            }
            if (targetSimulationTime <= older.SimulationTime)
            {
                return 0.0f;
            }
            if (targetSimulationTime >= newer.SimulationTime)
            {
                return 1.0f;
            }

            double interval = newer.SimulationTime - older.SimulationTime;
            return checked((float)(
                (targetSimulationTime - older.SimulationTime) / interval));
        }

        private static void ValidateBindingsOrThrow(
            ReachyPresentationBody[] bodies)
        {
            if (bodies.Length == 0)
            {
                throw new ArgumentException(
                    "At least one authoritative body binding is required.",
                    nameof(bodies));
            }

            for (int index = 0; index < bodies.Length; ++index)
            {
                ReachyPresentationBody body = bodies[index];
                if (body == null)
                {
                    throw new ArgumentException(
                        $"Authoritative body binding {index} is null.",
                        nameof(bodies));
                }
                if (body.BodyIndex != index ||
                    string.IsNullOrWhiteSpace(body.BodyPath))
                {
                    throw new ArgumentException(
                        $"Authoritative body binding {index} is not " +
                        "configured for its canonical model index.",
                        nameof(bodies));
                }
            }
        }

        private static void ValidateInvariantTolerancesOrThrow(
            float positionToleranceMetres,
            float rotationToleranceDegrees)
        {
            if (float.IsNaN(positionToleranceMetres) ||
                float.IsInfinity(positionToleranceMetres) ||
                positionToleranceMetres <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionToleranceMetres),
                    "The invariant position tolerance must be finite and positive.");
            }
            if (float.IsNaN(rotationToleranceDegrees) ||
                float.IsInfinity(rotationToleranceDegrees) ||
                rotationToleranceDegrees <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rotationToleranceDegrees),
                    "The invariant rotation tolerance must be finite and positive.");
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
            GameObject body,
            out string componentName)
        {
            if (body.GetComponentInChildren<Rigidbody>(true) != null)
            {
                componentName = nameof(Rigidbody);
                return true;
            }
            if (body.GetComponentInChildren<Rigidbody2D>(true) != null)
            {
                componentName = nameof(Rigidbody2D);
                return true;
            }
            if (body.GetComponentInChildren<ArticulationBody>(true) != null)
            {
                componentName = nameof(ArticulationBody);
                return true;
            }
            if (body.GetComponentInChildren<Joint>(true) != null)
            {
                componentName = nameof(Joint);
                return true;
            }
            if (body.GetComponentInChildren<Joint2D>(true) != null)
            {
                componentName = nameof(Joint2D);
                return true;
            }
            if (body.GetComponentInChildren<Animator>(true) != null)
            {
                componentName = nameof(Animator);
                return true;
            }
            if (body.GetComponentInChildren<Animation>(true) != null)
            {
                componentName = nameof(Animation);
                return true;
            }
            if (body.GetComponentInChildren<PlayableDirector>(true) != null)
            {
                componentName = nameof(PlayableDirector);
                return true;
            }

            componentName = string.Empty;
            return false;
        }

        private void ConfigureReusablePoseBuffers()
        {
            if (reusablePoseSource == null || authoritativeBodies.Length == 0)
            {
                reusableOlderPose = null;
                reusableNewerPose = null;
                return;
            }
            if (reusablePoseSource.BodyCount != authoritativeBodies.Length)
            {
                throw new ArgumentException(
                    $"The reusable pose source contains " +
                    $"{reusablePoseSource.BodyCount} bodies, but the generated " +
                    $"presentation contains {authoritativeBodies.Length}.");
            }

            reusableOlderPose = reusablePoseSource.CreatePoseFrame();
            reusableNewerPose = reusablePoseSource.CreatePoseFrame();
        }

        private void EnsureExpectedStorage()
        {
            if (expectedPositions.Length != authoritativeBodies.Length ||
                expectedRotations.Length != authoritativeBodies.Length)
            {
                expectedPositions = new Vector3[authoritativeBodies.Length];
                expectedRotations = new Quaternion[authoritativeBodies.Length];
            }
        }

        private bool EnterFault(string message)
        {
            fault = message;
            Status = ReachyAuthoritativeRendererStatus.Faulted;
            ReachyRuntimeDiagnostics.Emit(
                "renderer",
                ReachyDiagnosticEventIds.RendererFaulted,
                ReachyDiagnosticSeverity.Error,
                ReachyDiagnosticErrorCategory.Rendering,
                new ReachyDiagnosticField(
                    "status",
                    Status.ToString(),
                    ReachyDiagnosticDataClass.Identifier));
            enabled = false;
            return false;
        }
    }
}
