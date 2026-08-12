#nullable enable

using System;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker
    {
        public ReachySimAuthoritativeStateLayout AuthoritativeStateLayout =>
            authoritativeStateReader?.Layout ??
            throw new InvalidOperationException(
                "This simulation worker was not configured to publish authoritative state.");

        public ReachySimAuthoritativeStateFrame CreateAuthoritativeStateFrame()
        {
            return new ReachySimAuthoritativeStateFrame(AuthoritativeStateLayout);
        }

        public bool TryCaptureLatestAuthoritativeState(
            ReachySimAuthoritativeStateFrame destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            ReachySimAuthoritativeStateFrame? source = authoritativeStateFrame;
            ReachySimAuthoritativeStateLayout layout = AuthoritativeStateLayout;
            if (!layout.Matches(destination.Layout) || source == null)
            {
                throw new ArgumentException(
                    "The destination frame was created for a different authoritative state layout.",
                    nameof(destination));
            }

            lock (authoritativeStateGate)
            {
                if (!hasAuthoritativeState)
                {
                    return false;
                }
                CopyAuthoritativeState(source, destination);
                return true;
            }
        }

        private static void CopyAuthoritativeState(
            ReachySimAuthoritativeStateFrame source,
            ReachySimAuthoritativeStateFrame destination)
        {
            destination.Sequence = source.Sequence;
            destination.SimulationTime = source.SimulationTime;
            destination.ContinuityId = source.ContinuityId;
            destination.JointCount = source.JointCount;
            destination.ContactCount = source.ContactCount;
            destination.HealthFlags = source.HealthFlags;
            destination.CalibrationProfileId = source.CalibrationProfileId;
            destination.WarningCount = source.WarningCount;
            destination.ConstraintCount = source.ConstraintCount;
            destination.EqualityConstraintCount = source.EqualityConstraintCount;
            destination.MaximumConstraintResidual = source.MaximumConstraintResidual;
            destination.MaximumEqualityConstraintResidual =
                source.MaximumEqualityConstraintResidual;
            Array.Copy(
                source.QposStorage,
                destination.QposStorage,
                source.QposCount);
            Array.Copy(
                source.QvelStorage,
                destination.QvelStorage,
                source.QvelCount);
            for (int index = 0; index < source.ActuatorObservationCount; ++index)
            {
                destination.SetActuatorObservation(
                    index,
                    source.GetActuatorObservation(index));
            }
            for (int index = 0; index < source.BodyPoseCount; ++index)
            {
                destination.SetBodyPose(index, source.GetBodyPose(index));
            }
        }
    }
}
