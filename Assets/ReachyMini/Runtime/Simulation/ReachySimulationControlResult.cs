#nullable enable

using System;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed class ReachySimulationFault
    {
        internal ReachySimulationFault(
            string operation,
            ReachySimError error)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public string Operation { get; }

        public ReachySimError Error { get; }
    }

    public sealed class ReachySimulationControlResult
    {
        private ReachySimulationControlResult(
            bool isSuccess,
            ReachySimulationRunState state,
            int discardedCommandCount,
            ReachySimSnapshot? capturedSnapshot,
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            State = state;
            DiscardedCommandCount = discardedCommandCount;
            CapturedSnapshot = capturedSnapshot;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimulationRunState State { get; }

        public int DiscardedCommandCount { get; }

        public ReachySimSnapshot? CapturedSnapshot { get; }

        public ReachySimError Error { get; }

        internal static ReachySimulationControlResult Success(
            ReachySimulationRunState state,
            int discardedCommandCount = 0,
            ReachySimSnapshot? capturedSnapshot = null)
        {
            return new ReachySimulationControlResult(
                isSuccess: true,
                state,
                discardedCommandCount,
                capturedSnapshot,
                ReachySimError.NoError);
        }

        internal static ReachySimulationControlResult Failure(
            ReachySimulationRunState state,
            ReachySimError error)
        {
            return new ReachySimulationControlResult(
                isSuccess: false,
                state,
                discardedCommandCount: 0,
                capturedSnapshot: null,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
