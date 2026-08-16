package com.ekkus93.weachy.camera;

import androidx.camera.core.CameraState;

import java.util.concurrent.ExecutorService;

/**
 * Stop/teardown/finalize state machine for {@link ReachyCameraFrameBridge}.
 *
 * <p>{@code @GuardedBy}-style contract: every method here follows the
 * {@code *Locked} naming convention used throughout
 * {@link ReachyCameraFrameBridge} and assumes the caller already holds
 * {@link ReachyCameraFrameBridge#LOCK}, unless documented otherwise.
 * {@link #failOnMain(long, String, String)} is the one exception: it
 * acquires {@link ReachyCameraFrameBridge#LOCK} itself because it runs from
 * CameraX callback contexts (future listeners, analyzer failure callbacks)
 * that do not already hold it.
 */
final class ReachyCameraFrameLifecycleController {

    private ReachyCameraFrameLifecycleController() {
    }

    static void beginGracefulStopLocked() {
        if (ReachyCameraFrameBridge.boundCamera == null) {
            stopBoundUseCasesLocked();
            setInactiveLocked(
                    "Stopped",
                    "",
                    "No CameraX device was bound; stop completed without a close transition.");
            return;
        }

        ReachyCameraTextureFrameBridge.endSession(ReachyCameraFrameBridge.generation);
        if (ReachyCameraFrameBridge.imageAnalysis != null) {
            ReachyCameraFrameBridge.imageAnalysis.clearAnalyzer();
        }
        if (ReachyCameraFrameBridge.lifecycleOwner != null) {
            ReachyCameraFrameBridge.lifecycleOwner.destroy();
        }
        if (ReachyCameraFrameBridge.cameraProvider != null
                && ReachyCameraFrameBridge.imageAnalysis != null) {
            ReachyCameraFrameBridge.cameraProvider.unbind(
                    ReachyCameraFrameBridge.imageAnalysis);
        }

        ReachyCameraFrameBridge.descriptor = null;
        ReachyCameraFrameBridge.latestFrame = null;
        ReachyCameraFrameBridge.deliveredSequence = 0L;
        if (!"Stopping".equals(ReachyCameraFrameBridge.state)
                || ReachyCameraFrameBridge.boundCamera == null) {
            return;
        }
        CameraState current = ReachyCameraFrameBridge.boundCamera.getCameraInfo()
                .getCameraState()
                .getValue();
        if (current != null &&
                current.getType() == CameraState.Type.CLOSED) {
            completeGracefulStopLocked();
        }
    }

    static void completeGracefulStopLocked() {
        if (ReachyCameraFrameBridge.boundCamera != null
                && ReachyCameraFrameBridge.cameraStateObserver != null) {
            ReachyCameraFrameBridge.boundCamera.getCameraInfo()
                    .getCameraState()
                    .removeObserver(ReachyCameraFrameBridge.cameraStateObserver);
        }
        ReachyCameraFrameBridge.cameraStateObserver = null;
        ReachyCameraFrameBridge.boundCamera = null;
        if (ReachyCameraFrameBridge.analyzerExecutor != null) {
            ReachyCameraFrameBridge.analyzerExecutor.shutdownNow();
        }
        ReachyCameraFrameBridge.cameraProvider = null;
        ReachyCameraFrameBridge.lifecycleOwner = null;
        ReachyCameraFrameBridge.imageAnalysis = null;
        ReachyCameraFrameBridge.analyzerExecutor = null;
        setInactiveLocked(
                "Stopped",
                "",
                "CameraX camera device reached CLOSED; ImageAnalysis is fully released.");
    }

    static void stopBoundUseCasesLocked() {
        if (ReachyCameraFrameBridge.boundCamera != null
                && ReachyCameraFrameBridge.cameraStateObserver != null) {
            ReachyCameraFrameBridge.boundCamera.getCameraInfo()
                    .getCameraState()
                    .removeObserver(ReachyCameraFrameBridge.cameraStateObserver);
        }
        ReachyCameraFrameBridge.cameraStateObserver = null;
        ReachyCameraFrameBridge.boundCamera = null;
        if (ReachyCameraFrameBridge.imageAnalysis != null) {
            ReachyCameraFrameBridge.imageAnalysis.clearAnalyzer();
        }
        if (ReachyCameraFrameBridge.cameraProvider != null
                && ReachyCameraFrameBridge.imageAnalysis != null) {
            ReachyCameraFrameBridge.cameraProvider.unbind(
                    ReachyCameraFrameBridge.imageAnalysis);
        }
        if (ReachyCameraFrameBridge.lifecycleOwner != null) {
            ReachyCameraFrameBridge.lifecycleOwner.destroy();
        }
        if (ReachyCameraFrameBridge.analyzerExecutor != null) {
            ReachyCameraFrameBridge.analyzerExecutor.shutdownNow();
        }
        ReachyCameraTextureFrameBridge.endSession(ReachyCameraFrameBridge.generation);
        ReachyCameraFrameBridge.cameraProvider = null;
        ReachyCameraFrameBridge.lifecycleOwner = null;
        ReachyCameraFrameBridge.imageAnalysis = null;
        ReachyCameraFrameBridge.analyzerExecutor = null;
        ReachyCameraFrameBridge.descriptor = null;
        ReachyCameraFrameBridge.latestFrame = null;
        ReachyCameraFrameBridge.deliveredSequence = 0L;
    }

    static void setInactiveLocked(
            String nextState,
            String nextErrorCode,
            String nextMessage) {
        ReachyCameraFrameBridge.state = nextState;
        ReachyCameraFrameBridge.errorCode = nextErrorCode == null ? "" : nextErrorCode;
        ReachyCameraFrameBridge.message = nextMessage == null || nextMessage.trim().isEmpty()
                ? "Camera acquisition changed state without diagnostics."
                : nextMessage;
        ReachyCameraFrameBridge.sessionId = 0L;
        ReachyCameraFrameBridge.cameraId = "";
        ReachyCameraFrameBridge.descriptor = null;
        ReachyCameraFrameBridge.latestFrame = null;
        ReachyCameraFrameBridge.deliveredSequence = 0L;
    }

    static void failOnMain(
            long expectedGeneration,
            String code,
            String detail) {
        ReachyCameraFrameBridge.requireMainThread();
        synchronized (ReachyCameraFrameBridge.LOCK) {
            if (expectedGeneration != ReachyCameraFrameBridge.generation) {
                return;
            }
            ++ReachyCameraFrameBridge.generation;
            stopBoundUseCasesLocked();
            setInactiveLocked("Faulted", code, detail);
        }
    }

    static ExecutorService requireAnalyzerExecutorLocked() {
        if (ReachyCameraFrameBridge.analyzerExecutor == null) {
            throw new IllegalStateException(
                    "The CameraX analyzer executor is unavailable.");
        }
        return ReachyCameraFrameBridge.analyzerExecutor;
    }

    static ReachyCameraLifecycleOwner requireLifecycleOwnerLocked() {
        if (ReachyCameraFrameBridge.lifecycleOwner == null) {
            throw new IllegalStateException(
                    "The CameraX lifecycle owner is unavailable.");
        }
        return ReachyCameraFrameBridge.lifecycleOwner;
    }

    static boolean isActiveState(String value) {
        return "Starting".equals(value) ||
                "Running".equals(value) ||
                "Paused".equals(value) ||
                "Stopping".equals(value);
    }
}
