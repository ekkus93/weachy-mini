#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker
    {
        private void Run()
        {
            long shutdownRequestId = 0L;
            try
            {
                if (!PublishCurrentState(stepDurationSeconds: 0.0))
                {
                    WaitForShutdownAfterFault(ref shutdownRequestId);
                    return;
                }

                SetRunState(ReachySimulationRunState.Running);
                RunFixedStepLoop(ref shutdownRequestId);
            }
            catch (ObjectDisposedException exception)
            {
                EnterManagedFault("worker loop", exception);
                CompleteNonShutdownRequestAfterFault();
                WaitForShutdownAfterFault(ref shutdownRequestId);
            }
            catch (InvalidOperationException exception)
            {
                EnterManagedFault("worker loop", exception);
                CompleteNonShutdownRequestAfterFault();
                WaitForShutdownAfterFault(ref shutdownRequestId);
            }
            catch (ExternalException exception)
            {
                EnterManagedFault("worker loop", exception);
                CompleteNonShutdownRequestAfterFault();
                WaitForShutdownAfterFault(ref shutdownRequestId);
            }
            finally
            {
                ReachySimOperationResult closeResult = session.Close();
                if (!closeResult.IsSuccess)
                {
                    EnterNativeFault("shutdown", closeResult.Error);
                }

                lock (controlGate)
                {
                    runState = ReachySimulationRunState.Stopped;
                    if (shutdownRequestId != 0L)
                    {
                        CompleteRequestLocked(
                            shutdownRequestId,
                            closeResult.IsSuccess
                                ? ReachySimulationControlResult.Success(runState)
                                : ReachySimulationControlResult.Failure(
                                    runState,
                                    closeResult.Error));
                    }
                    Monitor.PulseAll(controlGate);
                }
            }
        }

        private void RunFixedStepLoop(ref long shutdownRequestId)
        {
            double accumulatorSeconds = 0.0;
            long previousTimestamp = Stopwatch.GetTimestamp();
            bool stopRequested = false;

            while (!stopRequested)
            {
                ProcessControlRequest(
                    ref accumulatorSeconds,
                    ref previousTimestamp,
                    ref stopRequested,
                    ref shutdownRequestId);
                if (stopRequested)
                {
                    break;
                }

                ReachySimulationRunState currentState = State;
                if (currentState == ReachySimulationRunState.Paused ||
                    currentState == ReachySimulationRunState.Faulted)
                {
                    wakeSignal.WaitOne(FaultWaitMilliseconds);
                    previousTimestamp = Stopwatch.GetTimestamp();
                    accumulatorSeconds = 0.0;
                    continue;
                }

                long currentTimestamp = Stopwatch.GetTimestamp();
                double elapsedSeconds = TimestampDeltaSeconds(
                    previousTimestamp,
                    currentTimestamp);
                previousTimestamp = currentTimestamp;
                if (elapsedSeconds < 0.0 ||
                    double.IsNaN(elapsedSeconds) ||
                    double.IsInfinity(elapsedSeconds))
                {
                    EnterNativeFault(
                        "monotonic clock",
                        ControlError(
                            ReachySimErrorCode.NumericFailure,
                            ReachySimRecoverability.RecreateHandle,
                            "The monotonic clock produced an invalid elapsed duration."));
                    continue;
                }

                accumulatorSeconds += elapsedSeconds;
                if (accumulatorSeconds < TimestepSeconds)
                {
                    WaitForNextDeadline(accumulatorSeconds);
                    continue;
                }

                int cycleStepCount = 0;
                bool deadlineMissRecordedThisCycle = false;
                while (accumulatorSeconds >= TimestepSeconds &&
                    cycleStepCount < MaximumCatchUpStepsPerCycle)
                {
                    ProcessControlRequest(
                        ref accumulatorSeconds,
                        ref previousTimestamp,
                        ref stopRequested,
                        ref shutdownRequestId);
                    if (stopRequested ||
                        State != ReachySimulationRunState.Running)
                    {
                        break;
                    }

                    if (!ApplyQueuedCommandsAtBoundary())
                    {
                        break;
                    }

                    long stepStartTimestamp = Stopwatch.GetTimestamp();
                    int stepStatus = session.StepRaw(1U);
                    long stepEndTimestamp = Stopwatch.GetTimestamp();
                    double stepDurationSeconds = TimestampDeltaSeconds(
                        stepStartTimestamp,
                        stepEndTimestamp);
                    if (stepStatus != (int)ReachySimErrorCode.Ok)
                    {
                        EnterNativeFault(
                            "step",
                            session.GetErrorForStatus(stepStatus));
                        break;
                    }
                    if (stepDurationSeconds < 0.0 ||
                        double.IsNaN(stepDurationSeconds) ||
                        double.IsInfinity(stepDurationSeconds))
                    {
                        EnterNativeFault(
                            "step timing",
                            ControlError(
                                ReachySimErrorCode.NumericFailure,
                                ReachySimRecoverability.RecreateHandle,
                                "The monotonic clock produced an invalid step duration."));
                        break;
                    }
                    if (stepDurationSeconds > TimestepSeconds)
                    {
                        ++deadlineMissCount;
                        deadlineMissRecordedThisCycle = true;
                    }

                    lastStepDurationSeconds = stepDurationSeconds;
                    if (stepDurationSeconds > maximumStepDurationSeconds)
                    {
                        maximumStepDurationSeconds = stepDurationSeconds;
                    }
                    ++totalStepCount;
                    accumulatorSeconds -= TimestepSeconds;
                    accumulatedLagSeconds = Math.Max(
                        0.0,
                        accumulatorSeconds);

                    if (!PublishCurrentState(stepDurationSeconds))
                    {
                        break;
                    }

                    ++cycleStepCount;
                }

                if (accumulatorSeconds >= TimestepSeconds &&
                    State == ReachySimulationRunState.Running)
                {
                    if (!deadlineMissRecordedThisCycle)
                    {
                        ++deadlineMissCount;
                    }
                    accumulatedLagSeconds = accumulatorSeconds;
                    Thread.Yield();
                }
            }
        }

        private void ProcessControlRequest(
            ref double accumulatorSeconds,
            ref long previousTimestamp,
            ref bool stopRequested,
            ref long shutdownRequestId)
        {
            if (!TryTakePendingRequest(out ControlRequest request))
            {
                return;
            }

            switch (request.Kind)
            {
                case ControlRequestKind.Pause:
                    accumulatorSeconds = 0.0;
                    previousTimestamp = Stopwatch.GetTimestamp();
                    SetRunState(ReachySimulationRunState.Paused);
                    CompleteRequest(
                        request.Id,
                        ReachySimulationControlResult.Success(
                            ReachySimulationRunState.Paused));
                    break;
                case ControlRequestKind.Resume:
                    accumulatorSeconds = 0.0;
                    previousTimestamp = Stopwatch.GetTimestamp();
                    SetRunState(ReachySimulationRunState.Running);
                    CompleteRequest(
                        request.Id,
                        ReachySimulationControlResult.Success(
                            ReachySimulationRunState.Running));
                    break;
                case ControlRequestKind.Reset:
                    ProcessResetRequest(request);
                    accumulatorSeconds = 0.0;
                    previousTimestamp = Stopwatch.GetTimestamp();
                    break;
                case ControlRequestKind.CaptureSnapshot:
                    ProcessCaptureSnapshotRequest(request);
                    break;
                case ControlRequestKind.RestoreSnapshot:
                    ProcessRestoreSnapshotRequest(request);
                    accumulatorSeconds = 0.0;
                    previousTimestamp = Stopwatch.GetTimestamp();
                    break;
                case ControlRequestKind.Shutdown:
                    SetRunState(ReachySimulationRunState.Stopping);
                    stopRequested = true;
                    shutdownRequestId = request.Id;
                    break;
                default:
                    CompleteRequest(
                        request.Id,
                        ReachySimulationControlResult.Failure(
                            State,
                            ControlError(
                                ReachySimErrorCode.InvalidArgument,
                                ReachySimRecoverability.FatalConfiguration,
                                $"Unsupported control request {request.Kind}.")));
                    break;
            }
        }

        private void WaitForNextDeadline(double accumulatorSeconds)
        {
            double remainingSeconds = TimestepSeconds - accumulatorSeconds;
            int waitMilliseconds = (int)Math.Floor(
                remainingSeconds * 1000.0);
            if (waitMilliseconds > 0)
            {
                wakeSignal.WaitOne(Math.Min(waitMilliseconds, 10));
            }
            else
            {
                Thread.Yield();
            }
        }

        private void WaitForShutdownAfterFault(
            ref long shutdownRequestId)
        {
            bool stopRequested = false;
            double accumulatorSeconds = 0.0;
            long previousTimestamp = Stopwatch.GetTimestamp();
            while (!stopRequested)
            {
                ProcessControlRequest(
                    ref accumulatorSeconds,
                    ref previousTimestamp,
                    ref stopRequested,
                    ref shutdownRequestId);
                if (!stopRequested)
                {
                    wakeSignal.WaitOne(FaultWaitMilliseconds);
                }
            }
        }
    }
}
