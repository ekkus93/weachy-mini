#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase C: the real per-frame pipeline behind "perception" --
    // camera texture bridge -> homography reprojection -> on-device tracker
    // -> BoundedWorldModel. This is a MonoBehaviour (not a plain
    // ReachyApplicationServiceBase, which application services normally are)
    // because ReachyCameraHomographyWarpPipeline.Execute() and the GPU
    // readback inside ReachyUnityTrackingFrameResources.StageAsync are
    // Unity-main-thread-only operations, so the per-frame pipeline must run
    // from Update() rather than a background Task.Run loop (contrast
    // ReachyBaselineBehaviorApplicationService, phase A, which has no such
    // constraint). ReachyAndroidPerceptionApplicationService owns/creates
    // this driver and exposes its snapshot through IReachyPerceptionService.
    //
    // Threading note for future maintainers: ReachyAndroidMlKitTrackingBackend
    // completes its Task from whatever thread the ML Kit Java callback fires
    // on, which is not guaranteed to be the Unity main thread. This driver
    // relies on ordinary `await` (no ConfigureAwait(false)) throughout
    // RunOneFrameAsync, which -- because it is invoked from Update(), where
    // Unity's own SynchronizationContext is current -- correctly resumes
    // every continuation in this method back on the Unity main thread
    // regardless of which thread the awaited work completes on. Do not add
    // ConfigureAwait(false) here without re-deriving this guarantee: several
    // steps after tracker awaits (frame disposal, PublishSnapshot) are not
    // safe to run off the main thread.
    [DisallowMultipleComponent]
    internal sealed class ReachyAndroidPerceptionDriver : MonoBehaviour
    {
        private const string ProviderInstanceId = "reachy-perception-driver";
        private const int TrackingTimeoutSeconds = 5;

        private readonly object snapshotSync = new object();
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private ReachyProductionAuthoritativeRuntime? runtime;
        private ReachyCameraCalibrationPersistenceStore? calibrations;
        private ReachyAndroidCameraTextureBridge? textureBridge;
        private ReachyCameraHomographyWarpPipeline? warpPipeline;
        private ReachyOnDeviceLightweightTracker? tracker;
        private VisionProviderSelection? selection;
        private BoundedWorldModel? worldModel;
        private ReachySimAuthoritativeStateFrame? capturedStateFrame;

        private ulong frameGeneration;
        private ulong requestSequence;
        private bool analysisInFlight;
        private bool paused;
        private bool configured;

        private ReachyPerceptionServiceSnapshot snapshot =
            new ReachyPerceptionServiceSnapshot(
                ReachyPerceptionServiceExecutionState.NoCameraFrame,
                null,
                "Perception has not started.",
                0UL);

        public ReachyPerceptionServiceSnapshot Snapshot
        {
            get
            {
                lock (snapshotSync)
                {
                    return snapshot;
                }
            }
        }

        public event EventHandler<ReachyPerceptionServiceSnapshotChangedEventArgs>?
            SnapshotChanged;

        public void Configure(
            ReachyProductionAuthoritativeRuntime runtime,
            ReachyCameraCalibrationPersistenceStore calibrations)
        {
            if (configured)
            {
                throw new InvalidOperationException(
                    "The perception driver cannot be configured more than once.");
            }
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.calibrations = calibrations ??
                throw new ArgumentNullException(nameof(calibrations));

            worldModel = new BoundedWorldModel(WorldModelPolicy.CreateMobileDefault());
            var backend = new ReachyAndroidMlKitTrackingBackend();
            tracker = new ReachyOnDeviceLightweightTracker(ProviderInstanceId, backend);
            selection = new VisionProviderSelection(tracker.Descriptor);
            warpPipeline = new ReachyCameraHomographyWarpPipeline();
            configured = true;
        }

        public void PauseForApplicationInterruption()
        {
            paused = true;
            PublishSnapshot(
                ReachyPerceptionServiceExecutionState.Suspended,
                null,
                "Perception paused for an application interruption.");
        }

        public void ResumeAfterApplicationInterruption()
        {
            paused = false;
        }

        public Task<ReachyVisionFrame?> CaptureFrameForAnalysisAsync(
            CancellationToken cancellationToken)
        {
            return CaptureFrameAsync(
                cancellationToken,
                allowWhilePaused: false);
        }

        internal Task<ReachyVisionFrame?> CaptureFrameForAcceptanceAsync(
            CancellationToken cancellationToken)
        {
            return CaptureFrameAsync(
                cancellationToken,
                allowWhilePaused: true);
        }

        private Task<ReachyVisionFrame?> CaptureFrameAsync(
            CancellationToken cancellationToken,
            bool allowWhilePaused)
        {
            if (!configured || (!allowWhilePaused && paused) || analysisInFlight ||
                tracker == null || warpPipeline == null ||
                worldModel == null || runtime == null || calibrations == null ||
                selection == null)
            {
                return Task.FromResult<ReachyVisionFrame?>(null);
            }
            cancellationToken.ThrowIfCancellationRequested();

            ReachyAndroidCameraTextureBridge? bridge = LocateTextureBridge();
            if (bridge == null)
            {
                PublishSnapshot(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    null,
                    "No camera texture bridge is installed.");
                return Task.FromResult<ReachyVisionFrame?>(null);
            }

            analysisInFlight = true;
            try
            {
                FrameBuildResult built = TryBuildFrame(bridge);
                if (!built.Succeeded)
                {
                    PublishSnapshot(built.FailureState, null, built.FailureMessage);
                }
                return Task.FromResult(built.Frame);
            }
            finally
            {
                analysisInFlight = false;
            }
        }

        private void OnDestroy()
        {
            warpPipeline?.Dispose();
            if (tracker != null)
            {
                _ = tracker.DisposeAsync().AsTask();
            }
        }

        private void Update()
        {
            if (!configured || paused || analysisInFlight ||
                tracker == null || warpPipeline == null ||
                worldModel == null || runtime == null || calibrations == null ||
                selection == null)
            {
                return;
            }

            ReachyAndroidCameraTextureBridge? bridge = LocateTextureBridge();
            if (bridge == null)
            {
                PublishSnapshot(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    null,
                    "No camera texture bridge is installed.");
                return;
            }

            analysisInFlight = true;
            _ = RunOneFrameAsync(bridge);
        }

        private ReachyAndroidCameraTextureBridge? LocateTextureBridge()
        {
            if (textureBridge != null)
            {
                return textureBridge;
            }
            ReachyAndroidCameraAcquisition? acquisition =
                FindAnyObjectByType<ReachyAndroidCameraAcquisition>();
            if (acquisition == null)
            {
                return null;
            }
            textureBridge = acquisition.GetComponent<ReachyAndroidCameraTextureBridge>();
            return textureBridge;
        }

        private readonly struct FrameBuildResult
        {
            private FrameBuildResult(
                ReachyVisionFrame? frame,
                ReachyPerceptionServiceExecutionState failureState,
                string failureMessage)
            {
                Frame = frame;
                FailureState = failureState;
                FailureMessage = failureMessage;
            }

            public static FrameBuildResult Success(ReachyVisionFrame frame) =>
                new FrameBuildResult(frame, default, string.Empty);

            public static FrameBuildResult Failure(
                ReachyPerceptionServiceExecutionState state, string message) =>
                new FrameBuildResult(null, state, message);

            public ReachyVisionFrame? Frame { get; }
            public ReachyPerceptionServiceExecutionState FailureState { get; }
            public string FailureMessage { get; }
            public bool Succeeded => Frame != null;
        }

        private FrameBuildResult TryBuildFrame(ReachyAndroidCameraTextureBridge bridge)
        {
            ReachyCameraTextureBridgeSnapshot bridgeSnapshot = bridge.Current;
            ReachyCameraTextureFrameDescriptor? descriptor = bridgeSnapshot.Frame;
            if (!bridgeSnapshot.HasTexture || descriptor == null)
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    "The camera texture bridge is not yet producing frames.");
            }

            ReachyCameraCalibrationSelectionResult calibrationSelection =
                calibrations!.State.SelectExact(
                    descriptor.CameraId,
                    descriptor.LensFacing,
                    descriptor.OutputWidth,
                    descriptor.OutputHeight,
                    descriptor.OutputWidth,
                    descriptor.OutputHeight,
                    ReachyCameraMujocoOpticalBinding.OfficialModelCompatibility);
            ReachyCameraCalibrationProfile? calibrationProfile =
                calibrationSelection.Profile;
            if (calibrationProfile == null)
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.NoCalibration,
                    "No exact camera calibration is available (" +
                        calibrationSelection.Status + "): " +
                        calibrationSelection.Message);
            }

            if (capturedStateFrame == null)
            {
                if (!runtime!.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame created))
                {
                    return FrameBuildResult.Failure(
                        ReachyPerceptionServiceExecutionState.NoCameraFrame,
                        "Waiting for the authoritative simulation to start.");
                }
                capturedStateFrame = created;
            }
            if (!runtime!.TryCaptureLatestAuthoritativeState(capturedStateFrame))
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    "Waiting for the authoritative simulation to publish a state frame.");
            }

            ReachySimBodyPoseSnapshot? cameraPose = FindCameraPose(capturedStateFrame);
            if (cameraPose == null)
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.Faulted,
                    "The authoritative canonical camera body is missing.");
            }

            var cameraQuaternion = new ReachyQuaternionD(
                cameraPose.Value.QuaternionX,
                cameraPose.Value.QuaternionY,
                cameraPose.Value.QuaternionZ,
                cameraPose.Value.QuaternionW);
            ReachyPhoneOpticalOrientationSample phoneOrientation =
                ReachyPhoneOpticalOrientationSample.Identity(
                    descriptor.TimestampNanoseconds);
            ReachyCameraRelativeRotationSample rotation =
                ReachyCameraRelativeRotationCalculator.Calculate(
                    ReachyCameraMujocoOpticalBinding.PinnedReachyMini,
                    calibrationProfile,
                    capturedStateFrame.Layout.ModelHash,
                    capturedStateFrame.Sequence,
                    capturedStateFrame.SimulationTime,
                    capturedStateFrame.ContinuityId,
                    cameraQuaternion,
                    phoneOrientation);

            ReachyCameraHomographyWarpResult warp = warpPipeline!.Execute(
                bridge,
                calibrationProfile,
                rotation);
            if (!warp.Succeeded || warp.Frame == null)
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    "Homography reprojection did not produce a frame: " + warp.Message);
            }

            ulong generation = checked(++frameGeneration);
            try
            {
                ReachyVisionFrame frame = ReachyUnityVisionFrameFactory.CreateOwnedTrackingFrame(
                    warp.Frame,
                    ProviderInstanceId,
                    generation);
                return FrameBuildResult.Success(frame);
            }
            catch (ArgumentException exception)
            {
                return FrameBuildResult.Failure(
                    ReachyPerceptionServiceExecutionState.NoCameraFrame,
                    "The reprojected frame's coverage is not observation-eligible: " +
                        exception.Message);
            }
        }

        private async Task RunOneFrameAsync(ReachyAndroidCameraTextureBridge bridge)
        {
            try
            {
                FrameBuildResult built = TryBuildFrame(bridge);
                if (!built.Succeeded)
                {
                    PublishSnapshot(built.FailureState, null, built.FailureMessage);
                    return;
                }
                ReachyVisionFrame frame = built.Frame!;

                try
                {
                    ulong sequence = checked(++requestSequence);
                    var context = new VisionRequestContext(
                        "perception-" + sequence,
                        selection!.Current,
                        TimeSpan.FromSeconds(TrackingTimeoutSeconds));
                    var request = new TrackingRequest(frame, context);
                    TrackingResult result = await VisionProviderExecutor.TrackAsync(
                        tracker!,
                        request,
                        selection,
                        CancellationToken.None);
                    if (!result.Succeeded)
                    {
                        PublishSnapshot(
                            ReachyPerceptionServiceExecutionState.Faulted,
                            null,
                            "Tracking failed (" + result.Status + "): " + result.Diagnostic);
                        return;
                    }

                    WorldModelTrackingBatch batch = WorldModelTrackingBatch.FromTrackingResult(
                        result,
                        frame.Coverage);
                    WorldModelUpdateResult update = worldModel!.ApplyTracking(batch);
                    if (!update.Accepted)
                    {
                        PublishSnapshot(
                            ReachyPerceptionServiceExecutionState.Faulted,
                            null,
                            "The world model rejected a tracking batch (" +
                                update.Status + "): " + update.Diagnostic);
                        return;
                    }

                    PublishSnapshot(
                        ReachyPerceptionServiceExecutionState.Tracking,
                        update.Snapshot,
                        "Tracking " + result.Objects.Count + " object(s).");
                }
                finally
                {
                    await frame.DisposeAsync();
                }
            }
            catch (Exception exception)
            {
                PublishSnapshot(
                    ReachyPerceptionServiceExecutionState.Faulted,
                    null,
                    "Perception loop failed: " + exception.Message);
            }
            finally
            {
                analysisInFlight = false;
            }
        }

        private static ReachySimBodyPoseSnapshot? FindCameraPose(
            ReachySimAuthoritativeStateFrame state)
        {
            int match = -1;
            for (int index = 0; index < state.BodyPoseCount; ++index)
            {
                if (state.GetBodyPose(index).BodyId !=
                    ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId)
                {
                    continue;
                }
                if (match >= 0)
                {
                    return null;
                }
                match = index;
            }
            return match < 0 ? null : state.GetBodyPose(match);
        }

        private long NowNanoseconds()
        {
            double nanoseconds =
                clock.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);
            return checked((long)nanoseconds);
        }

        private void PublishSnapshot(
            ReachyPerceptionServiceExecutionState state,
            WorldModelSnapshot? worldSnapshot,
            string message)
        {
            ReachyPerceptionServiceSnapshot next;
            lock (snapshotSync)
            {
                next = new ReachyPerceptionServiceSnapshot(
                    state,
                    worldSnapshot,
                    message,
                    checked(snapshot.Revision + 1UL));
                snapshot = next;
            }
            SnapshotChanged?.Invoke(
                this,
                new ReachyPerceptionServiceSnapshotChangedEventArgs(next));
        }
    }
}
