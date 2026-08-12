#nullable enable

using System;
using ReachyMini.Interop;

namespace ReachyMini.Behavior
{
    public static class ReachyBehaviorAuthoritativeSafety
    {
        private const uint SleepingHealthFlag = 1U << 0;
        private const uint MujocoWarningHealthFlag = 1U << 1;
        private const uint ContactOverloadHealthFlag = 1U << 2;
        private const uint HardStopHealthFlag = 1U << 3;
        private const uint KnownHealthFlags =
            SleepingHealthFlag |
            MujocoWarningHealthFlag |
            ContactOverloadHealthFlag |
            HardStopHealthFlag;

        public static ReachyBehaviorMotionSnapshot CreateMotionSnapshot(
            ReachySimAuthoritativeStateFrame state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (state.ActuatorObservationCount !=
                ReachyBehaviorPlannerActuators.Count)
            {
                throw new ArgumentException(
                    "Authoritative state must expose exactly nine Reachy actuators.",
                    nameof(state));
            }

            var positions = new double[ReachyBehaviorPlannerActuators.Count];
            var velocities = new double[ReachyBehaviorPlannerActuators.Count];
            for (int index = 0; index < positions.Length; ++index)
            {
                ReachySimActuatorObservationSnapshot observation =
                    state.GetActuatorObservation(index);
                if (observation.ActuatorId != checked((uint)index))
                {
                    throw new ArgumentException(
                        "Authoritative actuator observations are not in canonical order.",
                        nameof(state));
                }
                positions[index] = observation.Length;
                velocities[index] = observation.Velocity;
            }
            return new ReachyBehaviorMotionSnapshot(positions, velocities);
        }

        public static ReachyBehaviorSafetySnapshot CreateSafetySnapshot(
            ReachySimAuthoritativeStateFrame state,
            bool normalControllerAvailable,
            bool workspaceClear)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            uint health = state.HealthFlags;
            bool sleeping = (health & SleepingHealthFlag) != 0U;
            bool unknownHealth = (health & ~KnownHealthFlags) != 0U;
            bool warning = (health & MujocoWarningHealthFlag) != 0U;
            bool contactOverload =
                (health & ContactOverloadHealthFlag) != 0U;
            bool hardStop = (health & HardStopHealthFlag) != 0U;

            return new ReachyBehaviorSafetySnapshot(
                motionPathAvailable: normalControllerAvailable && !sleeping,
                workspaceClear,
                activeFault: warning || unknownHealth,
                activeCollision: state.ContactCount != 0U,
                activeHardStop: hardStop,
                loadLimitActive: contactOverload);
        }
    }
}
