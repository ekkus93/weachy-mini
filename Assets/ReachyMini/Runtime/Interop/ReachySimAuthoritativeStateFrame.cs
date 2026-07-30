#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    public readonly struct ReachySimActuatorObservationSnapshot
    {
        internal ReachySimActuatorObservationSnapshot(
            uint actuatorId,
            double controlValue,
            double actuatorForce,
            double length,
            double velocity)
        {
            ActuatorId = actuatorId;
            ControlValue = controlValue;
            ActuatorForce = actuatorForce;
            Length = length;
            Velocity = velocity;
        }

        public uint ActuatorId { get; }

        public double ControlValue { get; }

        public double ActuatorForce { get; }

        public double Length { get; }

        public double Velocity { get; }
    }

    public readonly struct ReachySimBodyPoseSnapshot
    {
        internal ReachySimBodyPoseSnapshot(
            uint bodyId,
            double positionX,
            double positionY,
            double positionZ,
            double quaternionW,
            double quaternionX,
            double quaternionY,
            double quaternionZ)
        {
            BodyId = bodyId;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            QuaternionW = quaternionW;
            QuaternionX = quaternionX;
            QuaternionY = quaternionY;
            QuaternionZ = quaternionZ;
        }

        public uint BodyId { get; }

        public double PositionX { get; }

        public double PositionY { get; }

        public double PositionZ { get; }

        public double QuaternionW { get; }

        public double QuaternionX { get; }

        public double QuaternionY { get; }

        public double QuaternionZ { get; }
    }

    public sealed class ReachySimAuthoritativeStateLayout
    {
        public ReachySimAuthoritativeStateLayout(
            int byteCount,
            ulong modelHash,
            int qposCount,
            int qvelCount,
            int actuatorObservationCount,
            int bodyPoseCount)
        {
            if (byteCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }
            if (qposCount < 0 || qvelCount < 0 ||
                actuatorObservationCount < 0 || bodyPoseCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(qposCount),
                    "Authoritative state counts cannot be negative.");
            }
            ByteCount = byteCount;
            ModelHash = modelHash;
            QposCount = qposCount;
            QvelCount = qvelCount;
            ActuatorObservationCount = actuatorObservationCount;
            BodyPoseCount = bodyPoseCount;
        }

        public int ByteCount { get; }

        public ulong ModelHash { get; }

        public int QposCount { get; }

        public int QvelCount { get; }

        public int ActuatorObservationCount { get; }

        public int BodyPoseCount { get; }

        internal bool Matches(ReachySimAuthoritativeStateLayout? other)
        {
            return other != null &&
                ByteCount == other.ByteCount &&
                ModelHash == other.ModelHash &&
                QposCount == other.QposCount &&
                QvelCount == other.QvelCount &&
                ActuatorObservationCount == other.ActuatorObservationCount &&
                BodyPoseCount == other.BodyPoseCount;
        }
    }

    public sealed class ReachySimAuthoritativeStateFrame
    {
        private readonly double[] qpos;
        private readonly double[] qvel;
        private readonly ReachySimActuatorObservationSnapshot[] actuatorObservations;
        private readonly ReachySimBodyPoseSnapshot[] bodyPoses;

        public ReachySimAuthoritativeStateFrame(
            ReachySimAuthoritativeStateLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            qpos = new double[layout.QposCount];
            qvel = new double[layout.QvelCount];
            actuatorObservations =
                new ReachySimActuatorObservationSnapshot[
                    layout.ActuatorObservationCount];
            bodyPoses = new ReachySimBodyPoseSnapshot[layout.BodyPoseCount];
        }

        public ReachySimAuthoritativeStateLayout Layout { get; }

        public ulong Sequence { get; internal set; }

        public double SimulationTime { get; internal set; }

        public uint ContinuityId { get; internal set; }

        public uint JointCount { get; internal set; }

        public uint ContactCount { get; internal set; }

        public uint HealthFlags { get; internal set; }

        public ulong CalibrationProfileId { get; internal set; }

        public ulong WarningCount { get; internal set; }

        public uint ConstraintCount { get; internal set; }

        public uint EqualityConstraintCount { get; internal set; }

        public double MaximumConstraintResidual { get; internal set; }

        public double MaximumEqualityConstraintResidual { get; internal set; }

        public int QposCount => qpos.Length;

        public int QvelCount => qvel.Length;

        public int ActuatorObservationCount => actuatorObservations.Length;

        public int BodyPoseCount => bodyPoses.Length;

        public double GetQpos(int index)
        {
            return qpos[index];
        }

        public double GetQvel(int index)
        {
            return qvel[index];
        }

        public ReachySimActuatorObservationSnapshot GetActuatorObservation(
            int index)
        {
            return actuatorObservations[index];
        }

        public ReachySimBodyPoseSnapshot GetBodyPose(int index)
        {
            return bodyPoses[index];
        }

        internal double[] QposStorage => qpos;

        internal double[] QvelStorage => qvel;

        internal void SetActuatorObservation(
            int index,
            ReachySimActuatorObservationSnapshot observation)
        {
            actuatorObservations[index] = observation;
        }

        internal void SetBodyPose(
            int index,
            ReachySimBodyPoseSnapshot bodyPose)
        {
            bodyPoses[index] = bodyPose;
        }
    }
}
