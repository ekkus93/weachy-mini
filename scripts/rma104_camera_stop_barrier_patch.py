from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(
            f"Expected one replacement in {path}, found {count}."
        )
    file_path.write_text(text.replace(old, new, 1), encoding="utf-8")


java_path = (
    "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/"
    "src/main/java/com/ekkus93/weachy/camera/ReachyCameraFrameBridge.java"
)
unity_path = (
    "Assets/ReachyMini/Runtime/Application/"
    "ReachyAndroidCameraAcquisition.cs"
)
acceptance_path = "scripts/run_rma092_camera_texture_acceptance_android.sh"

replace_once(
    java_path,
    """            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                ++generation;
                expectedGeneration = generation;
""",
    """            synchronized (LOCK) {
                if (\"Stopping\".equals(state)) {
                    return snapshotLocked();
                }
                stopBoundUseCasesLocked();
                ++generation;
                expectedGeneration = generation;
""",
)

replace_once(
    java_path,
    """    public static String stop(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (isActiveState(state)) {
                state = \"Stopping\";
                message = \"Unbinding CameraX Preview and ImageAnalysis.\";
            }
            ++generation;
            stopBoundUseCasesLocked();
            setInactiveLocked(
                    \"Stopped\",
                    \"\",
                    \"CameraX Preview and ImageAnalysis are unbound.\");
            return snapshotLocked();
        }
    }
""",
    """    public static String stop(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (!isActiveState(state) || \"Stopping\".equals(state)) {
                return snapshotLocked();
            }
            state = \"Stopping\";
            message =
                    \"CameraX use cases are unbinding; waiting for camera device CLOSED.\";
            errorCode = \"\";
            ++generation;
            try {
                beginGracefulStopLocked();
            } catch (RuntimeException exception) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        \"Faulted\",
                        \"camera_stop_failed\",
                        safeMessage(exception));
            }
            return snapshotLocked();
        }
    }
""",
)

replace_once(
    java_path,
    """        synchronized (LOCK) {
            if (expectedGeneration != generation ||
                    sessionId == 0L ||
                    !isActiveState(state)) {
                return;
            }

            CameraState.StateError cameraError = cameraState.getError();
""",
    """        synchronized (LOCK) {
            if (\"Stopping\".equals(state)) {
                handleStoppingCameraStateLocked(cameraState);
                return;
            }
            if (expectedGeneration != generation ||
                    sessionId == 0L ||
                    !isActiveState(state)) {
                return;
            }

            CameraState.StateError cameraError = cameraState.getError();
""",
)

stop_helpers = """    private static void beginGracefulStopLocked() {
        if (boundCamera == null) {
            stopBoundUseCasesLocked();
            setInactiveLocked(
                    \"Stopped\",
                    \"\",
                    \"No CameraX device was bound; stop completed without a close transition.\");
            return;
        }

        ReachyCameraTextureFrameBridge.endSession(generation);
        if (imageAnalysis != null) {
            imageAnalysis.clearAnalyzer();
        }
        if (preview != null) {
            preview.setSurfaceProvider((Preview.SurfaceProvider) null);
        }
        if (lifecycleOwner != null) {
            lifecycleOwner.destroy();
        }
        if (cameraProvider != null) {
            if (preview != null && imageAnalysis != null) {
                cameraProvider.unbind(preview, imageAnalysis);
            } else if (preview != null) {
                cameraProvider.unbind(preview);
            } else if (imageAnalysis != null) {
                cameraProvider.unbind(imageAnalysis);
            }
        }

        descriptor = null;
        latestFrame = null;
        deliveredSequence = 0L;
        if (!\"Stopping\".equals(state) || boundCamera == null) {
            return;
        }
        CameraState current = boundCamera.getCameraInfo()
                .getCameraState()
                .getValue();
        if (current != null &&
                current.getType() == CameraState.Type.CLOSED) {
            completeGracefulStopLocked();
        }
    }

    private static void handleStoppingCameraStateLocked(
            CameraState cameraState) {
        CameraState.StateError cameraError = cameraState.getError();
        if (cameraError != null &&
                cameraError.getType() == CameraState.ErrorType.CRITICAL) {
            String detail = cameraStateErrorDetail(cameraError);
            stopBoundUseCasesLocked();
            setInactiveLocked(
                    \"Faulted\",
                    \"camera_close_failed\",
                    \"CameraX failed while closing the camera device: \" + detail);
            return;
        }

        switch (cameraState.getType()) {
            case CLOSING:
                message =
                        \"CameraX camera device is closing; restart remains blocked.\";
                break;
            case CLOSED:
                completeGracefulStopLocked();
                break;
            default:
                message =
                        \"CameraX use cases are unbound; waiting for camera device CLOSED.\";
                break;
        }
    }

    private static void completeGracefulStopLocked() {
        if (boundCamera != null && cameraStateObserver != null) {
            boundCamera.getCameraInfo()
                    .getCameraState()
                    .removeObserver(cameraStateObserver);
        }
        cameraStateObserver = null;
        boundCamera = null;
        if (previewSurfaceProvider != null) {
            previewSurfaceProvider.close();
        }
        if (analyzerExecutor != null) {
            analyzerExecutor.shutdownNow();
        }
        cameraProvider = null;
        lifecycleOwner = null;
        preview = null;
        imageAnalysis = null;
        previewSurfaceProvider = null;
        analyzerExecutor = null;
        setInactiveLocked(
                \"Stopped\",
                \"\",
                \"CameraX camera device reached CLOSED; Preview and ImageAnalysis are fully released.\");
    }

"""
replace_once(
    java_path,
    "    private static void stopBoundUseCasesLocked() {\n",
    stop_helpers + "    private static void stopBoundUseCasesLocked() {\n",
)

replace_once(
    unity_path,
    """        private ReachyCameraFacing preferredFacing =
            ReachyCameraFacing.Unconfigured;
""",
    """        private ReachyCameraFacing preferredFacing =
            ReachyCameraFacing.Unconfigured;
        private bool pendingStartAfterStop;
""",
)

replace_once(
    unity_path,
    """        public void StartPreferred(ReachyCameraFacing facing)
        {
            EnsureReady();
            preferredFacing = facing;
            ReachyCameraCapabilitySnapshot capabilities =
                RequireDiscovery().State.Current;
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                if (capabilities.Permission == ReachyCameraPermissionState.Revoked)
                {
                    state.MarkPermissionRevoked(capabilities.Message);
                }
                else
                {
                    state.MarkUnavailable(capabilities.Message);
                }
                return;
            }

            ReachyCameraCapability? selected =
                SelectCamera(capabilities, facing);
            if (selected == null)
            {
                desiredActive = false;
                state.MarkUnavailable(
                    $\"No available {GetFacingLabel(facing)} camera exposes a YUV analysis resolution.\");
                return;
            }

            ReachyCameraResolution resolution =
                selected.AnalysisResolutions[0];
            if (state.Current.IsActive)
            {
                StopPlatformForSwitch();
            }

            ulong session = nextSessionId;
            nextSessionId = checked(nextSessionId + 1UL);
            desiredActive = true;
            state.BeginStart(
                session,
                selected.Facing,
                selected.CameraId,
                $\"Binding CameraX camera {selected.CameraId} at {resolution}.\");
            string json = RequirePlatform().Start(
                checked((long)session),
                selected.CameraId,
                resolution.Width,
                resolution.Height);
            ApplyPlatformSnapshot(json);
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
""",
    """        public void StartPreferred(ReachyCameraFacing facing)
        {
            EnsureReady();
            preferredFacing = facing;
            if (state.Current.IsActive)
            {
                desiredActive = true;
                pendingStartAfterStop = true;
                if (state.Current.State !=
                    ReachyCameraAcquisitionState.Stopping)
                {
                    StopPlatformForSwitch();
                }
                return;
            }

            ReachyCameraCapabilitySnapshot capabilities =
                RequireDiscovery().State.Current;
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                pendingStartAfterStop = false;
                if (capabilities.Permission == ReachyCameraPermissionState.Revoked)
                {
                    state.MarkPermissionRevoked(capabilities.Message);
                }
                else
                {
                    state.MarkUnavailable(capabilities.Message);
                }
                return;
            }

            ReachyCameraCapability? selected =
                SelectCamera(capabilities, facing);
            if (selected == null)
            {
                desiredActive = false;
                pendingStartAfterStop = false;
                state.MarkUnavailable(
                    $\"No available {GetFacingLabel(facing)} camera exposes a YUV analysis resolution.\");
                return;
            }

            ReachyCameraResolution resolution =
                selected.AnalysisResolutions[0];
            ulong session = nextSessionId;
            nextSessionId = checked(nextSessionId + 1UL);
            desiredActive = true;
            pendingStartAfterStop = false;
            state.BeginStart(
                session,
                selected.Facing,
                selected.CameraId,
                $\"Binding CameraX camera {selected.CameraId} at {resolution}.\");
            string json = RequirePlatform().Start(
                checked((long)session),
                selected.CameraId,
                resolution.Width,
                resolution.Height);
            ApplyPlatformSnapshot(json);
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
""",
)

replace_once(
    unity_path,
    """        public void StopAcquisition()
        {
            EnsureReady();
            desiredActive = false;
            if (state.Current.IsActive &&
                state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    \"Stopping CameraX Preview and ImageAnalysis.\");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            if (state.Current.State != ReachyCameraAcquisitionState.Stopped)
            {
                state.MarkStopped(
                    \"CameraX Preview and ImageAnalysis are stopped.\");
            }
        }
""",
    """        public void StopAcquisition()
        {
            EnsureReady();
            desiredActive = false;
            pendingStartAfterStop = false;
            if (state.Current.IsActive &&
                state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    \"Stopping CameraX Preview and ImageAnalysis.\");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
""",
)

replace_once(
    unity_path,
    """            initialized = false;
            desiredActive = false;
        }
""",
    """            initialized = false;
            desiredActive = false;
            pendingStartAfterStop = false;
        }
""",
)

replace_once(
    unity_path,
    """        private void StopPlatformForSwitch()
        {
            if (state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    \"Unbinding the current camera before an explicit switch.\");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            if (state.Current.State != ReachyCameraAcquisitionState.Stopped)
            {
                state.MarkStopped(
                    \"The previous camera is fully unbound before switching.\");
            }
        }
""",
    """        private void StopPlatformForSwitch()
        {
            if (state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    \"Unbinding the current camera before an explicit switch.\");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
""",
)

replace_once(
    unity_path,
    """                case \"Stopped\":
                    state.MarkStopped(detail);
                    break;
""",
    """                case \"Stopped\":
                    bool restartAfterClose =
                        desiredActive && pendingStartAfterStop;
                    state.MarkStopped(detail);
                    if (restartAfterClose)
                    {
                        pendingStartAfterStop = false;
                        StartPreferred(preferredFacing);
                    }
                    break;
""",
)

replace_once(
    acceptance_path,
    """        and report.get(\"current_state\") == \"Stopped\"
    )
""",
    """        and report.get(\"current_state\") == \"Stopped\"
        and \"CLOSED\" in str(report.get(\"message\", \"\"))
    )
""",
)
