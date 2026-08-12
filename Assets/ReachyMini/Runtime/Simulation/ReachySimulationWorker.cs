#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public sealed partial class ReachySimulationWorker : IDisposable,
        IReachyPublishedAuthoritativeStateSource,
        IReachySimulationTimingSource
    {
        private const int MaximumCatchUpStepsPerCycle = 8;
        private const int FaultWaitMilliseconds = 100;
        private const uint MujocoWarningHealthFlag = 1U << 1;
        private const double TimestepSeconds =
            ProjectMetadata.InitialPhysicsTimestepSeconds;

        private readonly object controlGate = new object();
        private readonly object authoritativeStateGate = new object();
        private readonly ReachySimSession session;
        private readonly IReachySimAuthoritativeStateReader? authoritativeStateReader;
        private readonly ReachySimAuthoritativeStateFrame? authoritativeStateFrame;
        private readonly ReachySimulationBoundedCommandQueue commandQueue;
        private readonly ReachySimulationSnapshotPublicationBuffer snapshotBuffer =
            new ReachySimulationSnapshotPublicationBuffer();
        private readonly AutoResetEvent wakeSignal = new AutoResetEvent(false);
        private readonly IntPtr stateBuffer;
        private readonly int stateBufferSize;
        private readonly IntPtr commandBuffer;
        private readonly int commandBufferSize;

        private Thread? workerThread;
        private ReachySimulationRunState runState =
            ReachySimulationRunState.Created;
        private ReachySimulationFault? fault;
        private ControlRequest pendingRequest;
        private long nextRequestId;
        private long inFlightRequestId;
        private ControlRequestKind inFlightRequestKind;
        private long completedRequestId;
        private ReachySimulationControlResult? completedRequestResult;
        private bool disposed;
        private bool unmanagedBuffersFreed;
        private bool hasAuthoritativeState;

        private ulong publicationSequence;
        private ulong totalStepCount;
        private ulong deadlineMissCount;
        private ulong solverWarningCount;
        private long commandQueueOverflowCount;
        private long discardedCommandCount;
        private double accumulatedLagSeconds;
        private double lastStepDurationSeconds;
        private double maximumStepDurationSeconds;
        private uint previousHealthFlags;

        public ReachySimulationWorker(
            ReachySimSession session,
            int commandQueueCapacity = 64,
            int maximumCommandBytes = 1024 * 1024)
            : this(
                session,
                authoritativeStateReader: null,
                commandQueueCapacity,
                maximumCommandBytes,
                useAuthoritativeStateReader: false)
        {
        }

        public ReachySimulationWorker(
            ReachySimSession session,
            IReachySimAuthoritativeStateReader authoritativeStateReader,
            int commandQueueCapacity = 64,
            int maximumCommandBytes = 1024 * 1024)
            : this(
                session,
                authoritativeStateReader ??
                    throw new ArgumentNullException(nameof(authoritativeStateReader)),
                commandQueueCapacity,
                maximumCommandBytes,
                useAuthoritativeStateReader: true)
        {
        }

        private ReachySimulationWorker(
            ReachySimSession session,
            IReachySimAuthoritativeStateReader? authoritativeStateReader,
            int commandQueueCapacity,
            int maximumCommandBytes,
            bool useAuthoritativeStateReader)
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            this.authoritativeStateReader = useAuthoritativeStateReader
                ? authoritativeStateReader
                : null;
            authoritativeStateFrame = this.authoritativeStateReader?.CreateFrame();
            if (commandQueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commandQueueCapacity),
                    "Command queue capacity must be positive.");
            }
            if (maximumCommandBytes < ReachySimulationBoundedCommandQueue.CommandHeaderSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCommandBytes),
                    $"Maximum command size must be at least {ReachySimulationBoundedCommandQueue.CommandHeaderSize} bytes.");
            }

            commandQueue = new ReachySimulationBoundedCommandQueue(
                commandQueueCapacity,
                maximumCommandBytes);
            stateBufferSize = Marshal.SizeOf<NativeReachySimStateHeader>();
            stateBuffer = Marshal.AllocHGlobal(stateBufferSize);
            try
            {
                commandBufferSize = maximumCommandBytes;
                commandBuffer = Marshal.AllocHGlobal(commandBufferSize);
            }
            catch (OutOfMemoryException)
            {
                Marshal.FreeHGlobal(stateBuffer);
                throw;
            }
        }

        public ReachySimulationRunState SimulationRunState => State;

        public ReachySimulationRunState State
        {
            get
            {
                lock (controlGate)
                {
                    return runState;
                }
            }
        }

        public ReachySimulationFault? Fault
        {
            get
            {
                lock (controlGate)
                {
                    return fault;
                }
            }
        }

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

        public ReachySimulationControlResult Start(TimeSpan timeout)
        {
            ValidateTimeout(timeout);
            Thread thread;
            lock (controlGate)
            {
                ThrowIfDisposed();
                if (runState != ReachySimulationRunState.Created)
                {
                    return ReachySimulationControlResult.Failure(
                        runState,
                        ControlError(
                            ReachySimErrorCode.InvalidArgument,
                            ReachySimRecoverability.FatalConfiguration,
                            $"Cannot start a worker in state {runState}."));
                }

                runState = ReachySimulationRunState.Starting;
                thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "Reachy authoritative simulation",
                };
                workerThread = thread;
            }

            try
            {
                thread.Start();
            }
            catch (ThreadStateException exception)
            {
                return RecordStartupFailure(exception);
            }
            catch (OutOfMemoryException exception)
            {
                return RecordStartupFailure(exception);
            }

            long deadline = CreateDeadline(timeout);
            lock (controlGate)
            {
                while (runState == ReachySimulationRunState.Starting)
                {
                    if (!WaitForPulse(controlGate, deadline))
                    {
                        return ReachySimulationControlResult.Failure(
                            runState,
                            TimeoutError("Simulation worker startup timed out."));
                    }
                }

                if (runState == ReachySimulationRunState.Running)
                {
                    return ReachySimulationControlResult.Success(runState);
                }

                return ReachySimulationControlResult.Failure(
                    runState,
                    fault?.Error ?? ControlError(
                        ReachySimErrorCode.ManagedInteropFailure,
                        ReachySimRecoverability.RecreateHandle,
                        $"Simulation worker entered unexpected state {runState} during startup."));
            }
        }

        public ReachySimulationControlResult Pause(TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.Pause,
                resetId: 0U,
                snapshot: null,
                timeout);
        }

        public ReachySimulationControlResult Resume(TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.Resume,
                resetId: 0U,
                snapshot: null,
                timeout);
        }

        public ReachySimulationControlResult Reset(
            ReachySimResetPose resetPose,
            TimeSpan timeout)
        {
            return Reset((uint)resetPose, timeout);
        }

        public ReachySimulationControlResult Reset(
            uint resetId,
            TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.Reset,
                resetId,
                snapshot: null,
                timeout);
        }

        public ReachySimulationControlResult CaptureSnapshot(TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.CaptureSnapshot,
                resetId: 0U,
                snapshot: null,
                timeout);
        }

        public ReachySimulationControlResult RestoreSnapshot(
            ReachySimSnapshot snapshot,
            TimeSpan timeout)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return SubmitControlRequest(
                ControlRequestKind.RestoreSnapshot,
                resetId: 0U,
                snapshot,
                timeout);
        }

        public ReachySimulationControlResult Shutdown(TimeSpan timeout)
        {
            ValidateTimeout(timeout);
            long deadline = CreateDeadline(timeout);

            lock (controlGate)
            {
                ThrowIfDisposed();
                if (runState == ReachySimulationRunState.Stopped)
                {
                    return ReachySimulationControlResult.Success(runState);
                }
                if (runState == ReachySimulationRunState.Created)
                {
                    runState = ReachySimulationRunState.Stopping;
                }
            }

            if (workerThread == null)
            {
                ReachySimOperationResult closeResult = session.Close();
                lock (controlGate)
                {
                    if (!closeResult.IsSuccess)
                    {
                        fault = new ReachySimulationFault(
                            "shutdown",
                            closeResult.Error);
                    }
                    runState = ReachySimulationRunState.Stopped;
                    Monitor.PulseAll(controlGate);
                    return closeResult.IsSuccess
                        ? ReachySimulationControlResult.Success(runState)
                        : ReachySimulationControlResult.Failure(
                            runState,
                            closeResult.Error);
                }
            }

            TimeSpan requestTime = RemainingTime(deadline);
            if (requestTime <= TimeSpan.Zero)
            {
                return ReachySimulationControlResult.Failure(
                    State,
                    TimeoutError(
                        "Simulation shutdown deadline expired before the request could be submitted."));
            }

            ReachySimulationControlResult requestResult =
                SubmitControlRequest(
                    ControlRequestKind.Shutdown,
                    resetId: 0U,
                    snapshot: null,
                    requestTime);
            if (!requestResult.IsSuccess)
            {
                return requestResult;
            }

            Thread? thread = workerThread;
            if (thread != null && thread.IsAlive)
            {
                TimeSpan remaining = RemainingTime(deadline);
                if (remaining <= TimeSpan.Zero ||
                    !thread.Join(remaining))
                {
                    return ReachySimulationControlResult.Failure(
                        State,
                        TimeoutError(
                            "Simulation worker did not terminate before the shutdown deadline."));
                }
            }

            return ReachySimulationControlResult.Success(State);
        }

        public ReachySimulationCommandEnqueueResult EnqueueCommandBatch(
            byte[] commandBatch)
        {
            if (commandBatch == null)
            {
                throw new ArgumentNullException(nameof(commandBatch));
            }

            ReachySimulationRunState currentState = State;
            if (currentState != ReachySimulationRunState.Running &&
                currentState != ReachySimulationRunState.Paused)
            {
                return ReachySimulationCommandEnqueueResult.WorkerUnavailable;
            }

            ReachySimulationCommandEnqueueResult result =
                commandQueue.Enqueue(commandBatch);
            if (result == ReachySimulationCommandEnqueueResult.QueueFull)
            {
                Interlocked.Increment(ref commandQueueOverflowCount);
            }
            if (result == ReachySimulationCommandEnqueueResult.Accepted)
            {
                wakeSignal.Set();
            }
            return result;
        }

        public bool TryGetLatestSnapshot(
            out ReachyPublishedSimulationSnapshot snapshot)
        {
            return snapshotBuffer.TryRead(out snapshot);
        }

        public bool TryGetLatestTimingSnapshot(
            out ReachySimulationTimingSnapshot timing)
        {
            if (TryGetLatestSnapshot(out ReachyPublishedSimulationSnapshot snapshot))
            {
                timing = snapshot.Timing;
                return true;
            }
            timing = default;
            return false;
        }

        public void Dispose()
        {
            lock (controlGate)
            {
                if (disposed)
                {
                    return;
                }
            }

            ReachySimulationControlResult shutdownResult = Shutdown(
                TimeSpan.FromSeconds(10.0));
            if (!shutdownResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Simulation worker shutdown failed: {shutdownResult.Error.Code}: {shutdownResult.Error.Message}");
            }

            authoritativeStateReader?.Dispose();
            session.Dispose();
            lock (controlGate)
            {
                disposed = true;
                runState = ReachySimulationRunState.Disposed;
                Monitor.PulseAll(controlGate);
            }

            FreeUnmanagedBuffers();
            wakeSignal.Dispose();
            GC.SuppressFinalize(this);
        }

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

        private void FreeUnmanagedBuffers()
        {
            lock (controlGate)
            {
                if (unmanagedBuffersFreed)
                {
                    return;
                }
                unmanagedBuffersFreed = true;
            }

            Marshal.FreeHGlobal(commandBuffer);
            Marshal.FreeHGlobal(stateBuffer);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachySimulationWorker));
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
