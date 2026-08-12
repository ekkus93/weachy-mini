#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Behavior
{
    public sealed class ReachyBehaviorMotionSnapshot
    {
        private readonly ReadOnlyCollection<double> positions;
        private readonly ReadOnlyCollection<double> velocities;

        public ReachyBehaviorMotionSnapshot(
            IReadOnlyList<double> positionsRadians,
            IReadOnlyList<double> velocitiesRadiansPerSecond)
        {
            positions = CopyFiniteVector(
                positionsRadians,
                nameof(positionsRadians));
            velocities = CopyFiniteVector(
                velocitiesRadiansPerSecond,
                nameof(velocitiesRadiansPerSecond));
        }

        public IReadOnlyList<double> PositionsRadians => positions;

        public IReadOnlyList<double> VelocitiesRadiansPerSecond => velocities;

        private static ReadOnlyCollection<double> CopyFiniteVector(
            IReadOnlyList<double> source,
            string name)
        {
            if (source == null ||
                source.Count != ReachyBehaviorPlannerActuators.Count)
            {
                throw new ArgumentException(
                    "Behavior motion snapshots require exactly nine actuator values.",
                    name);
            }

            var copy = new List<double>(source.Count);
            for (int index = 0; index < source.Count; ++index)
            {
                double value = source[index];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(name);
                }
                copy.Add(value);
            }
            return copy.AsReadOnly();
        }
    }

    public sealed class ReachyBehaviorSafetySnapshot
    {
        public ReachyBehaviorSafetySnapshot(
            bool motionPathAvailable,
            bool workspaceClear,
            bool activeFault,
            bool activeCollision,
            bool activeHardStop,
            bool loadLimitActive)
        {
            MotionPathAvailable = motionPathAvailable;
            WorkspaceClear = workspaceClear;
            ActiveFault = activeFault;
            ActiveCollision = activeCollision;
            ActiveHardStop = activeHardStop;
            LoadLimitActive = loadLimitActive;
        }

        public bool MotionPathAvailable { get; }

        public bool WorkspaceClear { get; }

        public bool ActiveFault { get; }

        public bool ActiveCollision { get; }

        public bool ActiveHardStop { get; }

        public bool LoadLimitActive { get; }

        public bool AllowsMotion =>
            MotionPathAvailable &&
            WorkspaceClear &&
            !ActiveFault &&
            !ActiveCollision &&
            !ActiveHardStop &&
            !LoadLimitActive;
    }
}
