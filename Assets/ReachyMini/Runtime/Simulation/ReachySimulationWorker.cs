#nullable enable

using System;
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

    }
}
