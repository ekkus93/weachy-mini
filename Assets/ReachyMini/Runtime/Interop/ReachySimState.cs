#nullable enable

using System;

namespace ReachyMini.Interop
{
    public readonly struct ReachySimStateSnapshot
    {
        public ReachySimStateSnapshot(
            ulong sequence,
            double simulationTime,
            uint bodyCount,
            uint jointCount,
            uint actuatorCount,
            uint contactCount,
            uint healthFlags)
        {
            Sequence = sequence;
            SimulationTime = simulationTime;
            BodyCount = bodyCount;
            JointCount = jointCount;
            ActuatorCount = actuatorCount;
            ContactCount = contactCount;
            HealthFlags = healthFlags;
        }

        public ulong Sequence { get; }

        public double SimulationTime { get; }

        public uint BodyCount { get; }

        public uint JointCount { get; }

        public uint ActuatorCount { get; }

        public uint ContactCount { get; }

        public uint HealthFlags { get; }

        internal static ReachySimStateSnapshot FromNative(
            NativeReachySimStateHeader state)
        {
            if (state.AbiVersion != Core.ProjectMetadata.NativeAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native state ABI {state.AbiVersion} does not match managed ABI {Core.ProjectMetadata.NativeAbiVersion}.");
            }
            if (state.StructSize !=
                checked((uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeReachySimStateHeader>()))
            {
                throw new InvalidOperationException(
                    $"Native state header size {state.StructSize} is incompatible.");
            }
            if (double.IsNaN(state.SimulationTime) ||
                double.IsInfinity(state.SimulationTime) ||
                state.SimulationTime < 0.0)
            {
                throw new InvalidOperationException(
                    "Native simulation state contains an invalid simulation time.");
            }

            return new ReachySimStateSnapshot(
                state.Sequence,
                state.SimulationTime,
                state.BodyCount,
                state.JointCount,
                state.ActuatorCount,
                state.ContactCount,
                state.HealthFlags);
        }
    }

    public sealed class ReachySimStateResult
    {
        private ReachySimStateResult(
            bool isSuccess,
            ReachySimStateSnapshot state,
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            State = state;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimStateSnapshot State { get; }

        public ReachySimError Error { get; }

        internal static ReachySimStateResult Success(
            ReachySimStateSnapshot state)
        {
            return new ReachySimStateResult(
                isSuccess: true,
                state,
                ReachySimError.NoError);
        }

        internal static ReachySimStateResult Failure(
            ReachySimError error)
        {
            return new ReachySimStateResult(
                isSuccess: false,
                default,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
