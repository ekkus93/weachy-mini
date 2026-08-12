#nullable enable

using System;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker
    {
        private ReachySimulationControlResult RecordStartupFailure(
            Exception exception)
        {
            ReachySimError error = ManagedFaultError(exception);
            lock (controlGate)
            {
                workerThread = null;
                fault = new ReachySimulationFault("startup", error);
                runState = ReachySimulationRunState.Faulted;
                Monitor.PulseAll(controlGate);
                return ReachySimulationControlResult.Failure(
                    runState,
                    error);
            }
        }

        private ReachySimulationControlResult SubmitControlRequest(
            ControlRequestKind kind,
            uint resetId,
            ReachySimSnapshot? snapshot,
            TimeSpan timeout)
        {
            ValidateTimeout(timeout);
            long deadline = CreateDeadline(timeout);
            long requestId;

            lock (controlGate)
            {
                ThrowIfDisposed();
                ReachySimulationControlResult? immediate =
                    ValidateControlRequest(kind);
                if (immediate != null)
                {
                    return immediate;
                }
                if (pendingRequest.Kind != ControlRequestKind.None ||
                    inFlightRequestId != 0L)
                {
                    return ReachySimulationControlResult.Failure(
                        runState,
                        ControlError(
                            ReachySimErrorCode.HandleBusy,
                            ReachySimRecoverability.Retry,
                            "Another simulation control request is already pending."));
                }

                requestId = checked(++nextRequestId);
                pendingRequest = new ControlRequest(
                    requestId,
                    kind,
                    resetId,
                    snapshot);
                wakeSignal.Set();

                while (completedRequestId < requestId)
                {
                    if (!WaitForPulse(controlGate, deadline))
                    {
                        return ReachySimulationControlResult.Failure(
                            runState,
                            TimeoutError(
                                $"Simulation {kind} request timed out; the request remains visible and may still complete."));
                    }
                }

                return completedRequestResult ??
                    ReachySimulationControlResult.Failure(
                        runState,
                        ControlError(
                            ReachySimErrorCode.ManagedInteropFailure,
                            ReachySimRecoverability.RecreateHandle,
                            "Simulation control completed without a result."));
            }
        }

        private ReachySimulationControlResult? ValidateControlRequest(
            ControlRequestKind kind)
        {
            switch (kind)
            {
                case ControlRequestKind.Pause:
                    if (runState == ReachySimulationRunState.Paused)
                    {
                        return ReachySimulationControlResult.Success(runState);
                    }
                    if (runState != ReachySimulationRunState.Running)
                    {
                        return InvalidStateResult(kind);
                    }
                    break;
                case ControlRequestKind.Resume:
                    if (runState == ReachySimulationRunState.Running)
                    {
                        return ReachySimulationControlResult.Success(runState);
                    }
                    if (runState != ReachySimulationRunState.Paused)
                    {
                        return InvalidStateResult(kind);
                    }
                    break;
                case ControlRequestKind.Reset:
                    if (runState != ReachySimulationRunState.Running &&
                        runState != ReachySimulationRunState.Paused)
                    {
                        return InvalidStateResult(kind);
                    }
                    break;
                case ControlRequestKind.CaptureSnapshot:
                case ControlRequestKind.RestoreSnapshot:
                    if (runState != ReachySimulationRunState.Paused)
                    {
                        return InvalidStateResult(kind);
                    }
                    break;
                case ControlRequestKind.Shutdown:
                    if (runState == ReachySimulationRunState.Stopped)
                    {
                        return ReachySimulationControlResult.Success(runState);
                    }
                    if (runState != ReachySimulationRunState.Starting &&
                        runState != ReachySimulationRunState.Running &&
                        runState != ReachySimulationRunState.Paused &&
                        runState != ReachySimulationRunState.Faulted)
                    {
                        return InvalidStateResult(kind);
                    }
                    break;
                default:
                    return ReachySimulationControlResult.Failure(
                        runState,
                        ControlError(
                            ReachySimErrorCode.InvalidArgument,
                            ReachySimRecoverability.FatalConfiguration,
                            $"Unsupported control request {kind}."));
            }

            return null;
        }

        private ReachySimulationControlResult InvalidStateResult(
            ControlRequestKind kind)
        {
            return ReachySimulationControlResult.Failure(
                runState,
                fault?.Error ?? ControlError(
                    ReachySimErrorCode.InvalidArgument,
                    ReachySimRecoverability.FatalConfiguration,
                    $"Cannot process {kind} while the simulation is {runState}."));
        }

        private bool TryTakePendingRequest(out ControlRequest request)
        {
            lock (controlGate)
            {
                if (pendingRequest.Kind == ControlRequestKind.None)
                {
                    request = default;
                    return false;
                }

                request = pendingRequest;
                pendingRequest = default;
                inFlightRequestId = request.Id;
                inFlightRequestKind = request.Kind;
                return true;
            }
        }

        private void CompleteRequest(
            long requestId,
            ReachySimulationControlResult result)
        {
            lock (controlGate)
            {
                CompleteRequestLocked(requestId, result);
            }
        }

        private void CompleteRequestLocked(
            long requestId,
            ReachySimulationControlResult result)
        {
            if (inFlightRequestId != requestId)
            {
                throw new InvalidOperationException(
                    $"Control request completion mismatch: expected {inFlightRequestId}, received {requestId}.");
            }

            completedRequestId = requestId;
            completedRequestResult = result;
            inFlightRequestId = 0L;
            inFlightRequestKind = ControlRequestKind.None;
            Monitor.PulseAll(controlGate);
        }

        private void CompleteNonShutdownRequestAfterFault()
        {
            lock (controlGate)
            {
                if (inFlightRequestId == 0L ||
                    inFlightRequestKind == ControlRequestKind.Shutdown)
                {
                    return;
                }

                CompleteRequestLocked(
                    inFlightRequestId,
                    ReachySimulationControlResult.Failure(
                        runState,
                        fault?.Error ?? ControlError(
                            ReachySimErrorCode.ManagedInteropFailure,
                            ReachySimRecoverability.RecreateHandle,
                            "Simulation control failed because the worker faulted.")));
            }
        }

        private enum ControlRequestKind
        {
            None = 0,
            Pause = 1,
            Resume = 2,
            Reset = 3,
            CaptureSnapshot = 4,
            RestoreSnapshot = 5,
            Shutdown = 6,
        }

        private readonly struct ControlRequest
        {
            internal ControlRequest(
                long id,
                ControlRequestKind kind,
                uint resetId,
                ReachySimSnapshot? snapshot)
            {
                Id = id;
                Kind = kind;
                ResetId = resetId;
                Snapshot = snapshot;
            }

            internal long Id { get; }

            internal ControlRequestKind Kind { get; }

            internal uint ResetId { get; }

            internal ReachySimSnapshot? Snapshot { get; }
        }
    }
}
