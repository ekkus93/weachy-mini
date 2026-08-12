#nullable enable

using System;
using System.Security.Cryptography;
using ReachyMini.Interop;
using ReachyMini.Presentation;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Rendering
{
    public enum ReachyProductionRuntimeStatus
    {
        Uninitialized = 0,
        Unavailable = 1,
        Starting = 2,
        Running = 3,
        Paused = 4,
        Faulted = 5,
        Stopped = 6,
    }

    [DisallowMultipleComponent]
    public sealed class ReachyProductionAuthoritativeRuntime : MonoBehaviour,
        IReachySimulationTimingSource
    {
        private const string ModelResourcePath =
            "ReachyMiniRuntime/reachy_mini_mjb";
        private const string ManifestResourcePath =
            "ReachyMiniRuntime/runtime_manifest_json";
        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10.0);

        private ReachyPresentationRoot? presentationRoot;
        private ReachyAuthoritativeRenderer? renderer;
        private ReachyPresentationBody[] canonicalBodies =
            Array.Empty<ReachyPresentationBody>();
        private ReachySimSession? session;
        private ReachySimulationWorker? worker;
        private ReachySimAuthoritativePoseSource? poseSource;
        private FaultCapturingPoseSource? boundPoseSource;
        private ulong nextCommandSequence = 1UL;
        private bool shuttingDown;

        public ReachyProductionRuntimeStatus Status { get; private set; } =
            ReachyProductionRuntimeStatus.Uninitialized;

        public string Fault { get; private set; } = string.Empty;

        public ulong ModelHash => poseSource?.ModelHash ?? 0UL;

        public int BodyCount => canonicalBodies.Length;

        public ReachySimulationRunState SimulationState =>
            worker?.State ?? ReachySimulationRunState.Created;

        public ReachySimulationRunState SimulationRunState => SimulationState;

        public bool TryGetLatestTimingSnapshot(
            out ReachySimulationTimingSnapshot timing)
        {
            ReachySimulationWorker? activeWorker = worker;
            if (activeWorker != null &&
                activeWorker.TryGetLatestTimingSnapshot(out timing))
            {
                return true;
            }
            timing = default;
            return false;
        }

        public bool TryCreateAuthoritativeStateFrame(
            out ReachySimAuthoritativeStateFrame frame)
        {
            ReachySimulationWorker? activeWorker = worker;
            if (activeWorker == null ||
                Status != ReachyProductionRuntimeStatus.Running)
            {
                frame = null!;
                return false;
            }

            frame = activeWorker.CreateAuthoritativeStateFrame();
            return true;
        }

        public bool TryCaptureLatestAuthoritativeState(
            ReachySimAuthoritativeStateFrame destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            ReachySimulationWorker? activeWorker = worker;
            return activeWorker != null &&
                Status == ReachyProductionRuntimeStatus.Running &&
                activeWorker.TryCaptureLatestAuthoritativeState(destination);
        }

        public ReachyAuthoritativeRendererStatus RendererStatus =>
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
        {
            ReachyPresentationBody[] copy =
                new ReachyPresentationBody[canonicalBodies.Length];
            Array.Copy(canonicalBodies, copy, canonicalBodies.Length);
            return copy;
        }

        public ReachySimulationCommandEnqueueResult SubmitPositionTargets(
            ReadOnlySpan<double> targetsRadians)
        {
            if (worker == null || Status != ReachyProductionRuntimeStatus.Running)
            {
                return ReachySimulationCommandEnqueueResult.WorkerUnavailable;
            }

            byte[] batch = ReachySimulationCommandBatch.CreatePositionTargets(
                nextCommandSequence,
                targetsRadians);
            ReachySimulationCommandEnqueueResult result =
                worker.EnqueueCommandBatch(batch);
            if (result == ReachySimulationCommandEnqueueResult.Accepted)
            {
                nextCommandSequence = checked(nextCommandSequence + 1UL);
            }
            return result;
        }

        public ReachySimulationControlResult ResetNeutral()
        {
            if (worker == null)
            {
                return ReachySimulationControlResult.Failure(
                    ReachySimulationRunState.Stopped,
                    new ReachySimError(
                        ReachySimErrorCode.InvalidHandle,
                        ReachySimRecoverability.RecreateHandle,
                        "The production simulation worker is unavailable."));
            }
            return worker.Reset(
                (uint)ReachySimResetPose.NeutralAwake,
                ControlTimeout);
        }

        public bool TryGetLatestAuthoritativePair(
            out ReachyAuthoritativePoseSnapshot older,
            out ReachyAuthoritativePoseSnapshot newer)
        {
            ReachySimAuthoritativePoseSource? source = poseSource;
            if (source == null)
            {
                older = null!;
                newer = null!;
                return false;
            }
            return source.TryGetLatestPair(out older, out newer);
        }

        private void Start()
        {
            presentationRoot = GetComponent<ReachyPresentationRoot>();
            renderer = GetComponent<ReachyAuthoritativeRenderer>();
            if (presentationRoot == null || renderer == null)
            {
                EnterFault(
                    "The generated presentation root or authoritative renderer is missing.");
                return;
            }

            canonicalBodies = presentationRoot.GetCanonicalBodies();
#if UNITY_ANDROID && !UNITY_EDITOR
            StartProductionRuntime();
#else
            Status = ReachyProductionRuntimeStatus.Unavailable;
            renderer.enabled = false;
            Debug.Log(
                "Reachy production simulation is unavailable outside an Android player; " +
                "the authoritative renderer remains unbound.",
                this);
#endif
        }

        private void Update()
        {
            if (Status != ReachyProductionRuntimeStatus.Running &&
                Status != ReachyProductionRuntimeStatus.Paused)
            {
                return;
            }
            if (boundPoseSource != null && !string.IsNullOrEmpty(boundPoseSource.Fault))
            {
                EnterFault(boundPoseSource.Fault);
                return;
            }
            if (worker != null && worker.State == ReachySimulationRunState.Faulted)
            {
                ReachySimulationFault? simulationFault = worker.Fault;
                EnterFault(
                    simulationFault == null
                        ? "The authoritative simulation worker faulted without diagnostics."
                        : $"Simulation {simulationFault.Operation} failed: " +
                          $"{simulationFault.Error.Code}: {simulationFault.Error.Message}");
                return;
            }
            if (renderer != null &&
                renderer.Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                EnterFault(
                    string.IsNullOrWhiteSpace(renderer.Fault)
                        ? "The authoritative renderer faulted without diagnostics."
                        : renderer.Fault);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            ReachySimulationWorker? activeWorker = worker;
            if (activeWorker == null || shuttingDown)
            {
                return;
            }

            ReachySimulationControlResult result;
            if (paused && Status == ReachyProductionRuntimeStatus.Running)
            {
                result = activeWorker.Pause(ControlTimeout);
                if (result.IsSuccess)
                {
                    Status = ReachyProductionRuntimeStatus.Paused;
                }
            }
            else if (!paused && Status == ReachyProductionRuntimeStatus.Paused)
            {
                result = activeWorker.Resume(ControlTimeout);
                if (result.IsSuccess)
                {
                    Status = ReachyProductionRuntimeStatus.Running;
                }
            }
            else
            {
                return;
            }

            if (!result.IsSuccess)
            {
                EnterFault(
                    $"Simulation lifecycle transition failed: " +
                    $"{result.Error.Code}: {result.Error.Message}");
            }
        }

        private void OnDestroy()
        {
            ShutdownRuntime(logFailures: true);
        }

        private void StartProductionRuntime()
        {
            if (presentationRoot == null || renderer == null)
            {
                EnterFault("Production runtime dependencies were not initialized.");
                return;
            }

            Status = ReachyProductionRuntimeStatus.Starting;
            try
            {
                TextAsset? modelAsset = Resources.Load<TextAsset>(ModelResourcePath);
                TextAsset? manifestAsset = Resources.Load<TextAsset>(ManifestResourcePath);
                if (modelAsset == null || manifestAsset == null)
                {
                    throw new InvalidOperationException(
                        "The production Reachy MJB or runtime manifest is missing from Resources.");
                }

                RuntimeManifest? manifest = JsonUtility.FromJson<RuntimeManifest>(
                    manifestAsset.text);
                byte[] modelBytes = modelAsset.bytes;
                ValidateRuntimeAssets(manifest, modelBytes, presentationRoot);

                ReachySimCreateResult createResult = ReachySimSession.Create(modelBytes);
                if (!createResult.IsSuccess || createResult.Session == null)
                {
                    throw new InvalidOperationException(
                        $"Native simulator creation failed: {createResult.Error.Code}: " +
                        createResult.Error.Message);
                }
                session = createResult.Session;

                ReachySimAuthoritativeStateReader reader =
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

                ReachySimulationControlResult startResult = worker.Start(ControlTimeout);
                if (!startResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Simulation worker startup failed: {startResult.Error.Code}: " +
                        startResult.Error.Message);
                }

                Status = ReachyProductionRuntimeStatus.Running;
                Debug.Log(
                    $"Reachy production authoritative runtime started: " +
                    $"model_hash={poseSource.ModelHash} bodies={canonicalBodies.Length}.",
                    this);
            }
            catch (Exception exception)
            {
                EnterFault(exception.Message);
            }
        }

        private void EnterFault(string message)
        {
            if (Status == ReachyProductionRuntimeStatus.Faulted || shuttingDown)
            {
                return;
            }
            Fault = string.IsNullOrWhiteSpace(message)
                ? "The production authoritative runtime failed without diagnostics."
                : message;
            Status = ReachyProductionRuntimeStatus.Faulted;
            if (renderer != null)
            {
                renderer.enabled = false;
            }
            Debug.LogError(
                $"Reachy production authoritative runtime fault: {Fault}",
                this);
            ShutdownRuntime(logFailures: true, preserveFault: true);
        }

        private void ShutdownRuntime(
            bool logFailures,
            bool preserveFault = false)
        {
            if (shuttingDown)
            {
                return;
            }
            shuttingDown = true;
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            poseSource?.Dispose();
            poseSource = null;
            boundPoseSource = null;

            if (worker != null)
            {
                try
                {
                    worker.Dispose();
                }
                catch (InvalidOperationException exception)
                {
                    if (logFailures)
                    {
                        Debug.LogError(
                            $"Reachy simulation shutdown failed: {exception.Message}",
                            this);
                    }
                }
                worker = null;
                session = null;
            }
            else if (session != null)
            {
                try
                {
                    session.Dispose();
                }
                catch (InvalidOperationException exception)
                {
                    if (logFailures)
                    {
                        Debug.LogError(
                            $"Reachy native session shutdown failed: {exception.Message}",
                            this);
                    }
                }
                session = null;
            }

            if (!preserveFault && Status != ReachyProductionRuntimeStatus.Unavailable)
            {
                Status = ReachyProductionRuntimeStatus.Stopped;
            }
            shuttingDown = false;
        }

        private static void ValidateRuntimeAssets(
            RuntimeManifest? manifest,
            byte[] modelBytes,
            ReachyPresentationRoot root)
        {
            if (manifest == null || manifest.schema_version != 1)
            {
                throw new InvalidOperationException(
                    "The Reachy runtime manifest is missing or has an unsupported schema.");
            }
            if (!string.Equals(manifest.mujoco_version, "3.9.0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The runtime manifest declares MuJoCo {manifest.mujoco_version}, expected 3.9.0.");
            }
            if (manifest.model_byte_count != modelBytes.Length)
            {
                throw new InvalidOperationException(
                    $"The staged MJB size changed: manifest={manifest.model_byte_count}, " +
                    $"resource={modelBytes.Length}.");
            }
            string actualHash = ComputeSha256(modelBytes);
            if (!string.Equals(
                    actualHash,
                    manifest.mjb_sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The staged MJB SHA-256 does not match the runtime manifest.");
            }
            if (!string.Equals(
                    root.SourceModelSha256,
                    manifest.source_model_sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The generated Unity presentation and production MJB come from different source models.");
            }
            if (manifest.body_pose_count != root.BodyCount)
            {
                throw new InvalidOperationException(
                    $"The runtime manifest contains {manifest.body_pose_count} body poses, " +
                    $"but the generated presentation contains {root.BodyCount} bodies.");
            }
        }

        private static void ValidateLayout(
            ReachySimAuthoritativeStateLayout layout,
            RuntimeManifest manifest)
        {
            if (layout.BodyPoseCount != manifest.body_pose_count ||
                layout.ActuatorObservationCount != manifest.actuator_count ||
                layout.QposCount != manifest.qpos_count ||
                layout.QvelCount != manifest.qvel_count)
            {
                throw new InvalidOperationException(
                    "The native authoritative-state dimensions do not match the staged runtime manifest.");
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(bytes);
                return BitConverter.ToString(digest)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        [Serializable]
        private sealed class RuntimeManifest
        {
            public int schema_version = 0;
            public string mujoco_version = string.Empty;
            public string source_model_sha256 = string.Empty;
            public string mjb_sha256 = string.Empty;
            public int model_byte_count = 0;
            public int body_pose_count = 0;
            public int actuator_count = 0;
            public int qpos_count = 0;
            public int qvel_count = 0;
        }

        private sealed class FaultCapturingPoseSource :
            IReachyReusableAuthoritativePoseSource
        {
            private readonly IReachyAuthoritativePoseSource inner;
            private readonly IReachyReusableAuthoritativePoseSource reusableInner;

            public FaultCapturingPoseSource(IReachyAuthoritativePoseSource inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
                reusableInner = inner as IReachyReusableAuthoritativePoseSource ??
                    throw new ArgumentException(
                        "The production pose source must provide reusable pose buffers.",
                        nameof(inner));
            }

            public string Fault { get; private set; } = string.Empty;

            public int BodyCount => reusableInner.BodyCount;

            public ReachyReusableAuthoritativePoseFrame CreatePoseFrame()
            {
                return reusableInner.CreatePoseFrame();
            }

            public bool TryCopyLatestPair(
                ReachyReusableAuthoritativePoseFrame olderDestination,
                ReachyReusableAuthoritativePoseFrame newerDestination)
            {
                if (!string.IsNullOrEmpty(Fault))
                {
                    return false;
                }
                try
                {
                    return reusableInner.TryCopyLatestPair(
                        olderDestination,
                        newerDestination);
                }
                catch (Exception exception)
                {
                    Fault = exception.Message;
                    return false;
                }
            }

            public bool TryGetLatestPair(
                out ReachyAuthoritativePoseSnapshot older,
                out ReachyAuthoritativePoseSnapshot newer)
            {
                if (!string.IsNullOrEmpty(Fault))
                {
                    older = null!;
                    newer = null!;
                    return false;
                }
                try
                {
                    return inner.TryGetLatestPair(out older, out newer);
                }
                catch (Exception exception)
                {
                    Fault = exception.Message;
                    older = null!;
                    newer = null!;
                    return false;
                }
            }
        }
    }
}
