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
