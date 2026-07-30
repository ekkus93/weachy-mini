#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public enum ReachySimulationRunState
    {
        Created = 0,
        Starting = 1,
        Running = 2,
        Paused = 3,
        Faulted = 4,
        Stopping = 5,
        Stopped = 6,
        Disposed = 7,
    }

    public enum ReachySimulationCommandEnqueueResult
    {
        Accepted = 0,
        QueueFull = 1,
        CommandTooLarge = 2,
        InvalidFormat = 3,
        WorkerUnavailable = 4,
    }

    public readonly struct ReachySimulationTimingSnapshot
    {
        internal ReachySimulationTimingSnapshot(
            ulong totalStepCount,
            ulong deadlineMissCount,
            ulong solverWarningCount,
            ulong commandQueueOverflowCount,
            ulong discardedCommandCount,
            double accumulatedLagSeconds,
            double lastStepDurationSeconds,
            double maximumStepDurationSeconds)
        {
            TotalStepCount = totalStepCount;
            DeadlineMissCount = deadlineMissCount;
            SolverWarningCount = solverWarningCount;
            CommandQueueOverflowCount = commandQueueOverflowCount;
            DiscardedCommandCount = discardedCommandCount;
            AccumulatedLagSeconds = accumulatedLagSeconds;
            LastStepDurationSeconds = lastStepDurationSeconds;
            MaximumStepDurationSeconds = maximumStepDurationSeconds;
        }

        public ulong TotalStepCount { get; }

        public ulong DeadlineMissCount { get; }

        public ulong SolverWarningCount { get; }

        public ulong CommandQueueOverflowCount { get; }

        public ulong DiscardedCommandCount { get; }

        public double AccumulatedLagSeconds { get; }

        public double LastStepDurationSeconds { get; }

        public double MaximumStepDurationSeconds { get; }
    }

    public readonly struct ReachyPublishedSimulationSnapshot
    {
        internal ReachyPublishedSimulationSnapshot(
            ulong publicationSequence,
            ReachySimStateSnapshot state,
            ReachySimulationTimingSnapshot timing)
        {
            PublicationSequence = publicationSequence;
            State = state;
            Timing = timing;
        }

        public ulong PublicationSequence { get; }

        public ReachySimStateSnapshot State { get; }

        public ReachySimulationTimingSnapshot Timing { get; }
    }

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
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            State = state;
            DiscardedCommandCount = discardedCommandCount;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimulationRunState State { get; }

        public int DiscardedCommandCount { get; }

        public ReachySimError Error { get; }

        internal static ReachySimulationControlResult Success(
            ReachySimulationRunState state,
            int discardedCommandCount = 0)
        {
            return new ReachySimulationControlResult(
                isSuccess: true,
                state,
                discardedCommandCount,
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
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }

    public sealed class ReachySimulationWorker : IDisposable
    {
        private const int CommandHeaderSize = 24;
        private const int MaximumCatchUpStepsPerCycle = 8;
        private const int FaultWaitMilliseconds = 100;
        private const uint MujocoWarningHealthFlag = 1U << 1;
        private const double TimestepSeconds =
            ProjectMetadata.InitialPhysicsTimestepSeconds;

        private readonly object controlGate = new object();
        private readonly ReachySimSession session;
        private readonly BoundedCommandQueue commandQueue;
        private readonly SnapshotPublicationBuffer snapshotBuffer =
            new SnapshotPublicationBuffer();
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
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            if (commandQueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commandQueueCapacity),
                    "Command queue capacity must be positive.");
            }
            if (maximumCommandBytes < CommandHeaderSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCommandBytes),
                    $"Maximum command size must be at least {CommandHeaderSize} bytes.");
            }

            commandQueue = new BoundedCommandQueue(
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
                timeout);
        }

        public ReachySimulationControlResult Resume(TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.Resume,
                resetId: 0U,
                timeout);
        }

        public ReachySimulationControlResult Reset(
            uint resetId,
            TimeSpan timeout)
        {
            return SubmitControlRequest(
                ControlRequestKind.Reset,
                resetId,
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
                    resetId);
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

        private enum ControlRequestKind
        {
            None = 0,
            Pause = 1,
            Resume = 2,
            Reset = 3,
            Shutdown = 4,
        }

        private readonly struct ControlRequest
        {
            internal ControlRequest(
                long id,
                ControlRequestKind kind,
                uint resetId)
            {
                Id = id;
                Kind = kind;
                ResetId = resetId;
            }

            internal long Id { get; }

            internal ControlRequestKind Kind { get; }

            internal uint ResetId { get; }
        }

        private sealed class BoundedCommandQueue
        {
            private readonly object gate = new object();
            private readonly byte[][] buffers;
            private readonly int[] lengths;
            private readonly int maximumCommandBytes;
            private int readIndex;
            private int writeIndex;
            private int count;

            internal BoundedCommandQueue(
                int capacity,
                int maximumCommandBytes)
            {
                buffers = new byte[capacity][];
                lengths = new int[capacity];
                this.maximumCommandBytes = maximumCommandBytes;
                for (int index = 0; index < capacity; ++index)
                {
                    buffers[index] = new byte[maximumCommandBytes];
                }
            }

            internal ReachySimulationCommandEnqueueResult Enqueue(
                byte[] commandBatch)
            {
                if (commandBatch.Length > maximumCommandBytes)
                {
                    return ReachySimulationCommandEnqueueResult.CommandTooLarge;
                }
                if (!ValidateCommandBatch(commandBatch))
                {
                    return ReachySimulationCommandEnqueueResult.InvalidFormat;
                }

                lock (gate)
                {
                    if (count == buffers.Length)
                    {
                        return ReachySimulationCommandEnqueueResult.QueueFull;
                    }

                    Buffer.BlockCopy(
                        commandBatch,
                        0,
                        buffers[writeIndex],
                        0,
                        commandBatch.Length);
                    lengths[writeIndex] = commandBatch.Length;
                    writeIndex = (writeIndex + 1) % buffers.Length;
                    ++count;
                    return ReachySimulationCommandEnqueueResult.Accepted;
                }
            }

            internal bool TryCopyNext(
                IntPtr destination,
                int destinationCapacity,
                out int byteCount)
            {
                lock (gate)
                {
                    if (count == 0)
                    {
                        byteCount = 0;
                        return false;
                    }

                    byteCount = lengths[readIndex];
                    if (byteCount > destinationCapacity)
                    {
                        throw new InvalidOperationException(
                            $"Queued command size {byteCount} exceeds destination capacity {destinationCapacity}.");
                    }

                    Marshal.Copy(
                        buffers[readIndex],
                        0,
                        destination,
                        byteCount);
                    lengths[readIndex] = 0;
                    readIndex = (readIndex + 1) % buffers.Length;
                    --count;
                    return true;
                }
            }

            internal int Clear()
            {
                lock (gate)
                {
                    int discarded = count;
                    Array.Clear(lengths, 0, lengths.Length);
                    readIndex = 0;
                    writeIndex = 0;
                    count = 0;
                    return discarded;
                }
            }

            private static bool ValidateCommandBatch(byte[] bytes)
            {
                if (!BitConverter.IsLittleEndian ||
                    bytes.Length < CommandHeaderSize)
                {
                    return false;
                }

                uint abiVersion = BitConverter.ToUInt32(bytes, 0);
                uint structureSize = BitConverter.ToUInt32(bytes, 4);
                uint declaredByteCount = BitConverter.ToUInt32(bytes, 20);
                return abiVersion == ProjectMetadata.NativeAbiVersion &&
                    structureSize == CommandHeaderSize &&
                    declaredByteCount == checked((uint)bytes.Length);
            }
        }

        private sealed class SnapshotPublicationBuffer
        {
            private readonly SnapshotSlot[] slots =
            {
                new SnapshotSlot(),
                new SnapshotSlot(),
                new SnapshotSlot(),
            };
            private int publishedIndex = -1;
            private int writerIndex = -1;

            internal void Publish(
                ReachyPublishedSimulationSnapshot snapshot)
            {
                int nextIndex = (writerIndex + 1) % slots.Length;
                SnapshotSlot slot = slots[nextIndex];
                long version = Volatile.Read(ref slot.Version);
                if ((version & 1L) != 0L)
                {
                    ++version;
                }

                Volatile.Write(ref slot.Version, checked(version + 1L));
                slot.Value = snapshot;
                Volatile.Write(ref slot.Version, checked(version + 2L));
                writerIndex = nextIndex;
                Volatile.Write(ref publishedIndex, nextIndex);
            }

            internal bool TryRead(
                out ReachyPublishedSimulationSnapshot snapshot)
            {
                for (int attempt = 0; attempt < 8; ++attempt)
                {
                    int index = Volatile.Read(ref publishedIndex);
                    if (index < 0)
                    {
                        snapshot = default;
                        return false;
                    }

                    SnapshotSlot slot = slots[index];
                    long before = Volatile.Read(ref slot.Version);
                    if ((before & 1L) != 0L)
                    {
                        Thread.Yield();
                        continue;
                    }

                    ReachyPublishedSimulationSnapshot candidate = slot.Value;
                    long after = Volatile.Read(ref slot.Version);
                    if (before == after && (after & 1L) == 0L)
                    {
                        snapshot = candidate;
                        return true;
                    }
                }

                snapshot = default;
                return false;
            }

            private sealed class SnapshotSlot
            {
                internal long Version;
                internal ReachyPublishedSimulationSnapshot Value;
            }
        }
    }
}
