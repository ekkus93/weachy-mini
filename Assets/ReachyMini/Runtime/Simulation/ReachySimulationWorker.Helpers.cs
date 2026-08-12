#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker
    {
        private void EnterManagedFault(
            string operation,
            Exception exception)
        {
            EnterNativeFault(
                operation,
                ManagedFaultError(exception));
        }

        private void EnterNativeFault(
            string operation,
            ReachySimError error)
        {
            lock (controlGate)
            {
                fault = new ReachySimulationFault(operation, error);
                if (runState != ReachySimulationRunState.Stopping &&
                    runState != ReachySimulationRunState.Stopped)
                {
                    runState = ReachySimulationRunState.Faulted;
                }
                Monitor.PulseAll(controlGate);
            }
            wakeSignal.Set();
        }

        private void SetRunState(ReachySimulationRunState state)
        {
            lock (controlGate)
            {
                runState = state;
                Monitor.PulseAll(controlGate);
            }
        }

        private static ReachySimError ManagedFaultError(
            Exception exception)
        {
            return ControlError(
                ReachySimErrorCode.ManagedInteropFailure,
                ReachySimRecoverability.RecreateHandle,
                $"{exception.GetType().Name}: {exception.Message}");
        }

        private static ReachySimError TimeoutError(string message)
        {
            return ControlError(
                ReachySimErrorCode.HandleBusy,
                ReachySimRecoverability.Retry,
                message);
        }

        private static ReachySimError ControlError(
            ReachySimErrorCode code,
            ReachySimRecoverability recoverability,
            string message)
        {
            return new ReachySimError(
                code,
                recoverability,
                message);
        }

        private static double TimestampDeltaSeconds(
            long startTimestamp,
            long endTimestamp)
        {
            return (endTimestamp - startTimestamp) /
                (double)Stopwatch.Frequency;
        }

        private static void ValidateTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Timeout must be positive.");
            }
        }

        private static long CreateDeadline(TimeSpan timeout)
        {
            long now = Stopwatch.GetTimestamp();
            double timeoutTicks = timeout.TotalSeconds *
                Stopwatch.Frequency;
            if (timeoutTicks >= long.MaxValue)
            {
                return long.MaxValue;
            }

            long additionalTicks = (long)Math.Ceiling(timeoutTicks);
            if (additionalTicks >= long.MaxValue - now)
            {
                return long.MaxValue;
            }
            return now + additionalTicks;
        }

        private static TimeSpan RemainingTime(long deadline)
        {
            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0L)
            {
                return TimeSpan.Zero;
            }
            return TimeSpan.FromSeconds(
                remainingTicks / (double)Stopwatch.Frequency);
        }

        private static bool WaitForPulse(
            object gate,
            long deadline)
        {
            TimeSpan remaining = RemainingTime(deadline);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            int milliseconds = remaining.TotalMilliseconds >= int.MaxValue
                ? int.MaxValue
                : Math.Max(
                    1,
                    (int)Math.Ceiling(remaining.TotalMilliseconds));
            return Monitor.Wait(gate, milliseconds);
        }
    }
}
