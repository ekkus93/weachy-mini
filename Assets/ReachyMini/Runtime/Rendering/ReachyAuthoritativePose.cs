#nullable enable

using System;

namespace ReachyMini.Rendering
{
    public readonly struct ReachyMujocoBodyPose
    {
        public ReachyMujocoBodyPose(
            int bodyIndex,
            string bodyName,
            double positionX,
            double positionY,
            double positionZ,
            double quaternionW,
            double quaternionX,
            double quaternionY,
            double quaternionZ)
        {
            if (bodyIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bodyIndex),
                    "A MuJoCo body index cannot be negative.");
            }
            BodyIndex = bodyIndex;
            BodyName = bodyName ?? throw new ArgumentNullException(nameof(bodyName));
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            QuaternionW = quaternionW;
            QuaternionX = quaternionX;
            QuaternionY = quaternionY;
            QuaternionZ = quaternionZ;
        }

        public int BodyIndex { get; }

        public string BodyName { get; }

        public double PositionX { get; }

        public double PositionY { get; }

        public double PositionZ { get; }

        public double QuaternionW { get; }

        public double QuaternionX { get; }

        public double QuaternionY { get; }

        public double QuaternionZ { get; }
    }

    public sealed class ReachyAuthoritativePoseSnapshot
    {
        private readonly ReachyMujocoBodyPose[] bodyPoses;

        public ReachyAuthoritativePoseSnapshot(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            ReachyMujocoBodyPose[] bodyPoses)
        {
            if (!IsFinite(simulationTime) || simulationTime < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(simulationTime),
                    "Simulation time must be finite and nonnegative.");
            }
            if (bodyPoses == null)
            {
                throw new ArgumentNullException(nameof(bodyPoses));
            }
            if (bodyPoses.Length == 0)
            {
                throw new ArgumentException(
                    "An authoritative pose snapshot must contain at least one body.",
                    nameof(bodyPoses));
            }

            ReachyMujocoBodyPose[] copied =
                new ReachyMujocoBodyPose[bodyPoses.Length];
            for (int index = 0; index < bodyPoses.Length; ++index)
            {
                ReachyMujocoBodyPose pose = bodyPoses[index];
                ValidatePose(pose, index);
                copied[index] = pose;
            }

            Sequence = sequence;
            SimulationTime = simulationTime;
            DiscontinuityId = discontinuityId;
            this.bodyPoses = copied;
        }

        public ulong Sequence { get; }

        public double SimulationTime { get; }

        public uint DiscontinuityId { get; }

        public int BodyCount => bodyPoses.Length;

        public ReachyMujocoBodyPose GetBodyPose(int index)
        {
            return bodyPoses[index];
        }

        private static void ValidatePose(
            ReachyMujocoBodyPose pose,
            int index)
        {
            if (pose.BodyIndex != index)
            {
                throw new ArgumentException(
                    $"Body pose {index} declares body index {pose.BodyIndex}.",
                    nameof(pose));
            }
            if (!IsFinite(pose.PositionX) ||
                !IsFinite(pose.PositionY) ||
                !IsFinite(pose.PositionZ) ||
                !IsFinite(pose.QuaternionW) ||
                !IsFinite(pose.QuaternionX) ||
                !IsFinite(pose.QuaternionY) ||
                !IsFinite(pose.QuaternionZ))
            {
                throw new ArgumentException(
                    $"Body pose {index} contains NaN or infinity.",
                    nameof(pose));
            }

            double normSquared =
                pose.QuaternionW * pose.QuaternionW +
                pose.QuaternionX * pose.QuaternionX +
                pose.QuaternionY * pose.QuaternionY +
                pose.QuaternionZ * pose.QuaternionZ;
            if (!IsFinite(normSquared) || normSquared <= 1.0e-24)
            {
                throw new ArgumentException(
                    $"Body pose {index} has an invalid quaternion.",
                    nameof(pose));
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public interface IReachyAuthoritativePoseSource
    {
        bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer);
    }

    public sealed class ReachyAuthoritativePoseBuffer :
        IReachyAuthoritativePoseSource
    {
        private readonly object gate = new object();
        private ReachyAuthoritativePoseSnapshot? older;
        private ReachyAuthoritativePoseSnapshot? newer;

        public void Publish(ReachyAuthoritativePoseSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (gate)
            {
                if (newer != null &&
                    snapshot.DiscontinuityId == newer.DiscontinuityId)
                {
                    if (snapshot.Sequence <= newer.Sequence)
                    {
                        throw new InvalidOperationException(
                            "Authoritative pose sequence must increase within a continuity epoch.");
                    }
                    if (snapshot.SimulationTime <= newer.SimulationTime)
                    {
                        throw new InvalidOperationException(
                            "Authoritative simulation time must increase within a continuity epoch.");
                    }
                }

                older = newer;
                newer = snapshot;
            }
        }

        public bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot olderSnapshot,
            out ReachyAuthoritativePoseSnapshot newerSnapshot)
        {
            lock (gate)
            {
                if (older == null || newer == null)
                {
                    olderSnapshot = null!;
                    newerSnapshot = null!;
                    return false;
                }

                olderSnapshot = older;
                newerSnapshot = newer;
                return true;
            }
        }
    }
}
