from __future__ import annotations

from pathlib import Path

root = Path.cwd()


def replace_once(path: Path, old: str, new: str, description: str) -> None:
    text = path.read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise SystemExit(f"Could not locate {description}: found {text.count(old)} matches")
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


worker_path = root / "Assets/ReachyMini/Runtime/Simulation/ReachySimulationWorker.cs"
worker = worker_path.read_text(encoding="utf-8")
worker = worker.replace(
    "public sealed class ReachySimulationWorker : IDisposable",
    "public sealed class ReachySimulationWorker : IDisposable,\n        IReachyPublishedAuthoritativeStateSource",
    1,
)
worker = worker.replace(
    """        private readonly object controlGate = new object();
        private readonly ReachySimSession session;
        private readonly BoundedCommandQueue commandQueue;
""",
    """        private readonly object controlGate = new object();
        private readonly object authoritativeStateGate = new object();
        private readonly ReachySimSession session;
        private readonly IReachySimAuthoritativeStateReader? authoritativeStateReader;
        private readonly ReachySimAuthoritativeStateFrame? authoritativeStateFrame;
        private readonly BoundedCommandQueue commandQueue;
""",
    1,
)
worker = worker.replace(
    """        private bool disposed;
        private bool unmanagedBuffersFreed;

        private ulong publicationSequence;
""",
    """        private bool disposed;
        private bool unmanagedBuffersFreed;
        private bool hasAuthoritativeState;

        private ulong publicationSequence;
""",
    1,
)
constructor_start = worker.index("        public ReachySimulationWorker(\n")
constructor_end = worker.index("        public ReachySimulationRunState State\n", constructor_start)
constructors = """        public ReachySimulationWorker(
            ReachySimSession session,
            int commandQueueCapacity = 64,
            int maximumCommandBytes = 1024 * 1024)
            : this(
                session,
                authoritativeStateReader: null,
                commandQueueCapacity,
                maximumCommandBytes)
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
                maximumCommandBytes)
        {
        }

        private ReachySimulationWorker(
            ReachySimSession session,
            IReachySimAuthoritativeStateReader? authoritativeStateReader,
            int commandQueueCapacity,
            int maximumCommandBytes)
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            this.authoritativeStateReader = authoritativeStateReader;
            authoritativeStateFrame = authoritativeStateReader?.CreateFrame();
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

"""
worker = worker[:constructor_start] + constructors + worker[constructor_end:]
start_marker = "        public ReachySimulationControlResult Start(TimeSpan timeout)\n"
start_index = worker.index(start_marker)
authoritative_api = """        public ReachySimAuthoritativeStateLayout AuthoritativeStateLayout =>
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

"""
worker = worker[:start_index] + authoritative_api + worker[start_index:]
worker = worker.replace(
    """            session.Dispose();
            lock (controlGate)
""",
    """            authoritativeStateReader?.Dispose();
            session.Dispose();
            lock (controlGate)
""",
    1,
)
publish_start = worker.index("        private bool PublishCurrentState(double stepDurationSeconds)\n")
publish_end = worker.index("        internal static ulong CountNewSolverWarningEpisodes(\n", publish_start)
publish_method = """        private bool PublishCurrentState(double stepDurationSeconds)
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

"""
worker = worker[:publish_start] + publish_method + worker[publish_end:]
worker_path.write_text(worker, encoding="utf-8", newline="\n")

pose_path = root / "Assets/ReachyMini/Runtime/Rendering/ReachySimAuthoritativePoseSource.cs"
pose_path.write_text(
    """#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Interop;
using ReachyMini.Presentation;
using ReachyMini.Simulation;

namespace ReachyMini.Rendering
{
    public sealed class ReachySimAuthoritativePoseSource :
        IReachyAuthoritativePoseSource,
        IDisposable
    {
        private readonly IReachySimAuthoritativeStateReader? stateReader;
        private readonly IReachyPublishedAuthoritativeStateSource? publishedStateSource;
        private readonly bool ownsStateReader;
        private readonly ReachySimAuthoritativeStateLayout layout;
        private readonly string[] bodyNames;
        private readonly ReachySimAuthoritativeStateFrame stateFrame;
        private readonly ReachyAuthoritativePoseBuffer poseBuffer =
            new ReachyAuthoritativePoseBuffer();
        private bool hasPublishedState;
        private ulong lastSequence;
        private double lastSimulationTime;
        private uint lastContinuityId;
        private bool disposed;

        public ReachySimAuthoritativePoseSource(
            ReachySimSession session,
            IReadOnlyList<string> canonicalBodyNames)
            : this(
                new ReachySimAuthoritativeStateReader(
                    session ?? throw new ArgumentNullException(nameof(session))),
                publishedStateSource: null,
                canonicalBodyNames,
                ownsStateReader: true)
        {
        }

        public ReachySimAuthoritativePoseSource(
            IReachySimAuthoritativeStateReader stateReader,
            IReadOnlyList<string> canonicalBodyNames,
            bool ownsStateReader = false)
            : this(
                stateReader ?? throw new ArgumentNullException(nameof(stateReader)),
                publishedStateSource: null,
                canonicalBodyNames,
                ownsStateReader)
        {
        }

        public ReachySimAuthoritativePoseSource(
            IReachyPublishedAuthoritativeStateSource publishedStateSource,
            IReadOnlyList<string> canonicalBodyNames)
            : this(
                stateReader: null,
                publishedStateSource ??
                    throw new ArgumentNullException(nameof(publishedStateSource)),
                canonicalBodyNames,
                ownsStateReader: false)
        {
        }

        private ReachySimAuthoritativePoseSource(
            IReachySimAuthoritativeStateReader? stateReader,
            IReachyPublishedAuthoritativeStateSource? publishedStateSource,
            IReadOnlyList<string> canonicalBodyNames,
            bool ownsStateReader)
        {
            if ((stateReader == null) == (publishedStateSource == null))
            {
                throw new ArgumentException(
                    "Exactly one authoritative state source must be provided.");
            }
            this.stateReader = stateReader;
            this.publishedStateSource = publishedStateSource;
            this.ownsStateReader = ownsStateReader;
            layout = stateReader?.Layout ??
                publishedStateSource!.AuthoritativeStateLayout;
            if (canonicalBodyNames == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodyNames));
            }
            if (canonicalBodyNames.Count != layout.BodyPoseCount)
            {
                throw new ArgumentException(
                    $"The canonical body-name count {canonicalBodyNames.Count} " +
                    $"does not match the authoritative body-pose count " +
                    $"{layout.BodyPoseCount}.",
                    nameof(canonicalBodyNames));
            }

            bodyNames = new string[canonicalBodyNames.Count];
            for (int index = 0; index < bodyNames.Length; ++index)
            {
                string name = canonicalBodyNames[index];
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        $"Canonical body name {index} is missing.",
                        nameof(canonicalBodyNames));
                }
                bodyNames[index] = name;
            }
            stateFrame = stateReader?.CreateFrame() ??
                publishedStateSource!.CreateAuthoritativeStateFrame();
        }

        public ulong ModelHash => layout.ModelHash;

        public int BodyCount => bodyNames.Length;

        public static ReachySimAuthoritativePoseSource Bind(
            ReachyAuthoritativeRenderer renderer,
            ReachySimSession session,
            ReachyPresentationBody[] canonicalBodies)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }
            if (canonicalBodies == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodies));
            }

            string[] names = new string[canonicalBodies.Length];
            for (int index = 0; index < canonicalBodies.Length; ++index)
            {
                ReachyPresentationBody body = canonicalBodies[index] ??
                    throw new ArgumentException(
                        $"Canonical body binding {index} is null.",
                        nameof(canonicalBodies));
                if (body.BodyIndex != index)
                {
                    throw new ArgumentException(
                        $"Canonical body binding {index} declares index " +
                        $"{body.BodyIndex}.",
                        nameof(canonicalBodies));
                }
                names[index] = body.BodyName;
            }

            ReachySimAuthoritativePoseSource source =
                new ReachySimAuthoritativePoseSource(session, names);
            try
            {
                renderer.BindPoseSource(source);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        public bool TryGetLatestPair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer)
        {
            ThrowIfDisposed();
            if (publishedStateSource != null)
            {
                if (!publishedStateSource.TryCaptureLatestAuthoritativeState(
                        stateFrame))
                {
                    older = null!;
                    newer = null!;
                    return false;
                }
            }
            else
            {
                stateReader!.Capture(stateFrame);
            }
            if (!hasPublishedState ||
                stateFrame.Sequence != lastSequence ||
                stateFrame.SimulationTime != lastSimulationTime ||
                stateFrame.ContinuityId != lastContinuityId)
            {
                PublishCurrentState();
            }

            return poseBuffer.TryGetLatestPair(out older, out newer);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (ownsStateReader)
            {
                stateReader?.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private void PublishCurrentState()
        {
            ReachyMujocoBodyPose[] poses =
                new ReachyMujocoBodyPose[stateFrame.BodyPoseCount];
            for (int index = 0; index < poses.Length; ++index)
            {
                ReachySimBodyPoseSnapshot nativePose =
                    stateFrame.GetBodyPose(index);
                uint expectedBodyId = checked((uint)index + 1U);
                if (nativePose.BodyId != expectedBodyId)
                {
                    throw new InvalidOperationException(
                        $"Native body pose {index} declares MuJoCo body " +
                        $"{nativePose.BodyId}, expected {expectedBodyId}.");
                }
                poses[index] = new ReachyMujocoBodyPose(
                    index,
                    bodyNames[index],
                    nativePose.PositionX,
                    nativePose.PositionY,
                    nativePose.PositionZ,
                    nativePose.QuaternionW,
                    nativePose.QuaternionX,
                    nativePose.QuaternionY,
                    nativePose.QuaternionZ);
            }

            poseBuffer.Publish(
                new ReachyAuthoritativePoseSnapshot(
                    stateFrame.Sequence,
                    stateFrame.SimulationTime,
                    stateFrame.ContinuityId,
                    poses));
            hasPublishedState = true;
            lastSequence = stateFrame.Sequence;
            lastSimulationTime = stateFrame.SimulationTime;
            lastContinuityId = stateFrame.ContinuityId;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachySimAuthoritativePoseSource));
            }
        }
    }
}
""",
    encoding="utf-8",
    newline="\n",
)

runtime_path = root / "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs"
runtime = runtime_path.read_text(encoding="utf-8")
runtime = runtime.replace(
    """        public ReachyAuthoritativeRendererStatus RendererStatus =>
            renderer?.Status ?? ReachyAuthoritativeRendererStatus.Unbound;

        public ReachyPresentationBody[] GetCanonicalBodies()
""",
    """        public ReachyAuthoritativeRendererStatus RendererStatus =>
            renderer?.Status ?? ReachyAuthoritativeRendererStatus.Unbound;

        public ulong PublishedWorkerSequence
        {
            get
            {
                return worker != null && worker.TryGetLatestSnapshot(
                    out ReachyPublishedSimulationSnapshot snapshot)
                        ? snapshot.State.Sequence
                        : 0UL;
            }
        }

        public ulong WorkerPublicationSequence
        {
            get
            {
                return worker != null && worker.TryGetLatestSnapshot(
                    out ReachyPublishedSimulationSnapshot snapshot)
                        ? snapshot.PublicationSequence
                        : 0UL;
            }
        }

        public ulong WorkerStepCount
        {
            get
            {
                return worker != null && worker.TryGetLatestSnapshot(
                    out ReachyPublishedSimulationSnapshot snapshot)
                        ? snapshot.Timing.TotalStepCount
                        : 0UL;
            }
        }

        public ReachyPresentationBody[] GetCanonicalBodies()
""",
    1,
)
old_runtime_block = """                ReachySimAuthoritativeStateReader reader =
                    new ReachySimAuthoritativeStateReader(session);
                ValidateLayout(reader.Layout, manifest!);
                string[] bodyNames = new string[canonicalBodies.Length];
                for (int index = 0; index < canonicalBodies.Length; ++index)
                {
                    bodyNames[index] = canonicalBodies[index].BodyName;
                }
                poseSource = new ReachySimAuthoritativePoseSource(
                    reader,
                    bodyNames,
                    ownsStateReader: true);
                boundPoseSource = new FaultCapturingPoseSource(poseSource);
                renderer.BindPoseSource(boundPoseSource);

                worker = new ReachySimulationWorker(session);
"""
new_runtime_block = """                ReachySimAuthoritativeStateReader reader =
                    new ReachySimAuthoritativeStateReader(session);
                ValidateLayout(reader.Layout, manifest!);
                try
                {
                    worker = new ReachySimulationWorker(session, reader);
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
                string[] bodyNames = new string[canonicalBodies.Length];
                for (int index = 0; index < canonicalBodies.Length; ++index)
                {
                    bodyNames[index] = canonicalBodies[index].BodyName;
                }
                poseSource = new ReachySimAuthoritativePoseSource(
                    worker,
                    bodyNames);
                boundPoseSource = new FaultCapturingPoseSource(poseSource);
                renderer.BindPoseSource(boundPoseSource);

"""
if runtime.count(old_runtime_block) != 1:
    raise SystemExit("Could not locate production direct-reader block")
runtime = runtime.replace(old_runtime_block, new_runtime_block)
runtime_path.write_text(runtime, encoding="utf-8", newline="\n")

acceptance_path = root / "Assets/ReachyMini/Runtime/Rendering/ReachyAuthoritativePhysicalAcceptance.cs"
replace_once(
    acceptance_path,
    """                        $"renderer={renderer.Status}, " +
                        $"runtime_fault={runtime.Fault}, " +
                        $"renderer_fault={renderer.Fault}.");
""",
    """                        $"renderer={renderer.Status}, " +
                        $"worker_state_sequence={runtime.PublishedWorkerSequence}, " +
                        $"worker_publication_sequence={runtime.WorkerPublicationSequence}, " +
                        $"worker_steps={runtime.WorkerStepCount}, " +
                        $"runtime_fault={runtime.Fault}, " +
                        $"renderer_fault={renderer.Fault}.");
""",
    "physical acceptance diagnostics",
)

program_path = root / "managed/ReachyMini.Core.Tests/Program.cs"
program = program_path.read_text(encoding="utf-8")
program = program.replace(
    """                TestAuthoritativeSimulationWorker();
                TestAuthoritativeSimulationWorkerSnapshots();
""",
    """                TestAuthoritativeSimulationWorker();
                TestAuthoritativeSimulationWorkerPublishedState();
                TestAuthoritativeSimulationWorkerSnapshots();
""",
    1,
)
method_marker = "        private static void TestAuthoritativeSimulationWorkerSnapshots()\n"
method_index = program.index(method_marker)
new_test = """        private static void TestAuthoritativeSimulationWorkerPublishedState()
        {
            ReachySimCreateResult createResult = ReachySimSession.Create(
                Encoding.UTF8.GetBytes("worker-published-authoritative-state"));
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Published-state worker create failed: {createResult.Error.Code}: {createResult.Error.Message}");
            SyntheticAuthoritativeStateReader reader =
                new SyntheticAuthoritativeStateReader();

            using (ReachySimulationWorker worker =
                new ReachySimulationWorker(session, reader))
            {
                ReachySimAuthoritativeStateFrame destination =
                    worker.CreateAuthoritativeStateFrame();
                AssertEqual(
                    false,
                    worker.TryCaptureLatestAuthoritativeState(destination),
                    "authoritative publication unavailable before worker start");

                AssertControlSuccess(
                    worker.Start(TimeSpan.FromSeconds(5.0)),
                    "published-state worker start");
                long deadline = Stopwatch.GetTimestamp() + checked(
                    (long)Math.Ceiling(5.0 * Stopwatch.Frequency));
                while ((!worker.TryCaptureLatestAuthoritativeState(destination) ||
                        destination.Sequence < 5UL) &&
                    Stopwatch.GetTimestamp() < deadline)
                {
                    Thread.Sleep(1);
                }
                if (destination.Sequence < 5UL)
                {
                    throw new InvalidOperationException(
                        "Managed test timed out waiting for worker-owned authoritative state.");
                }

                AssertEqual(
                    reader.Layout.ModelHash,
                    worker.AuthoritativeStateLayout.ModelHash,
                    "worker authoritative model hash");
                AssertEqual(2, destination.BodyPoseCount, "published body count");
                AssertEqual(
                    1U,
                    destination.GetBodyPose(0).BodyId,
                    "published first body identifier");
                AssertEqual(
                    checked((double)destination.Sequence),
                    destination.GetBodyPose(1).PositionX,
                    "published body value");
                ulong firstSequence = destination.Sequence;
                Thread.Sleep(40);
                if (!worker.TryCaptureLatestAuthoritativeState(destination) ||
                    destination.Sequence <= firstSequence)
                {
                    throw new InvalidOperationException(
                        "Managed test failed: worker-owned authoritative publication did not advance independently of its reader.");
                }
            }

            AssertEqual(true, reader.IsDisposed, "worker owns authoritative reader");
        }

"""
program = program[:method_index] + new_test + program[method_index:]
class_marker = "        private static byte[] CreateCommandBatch(ulong sequence)\n"
class_index = program.index(class_marker)
synthetic_reader = """        private sealed class SyntheticAuthoritativeStateReader :
            IReachySimAuthoritativeStateReader
        {
            private ulong nextSequence;

            internal SyntheticAuthoritativeStateReader()
            {
                Layout = new ReachySimAuthoritativeStateLayout(
                    byteCount: 512,
                    modelHash: 0x40a040a040a040a0UL,
                    qposCount: 2,
                    qvelCount: 2,
                    actuatorObservationCount: 1,
                    bodyPoseCount: 2);
            }

            public ReachySimAuthoritativeStateLayout Layout { get; }

            internal bool IsDisposed { get; private set; }

            public ReachySimAuthoritativeStateFrame CreateFrame()
            {
                return new ReachySimAuthoritativeStateFrame(Layout);
            }

            public void Capture(ReachySimAuthoritativeStateFrame frame)
            {
                ulong sequence = nextSequence++;
                frame.Sequence = sequence;
                frame.SimulationTime = sequence *
                    ProjectMetadata.InitialPhysicsTimestepSeconds;
                frame.ContinuityId = 1U;
                frame.JointCount = 3U;
                frame.ContactCount = 0U;
                frame.HealthFlags = 0U;
                frame.CalibrationProfileId =
                    ProjectMetadata.UncalibratedCalibrationProfileId;
                frame.WarningCount = 0UL;
                frame.ConstraintCount = 1U;
                frame.EqualityConstraintCount = 1U;
                frame.MaximumConstraintResidual = 0.0;
                frame.MaximumEqualityConstraintResidual = 0.0;
                frame.QposStorage[0] = sequence;
                frame.QposStorage[1] = -checked((double)sequence);
                frame.QvelStorage[0] = 0.0;
                frame.QvelStorage[1] = 0.0;
                frame.SetActuatorObservation(
                    0,
                    new ReachySimActuatorObservationSnapshot(
                        actuatorId: 0U,
                        controlValue: 0.0,
                        actuatorForce: 0.0,
                        length: 0.0,
                        velocity: 0.0));
                frame.SetBodyPose(
                    0,
                    new ReachySimBodyPoseSnapshot(
                        bodyId: 1U,
                        positionX: 0.0,
                        positionY: 0.0,
                        positionZ: 0.0,
                        quaternionW: 1.0,
                        quaternionX: 0.0,
                        quaternionY: 0.0,
                        quaternionZ: 0.0));
                frame.SetBodyPose(
                    1,
                    new ReachySimBodyPoseSnapshot(
                        bodyId: 2U,
                        positionX: sequence,
                        positionY: 0.0,
                        positionZ: 0.0,
                        quaternionW: 1.0,
                        quaternionX: 0.0,
                        quaternionY: 0.0,
                        quaternionZ: 0.0));
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

"""
program = program[:class_index] + synthetic_reader + program[class_index:]
program_path.write_text(program, encoding="utf-8", newline="\n")

support_path = root / "Assets/ReachyMini/Tests/Editor/ReachySimAuthoritativeStateTestSupport.cs"
support = support_path.read_text(encoding="utf-8")
support = support.replace(
    "using ReachyMini.Rendering;\n",
    "using ReachyMini.Rendering;\nusing ReachyMini.Simulation;\n",
    1,
)
insert_marker = "        private sealed class FakeStateReader :\n"
insert_index = support.index(insert_marker)
fake_published = """        private sealed class FakePublishedStateSource :
            IReachyPublishedAuthoritativeStateSource,
            IDisposable
        {
            private readonly FakeStateReader reader;

            internal FakePublishedStateSource(byte[][] frames)
            {
                reader = new FakeStateReader(frames);
            }

            public ReachySimAuthoritativeStateLayout AuthoritativeStateLayout =>
                reader.Layout;

            public ReachySimAuthoritativeStateFrame CreateAuthoritativeStateFrame()
            {
                return reader.CreateFrame();
            }

            public bool TryCaptureLatestAuthoritativeState(
                ReachySimAuthoritativeStateFrame destination)
            {
                reader.Capture(destination);
                return true;
            }

            public void Dispose()
            {
                reader.Dispose();
            }
        }

"""
support = support[:insert_index] + fake_published + support[insert_index:]
support_path.write_text(support, encoding="utf-8", newline="\n")

unity_test_path = root / "Assets/ReachyMini/Tests/Editor/ReachySimAuthoritativeStateTests.cs"
unity_test = unity_test_path.read_text(encoding="utf-8")
end_marker = "        [Test]\n        public void PoseSourcePublishesOrderedPairsAndDiscontinuities()\n"
end_index = unity_test.index(end_marker)
worker_source_test = """        [Test]
        public void PoseSourceConsumesWorkerPublishedFrames()
        {
            byte[][] frames =
            {
                BuildPayload(4UL, 0.008, 1U, 0.4),
                BuildPayload(5UL, 0.010, 1U, 0.5),
            };
            using (FakePublishedStateSource published =
                new FakePublishedStateSource(frames))
            using (ReachySimAuthoritativePoseSource source =
                new ReachySimAuthoritativePoseSource(
                    published,
                    new[] { "base", "head" }))
            {
                Assert.That(source.TryGetLatestPair(out _, out _), Is.False);
                Assert.That(
                    source.TryGetLatestPair(
                        out ReachyAuthoritativePoseSnapshot older,
                        out ReachyAuthoritativePoseSnapshot newer),
                    Is.True);
                Assert.That(older.Sequence, Is.EqualTo(4UL));
                Assert.That(newer.Sequence, Is.EqualTo(5UL));
                Assert.That(newer.GetBodyPose(1).PositionX, Is.EqualTo(0.5));
            }
        }

"""
unity_test = unity_test[:end_index] + worker_source_test + unity_test[end_index:]
unity_test_path.write_text(unity_test, encoding="utf-8", newline="\n")
