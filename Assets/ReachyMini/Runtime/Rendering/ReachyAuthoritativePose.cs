#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Interop;

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

    public interface IReachyAuthoritativePoseFrame
    {
        ulong Sequence { get; }

        double SimulationTime { get; }

        uint DiscontinuityId { get; }

        int BodyCount { get; }

        ReachyMujocoBodyPose GetBodyPose(int index);
    }

    public sealed class ReachyAuthoritativePoseSnapshot :
        IReachyAuthoritativePoseFrame
    {
        private readonly ReachyMujocoBodyPose[] bodyPoses;

        public ReachyAuthoritativePoseSnapshot(
            ulong sequence,
            double simulationTime,
            uint discontinuityId,
            ReachyMujocoBodyPose[] bodyPoses)
        {
            ValidateSimulationTime(simulationTime, nameof(simulationTime));
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
                ValidatePose(pose, index, nameof(bodyPoses));
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

        internal static void ValidateSimulationTime(
            double simulationTime,
            string parameterName)
        {
            if (!IsFinite(simulationTime) || simulationTime < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Simulation time must be finite and nonnegative.");
            }
        }

        internal static void ValidatePose(
            ReachyMujocoBodyPose pose,
            int index,
            string parameterName)
        {
            if (pose.BodyIndex != index)
            {
                throw new ArgumentException(
                    $"Body pose {index} declares body index {pose.BodyIndex}.",
                    parameterName);
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
                    parameterName);
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
                    parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ReachyReusableAuthoritativePoseFrame :
        IReachyAuthoritativePoseFrame
    {
        private readonly ReachyMujocoBodyPose[] bodyPoses;

        internal ReachyReusableAuthoritativePoseFrame(
            IReadOnlyList<string> canonicalBodyNames)
        {
            if (canonicalBodyNames == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodyNames));
            }
            if (canonicalBodyNames.Count == 0)
            {
                throw new ArgumentException(
                    "A reusable authoritative pose frame requires at least one body.",
                    nameof(canonicalBodyNames));
            }

            bodyPoses = new ReachyMujocoBodyPose[canonicalBodyNames.Count];
            for (int index = 0; index < bodyPoses.Length; ++index)
            {
                string bodyName = canonicalBodyNames[index];
                if (string.IsNullOrWhiteSpace(bodyName))
                {
                    throw new ArgumentException(
                        $"Canonical body name {index} is missing.",
                        nameof(canonicalBodyNames));
                }
                bodyPoses[index] = new ReachyMujocoBodyPose(
                    index,
                    bodyName,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0);
            }
        }

        public ulong Sequence { get; private set; }

        public double SimulationTime { get; private set; }

        public uint DiscontinuityId { get; private set; }

        public int BodyCount => bodyPoses.Length;

        public ReachyMujocoBodyPose GetBodyPose(int index)
        {
            return bodyPoses[index];
        }

        internal void CopyFrom(ReachySimAuthoritativeStateFrame source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (source.BodyPoseCount != bodyPoses.Length)
            {
                throw new ArgumentException(
                    "The authoritative state frame has a different body count.",
                    nameof(source));
            }
            ReachyAuthoritativePoseSnapshot.ValidateSimulationTime(
                source.SimulationTime,
                nameof(source));

            for (int index = 0; index < bodyPoses.Length; ++index)
            {
                ReachySimBodyPoseSnapshot nativePose = source.GetBodyPose(index);
                uint expectedBodyId = checked((uint)index + 1U);
                if (nativePose.BodyId != expectedBodyId)
                {
                    throw new InvalidOperationException(
                        $"Native body pose {index} declares MuJoCo body " +
                        $"{nativePose.BodyId}, expected {expectedBodyId}.");
                }
                ReachyMujocoBodyPose pose = CreatePose(index, nativePose);
                ReachyAuthoritativePoseSnapshot.ValidatePose(
                    pose,
                    index,
                    nameof(source));
            }

            for (int index = 0; index < bodyPoses.Length; ++index)
            {
                bodyPoses[index] = CreatePose(
                    index,
                    source.GetBodyPose(index));
            }
            Sequence = source.Sequence;
            SimulationTime = source.SimulationTime;
            DiscontinuityId = source.ContinuityId;
        }

        private ReachyMujocoBodyPose CreatePose(
            int index,
            ReachySimBodyPoseSnapshot nativePose)
        {
            return new ReachyMujocoBodyPose(
                index,
                bodyPoses[index].BodyName,
                nativePose.PositionX,
                nativePose.PositionY,
                nativePose.PositionZ,
                nativePose.QuaternionW,
                nativePose.QuaternionX,
                nativePose.QuaternionY,
                nativePose.QuaternionZ);
        }
    }

    public interface IReachyAuthoritativePoseSource
    {
        bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer);
    }

    public interface IReachyReusableAuthoritativePoseSource :
        IReachyAuthoritativePoseSource
    {
        int BodyCount { get; }

        ReachyReusableAuthoritativePoseFrame CreatePoseFrame();

        bool TryCopyLatestPair(
            ReachyReusableAuthoritativePoseFrame olderDestination,
            ReachyReusableAuthoritativePoseFrame newerDestination);
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
