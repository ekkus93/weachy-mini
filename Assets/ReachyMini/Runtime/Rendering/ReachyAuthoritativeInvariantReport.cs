#nullable enable

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
