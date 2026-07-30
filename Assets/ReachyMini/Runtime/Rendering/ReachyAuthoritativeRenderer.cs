#nullable enable

using System;
using ReachyMini.Presentation;
using UnityEngine;
using UnityEngine.Playables;

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
        private Vector3[] expectedPositions = Array.Empty<Vector3>();
        private Quaternion[] expectedRotations = Array.Empty<Quaternion>();
        private bool hasAppliedPose;
        private string fault = string.Empty;

        public ReachyAuthoritativeRendererStatus Status { get; private set; } =
            ReachyAuthoritativeRendererStatus.Unbound;

        public string Fault => fault;

        public int AuthoritativeBodyCount => authoritativeBodies.Length;

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
            authoritativeBodies = copy;
            expectedPositions = new Vector3[copy.Length];
            expectedRotations = new Quaternion[copy.Length];
            hasAppliedPose = false;
            fault = string.Empty;
            Status = poseSource == null
                ? ReachyAuthoritativeRendererStatus.Unbound
                : ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
        }

        public void BindPoseSource(IReachyAuthoritativePoseSource source)
        {
            poseSource = source ?? throw new ArgumentNullException(nameof(source));
            fault = string.Empty;
            hasAppliedPose = false;
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
            if (Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                return false;
            }
            if (!ValidatePreviousApplication() ||
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

            hasAppliedPose = true;
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
                    EnsureExpectedStorage();
                }
                catch (ArgumentException exception)
                {
                    EnterFault(exception.Message);
                }
            }
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
            if (!poseSource.TryGetLatestPair(
                out ReachyAuthoritativePoseSnapshot older,
                out ReachyAuthoritativePoseSnapshot newer))
            {
                Status = ReachyAuthoritativeRendererStatus.WaitingForSnapshots;
                return;
            }

            double targetTime = Math.Max(
                older.SimulationTime,
                newer.SimulationTime - interpolationBackTimeSeconds);
            RenderAtSimulationTime(older, newer, targetTime);
        }

        private bool ValidateSnapshotPair(
            ReachyAuthoritativePoseSnapshot older,
            ReachyAuthoritativePoseSnapshot newer)
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

        private bool ValidatePreviousApplication()
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
                        $"Authoritative transform drift detected for " +
                        $"{authoritativeBodies[index].BodyName}: " +
                        $"position={positionDrift:R}m " +
                        $"rotation={rotationDrift:R}deg.");
                }
            }

            return true;
        }

        private static float CalculateInterpolationAlpha(
            ReachyAuthoritativePoseSnapshot older,
            ReachyAuthoritativePoseSnapshot newer,
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

        private void EnsureExpectedStorage()
        {
            if (expectedPositions.Length != authoritativeBodies.Length)
            {
                expectedPositions = new Vector3[authoritativeBodies.Length];
                expectedRotations = new Quaternion[authoritativeBodies.Length];
            }
        }

        private bool EnterFault(string message)
        {
            fault = message;
            Status = ReachyAuthoritativeRendererStatus.Faulted;
            Debug.LogError(
                $"Reachy authoritative rendering fault: {message}",
                this);
            enabled = false;
            return false;
        }
    }
}
