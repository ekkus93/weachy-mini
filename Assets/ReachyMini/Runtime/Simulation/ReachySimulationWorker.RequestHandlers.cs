#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker
    {
        private void ProcessResetRequest(ControlRequest request)
        {
            int discarded = commandQueue.Clear();
            if (discarded > 0)
            {
                Interlocked.Add(ref discardedCommandCount, discarded);
            }

            int status = session.ResetRaw(request.ResetId);
            if (status != (int)ReachySimErrorCode.Ok)
            {
                ReachySimError error = session.GetErrorForStatus(status);
                EnterNativeFault("reset", error);
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(
                        ReachySimulationRunState.Faulted,
                        error));
                return;
            }

            lastStepDurationSeconds = 0.0;
            accumulatedLagSeconds = 0.0;
            if (!PublishCurrentState(stepDurationSeconds: 0.0))
            {
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(
                        ReachySimulationRunState.Faulted,
                        Fault?.Error ?? ControlError(
                            ReachySimErrorCode.ManagedInteropFailure,
                            ReachySimRecoverability.RecreateHandle,
                            "Reset state publication failed.")));
                return;
            }

            CompleteRequest(
                request.Id,
                ReachySimulationControlResult.Success(
                    State,
                    discarded));
        }

        private void ProcessCaptureSnapshotRequest(ControlRequest request)
        {
            ReachySimSnapshotCaptureResult capture = session.CaptureSnapshot();
            ReachySimSnapshot? snapshot = capture.Snapshot;
            if (!capture.IsSuccess || snapshot == null)
            {
                ReachySimError error = capture.IsSuccess
                    ? ControlError(
                        ReachySimErrorCode.ManagedInteropFailure,
                        ReachySimRecoverability.RecreateHandle,
                        "Snapshot capture succeeded without returning snapshot bytes.")
                    : capture.Error;
                RetainTerminalSnapshotFault("capture snapshot", error);
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(State, error));
                return;
            }

            CompleteRequest(
                request.Id,
                ReachySimulationControlResult.Success(
                    State,
                    capturedSnapshot: snapshot));
        }

        private void ProcessRestoreSnapshotRequest(ControlRequest request)
        {
            ReachySimSnapshot? snapshot = request.Snapshot;
            if (snapshot == null)
            {
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(
                        State,
                        ControlError(
                            ReachySimErrorCode.InvalidArgument,
                            ReachySimRecoverability.FatalConfiguration,
                            "Snapshot restore request did not contain snapshot bytes.")));
                return;
            }

            ReachySimOperationResult restore = session.RestoreSnapshot(snapshot);
            if (!restore.IsSuccess)
            {
                RetainTerminalSnapshotFault("restore snapshot", restore.Error);
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(
                        State,
                        restore.Error));
                return;
            }

            int discarded = commandQueue.Clear();
            if (discarded > 0)
            {
                Interlocked.Add(ref discardedCommandCount, discarded);
            }

            lastStepDurationSeconds = 0.0;
            accumulatedLagSeconds = 0.0;
            if (!PublishCurrentState(stepDurationSeconds: 0.0))
            {
                CompleteRequest(
                    request.Id,
                    ReachySimulationControlResult.Failure(
                        ReachySimulationRunState.Faulted,
                        Fault?.Error ?? ControlError(
                            ReachySimErrorCode.ManagedInteropFailure,
                            ReachySimRecoverability.RecreateHandle,
                            "Restored state publication failed.")));
                return;
            }

            CompleteRequest(
                request.Id,
                ReachySimulationControlResult.Success(
                    ReachySimulationRunState.Paused,
                    discarded));
        }

        private void RetainTerminalSnapshotFault(
            string operation,
            ReachySimError error)
        {
            if (error.Recoverability == ReachySimRecoverability.RecreateHandle ||
                error.Recoverability == ReachySimRecoverability.FatalConfiguration)
            {
                EnterNativeFault(operation, error);
            }
        }

        private bool ApplyQueuedCommandsAtBoundary()
        {
            while (commandQueue.TryCopyNext(
                commandBuffer,
                commandBufferSize,
                out int byteCount))
            {
                int status = session.SubmitCommandsRaw(
                    commandBuffer,
                    byteCount);
                if (status != (int)ReachySimErrorCode.Ok)
                {
                    EnterNativeFault(
                        "submit commands",
                        session.GetErrorForStatus(status));
                    return false;
                }
            }

            return true;
        }

        private bool PublishCurrentState(double stepDurationSeconds)
        {
            int status = session.CopyStateRaw(
                stateBuffer,
                stateBufferSize,
                out int requiredSize);
            if (status != (int)ReachySimErrorCode.Ok)
            {
                EnterNativeFault(
                    "copy state",
                    session.GetErrorForStatus(status));
                return false;
            }
            if (requiredSize != stateBufferSize)
            {
                EnterNativeFault(
                    "copy state",
                    ControlError(
                        ReachySimErrorCode.StructSizeMismatch,
                        ReachySimRecoverability.FatalConfiguration,
                        $"Native state size {requiredSize} does not match managed header size {stateBufferSize}."));
                return false;
            }

            NativeReachySimStateHeader nativeState =
                Marshal.PtrToStructure<NativeReachySimStateHeader>(
                    stateBuffer);
            ReachySimStateSnapshot state =
                ReachySimStateSnapshot.FromNative(nativeState);

            IReachySimAuthoritativeStateReader? stateReader =
                authoritativeStateReader;
            ReachySimAuthoritativeStateFrame? stateFrame =
                authoritativeStateFrame;
            if (stateReader != null && stateFrame != null)
            {
                lock (authoritativeStateGate)
                {
                    stateReader.Capture(stateFrame);
                    if (stateFrame.Sequence != state.Sequence ||
                        stateFrame.SimulationTime != state.SimulationTime)
                    {
                        EnterNativeFault(
                            "authoritative state publication",
                            ControlError(
                                ReachySimErrorCode.NumericFailure,
                                ReachySimRecoverability.RecreateHandle,
                                "Legacy and authoritative state captures disagree on sequence or simulation time."));
                        return false;
                    }
                    hasAuthoritativeState = true;
                }
            }

            solverWarningCount = CountNewSolverWarningEpisodes(
                solverWarningCount,
                previousHealthFlags,
                state.HealthFlags);
            previousHealthFlags = state.HealthFlags;

            lastStepDurationSeconds = stepDurationSeconds;
            ReachySimulationTimingSnapshot timing =
                new ReachySimulationTimingSnapshot(
                    totalStepCount,
                    deadlineMissCount,
                    solverWarningCount,
                    checked((ulong)Math.Max(
                        0L,
                        Interlocked.Read(ref commandQueueOverflowCount))),
                    checked((ulong)Math.Max(
                        0L,
                        Interlocked.Read(ref discardedCommandCount))),
                    accumulatedLagSeconds,
                    lastStepDurationSeconds,
                    maximumStepDurationSeconds);
            ReachyPublishedSimulationSnapshot published =
                new ReachyPublishedSimulationSnapshot(
                    checked(++publicationSequence),
                    state,
                    timing);
            snapshotBuffer.Publish(published);
            return true;
        }

        internal static ulong CountNewSolverWarningEpisodes(
            ulong currentCount,
            uint previousHealthFlags,
            uint currentHealthFlags)
        {
            bool warningWasActive =
                (previousHealthFlags & MujocoWarningHealthFlag) != 0U;
            bool warningIsActive =
                (currentHealthFlags & MujocoWarningHealthFlag) != 0U;
            return warningIsActive && !warningWasActive
                ? checked(currentCount + 1UL)
                : currentCount;
        }
    }
}
