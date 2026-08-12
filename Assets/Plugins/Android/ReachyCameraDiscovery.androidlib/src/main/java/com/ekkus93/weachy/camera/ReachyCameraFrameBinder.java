package com.ekkus93.weachy.camera;

import androidx.annotation.NonNull;
import androidx.annotation.OptIn;
import androidx.camera.camera2.interop.Camera2CameraInfo;
import androidx.camera.camera2.interop.ExperimentalCamera2Interop;
import androidx.camera.core.Camera;
import androidx.camera.core.CameraFilter;
import androidx.camera.core.CameraInfo;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.CameraState;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageProxy;
import androidx.camera.core.Preview;
import androidx.camera.core.resolutionselector.ResolutionSelector;
import androidx.camera.core.resolutionselector.ResolutionStrategy;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.lifecycle.Observer;

import com.google.common.util.concurrent.ListenableFuture;

import android.util.Size;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutionException;

/**
 * CameraX bind and camera-state state-machine glue for {@link ReachyCameraFrameBridge}.
 *
 * <p>{@code @GuardedBy}-style contract: every method here that carries the {@code Locked}
 * suffix, or that runs inside its own {@code synchronized (}{@link ReachyCameraFrameBridge#LOCK}
 * {@code )} block, follows the same convention documented on
 * {@link ReachyCameraFrameLifecycleController}: it assumes the caller already holds
 * {@link ReachyCameraFrameBridge#LOCK}, unless the method itself acquires the lock. Both
 * {@link #bindProvider(ListenableFuture, long, String, int, int, int)} and
 * {@link #handleCameraState(CameraState, long)} run on the Android main thread from CameraX
 * future/observer callbacks and acquire {@link ReachyCameraFrameBridge#LOCK} themselves;
 * {@link #handleStoppingCameraStateLocked(CameraState)} assumes the caller (
 * {@link #handleCameraState(CameraState, long)}) already holds it.
 */
final class ReachyCameraFrameBinder {

    private ReachyCameraFrameBinder() {
    }

    static void bindProvider(
            ListenableFuture<ProcessCameraProvider> providerFuture,
            long expectedGeneration,
            String requestedCameraId,
            int width,
            int height,
            int targetRotation) {
        ReachyCameraFrameBridge.requireMainThread();
        try {
            ProcessCameraProvider provider = providerFuture.get();
            synchronized (ReachyCameraFrameBridge.LOCK) {
                if (expectedGeneration != ReachyCameraFrameBridge.generation ||
                        ReachyCameraFrameBridge.sessionId == 0L ||
                        !"Starting".equals(ReachyCameraFrameBridge.state)) {
                    return;
                }

                ResolutionSelector resolutionSelector =
                        new ResolutionSelector.Builder()
                                .setResolutionStrategy(
                                        new ResolutionStrategy(
                                                new Size(width, height),
                                                ResolutionStrategy
                                                        .FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER))
                                .build();
                Preview nextPreview =
                        new Preview.Builder()
                                .setResolutionSelector(resolutionSelector)
                                .setTargetRotation(targetRotation)
                                .build();
                ImageAnalysis nextAnalysis =
                        new ImageAnalysis.Builder()
                                .setResolutionSelector(resolutionSelector)
                                .setTargetRotation(targetRotation)
                                .setOutputImageFormat(
                                        ImageAnalysis.OUTPUT_IMAGE_FORMAT_YUV_420_888)
                                .setBackpressureStrategy(
                                        ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                                .build();
                ReachyDiscardingPreviewSurfaceProvider nextSurfaceProvider =
                        new ReachyDiscardingPreviewSurfaceProvider();
                nextPreview.setSurfaceProvider(
                        ReachyCameraFrameBridge.MAIN_EXECUTOR,
                        nextSurfaceProvider);
                final long analyzerGeneration = expectedGeneration;
                nextAnalysis.setAnalyzer(
                        ReachyCameraFrameLifecycleController.requireAnalyzerExecutorLocked(),
                        new ImageAnalysis.Analyzer() {
                            @Override
                            public void analyze(@NonNull ImageProxy imageProxy) {
                                ReachyCameraFrameAnalyzer.analyzeFrame(
                                        imageProxy,
                                        analyzerGeneration);
                            }
                        });

                CameraSelector selector = exactCameraSelector(requestedCameraId);
                Camera nextCamera = provider.bindToLifecycle(
                        ReachyCameraFrameLifecycleController.requireLifecycleOwnerLocked(),
                        selector,
                        nextPreview,
                        nextAnalysis);
                ReachyCameraFrameBridge.cameraProvider = provider;
                ReachyCameraFrameBridge.boundCamera = nextCamera;
                ReachyCameraFrameBridge.preview = nextPreview;
                ReachyCameraFrameBridge.imageAnalysis = nextAnalysis;
                ReachyCameraFrameBridge.previewSurfaceProvider = nextSurfaceProvider;
                ReachyCameraFrameBridge.state = "Starting";
                ReachyCameraFrameBridge.message =
                        "CameraX use cases are bound; waiting for the camera device to open.";
                ReachyCameraFrameBridge.errorCode = "";

                final long observerGeneration = expectedGeneration;
                Observer<CameraState> nextObserver =
                        new Observer<CameraState>() {
                            @Override
                            public void onChanged(CameraState cameraState) {
                                handleCameraState(
                                        cameraState,
                                        observerGeneration);
                            }
                        };
                ReachyCameraFrameBridge.cameraStateObserver = nextObserver;
                nextCamera.getCameraInfo()
                        .getCameraState()
                        .observeForever(nextObserver);
            }
        } catch (ExecutionException exception) {
            ReachyCameraFrameLifecycleController.failOnMain(
                    expectedGeneration,
                    "camera_provider_failed",
                    ReachyCameraErrorUtil.safeMessage(exception.getCause() == null
                            ? exception
                            : exception.getCause()));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            ReachyCameraFrameLifecycleController.failOnMain(
                    expectedGeneration,
                    "camera_provider_interrupted",
                    ReachyCameraErrorUtil.safeMessage(exception));
        } catch (RuntimeException exception) {
            ReachyCameraFrameLifecycleController.failOnMain(
                    expectedGeneration,
                    "camera_bind_failed",
                    ReachyCameraErrorUtil.safeMessage(exception));
        }
    }

    static void handleCameraState(
            CameraState cameraState,
            long expectedGeneration) {
        ReachyCameraFrameBridge.requireMainThread();
        if (cameraState == null) {
            return;
        }
        synchronized (ReachyCameraFrameBridge.LOCK) {
            if ("Stopping".equals(ReachyCameraFrameBridge.state)) {
                handleStoppingCameraStateLocked(cameraState);
                return;
            }
            if (expectedGeneration != ReachyCameraFrameBridge.generation ||
                    ReachyCameraFrameBridge.sessionId == 0L ||
                    !ReachyCameraFrameLifecycleController.isActiveState(
                            ReachyCameraFrameBridge.state)) {
                return;
            }

            CameraState.StateError cameraError = cameraState.getError();
            if (cameraError != null) {
                String nextErrorCode =
                        ReachyCameraErrorUtil.cameraStateErrorCode(cameraError.getCode());
                String detail = ReachyCameraErrorUtil.cameraStateErrorDetail(cameraError);
                if (cameraError.getType() == CameraState.ErrorType.CRITICAL) {
                    ++ReachyCameraFrameBridge.generation;
                    ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                    ReachyCameraFrameLifecycleController.setInactiveLocked(
                            ReachyCameraErrorUtil.cameraStateErrorIsUnavailable(cameraError.getCode())
                                    ? "Unavailable"
                                    : "Faulted",
                            nextErrorCode,
                            detail);
                    return;
                }

                if (!"Paused".equals(ReachyCameraFrameBridge.state)) {
                    ReachyCameraFrameBridge.state = "Starting";
                    ReachyCameraFrameBridge.errorCode = nextErrorCode;
                    ReachyCameraFrameBridge.message =
                            "CameraX is recovering from " +
                            nextErrorCode + ": " + detail;
                }
                return;
            }

            switch (cameraState.getType()) {
                case OPEN:
                    if (!"Paused".equals(ReachyCameraFrameBridge.state)) {
                        ReachyCameraFrameBridge.state = "Running";
                        ReachyCameraFrameBridge.errorCode = "";
                        ReachyCameraFrameBridge.message =
                                "CameraX camera device is open with Preview and ImageAnalysis active.";
                    }
                    break;
                case OPENING:
                    if (!"Paused".equals(ReachyCameraFrameBridge.state)) {
                        ReachyCameraFrameBridge.state = "Starting";
                        ReachyCameraFrameBridge.errorCode = "";
                        ReachyCameraFrameBridge.message = "CameraX camera device is opening.";
                    }
                    break;
                case PENDING_OPEN:
                    if (!"Paused".equals(ReachyCameraFrameBridge.state)) {
                        ReachyCameraFrameBridge.state = "Starting";
                        ReachyCameraFrameBridge.errorCode = "";
                        ReachyCameraFrameBridge.message =
                                "CameraX is waiting for the selected camera to become available.";
                    }
                    break;
                case CLOSING:
                    if (!"Paused".equals(ReachyCameraFrameBridge.state) &&
                            !"Stopping".equals(ReachyCameraFrameBridge.state)) {
                        ReachyCameraFrameBridge.message = "CameraX camera device is closing.";
                    }
                    break;
                case CLOSED:
                    if (!"Paused".equals(ReachyCameraFrameBridge.state) &&
                            !"Stopping".equals(ReachyCameraFrameBridge.state)) {
                        ReachyCameraFrameBridge.state = "Starting";
                        ReachyCameraFrameBridge.message =
                                "CameraX camera device is closed and awaiting a reopen transition.";
                    }
                    break;
                default:
                    break;
            }
        }
    }

    static void handleStoppingCameraStateLocked(
            CameraState cameraState) {
        CameraState.StateError cameraError = cameraState.getError();
        if (cameraError != null &&
                cameraError.getType() == CameraState.ErrorType.CRITICAL) {
            String detail = ReachyCameraErrorUtil.cameraStateErrorDetail(cameraError);
            ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
            ReachyCameraFrameLifecycleController.setInactiveLocked(
                    "Faulted",
                    "camera_close_failed",
                    "CameraX failed while closing the camera device: " + detail);
            return;
        }

        switch (cameraState.getType()) {
            case CLOSING:
                ReachyCameraFrameBridge.message =
                        "CameraX camera device is closing; restart remains blocked.";
                break;
            case CLOSED:
                ReachyCameraFrameLifecycleController.completeGracefulStopLocked();
                break;
            default:
                ReachyCameraFrameBridge.message =
                        "CameraX use cases are unbound; waiting for camera device CLOSED.";
                break;
        }
    }

    @OptIn(markerClass = ExperimentalCamera2Interop.class)
    static CameraSelector exactCameraSelector(final String selectedId) {
        CameraFilter filter = new CameraFilter() {
            @NonNull
            @Override
            public List<CameraInfo> filter(@NonNull List<CameraInfo> cameraInfos) {
                List<CameraInfo> matching = new ArrayList<>();
                for (CameraInfo cameraInfo : cameraInfos) {
                    String candidateId =
                            Camera2CameraInfo.from(cameraInfo).getCameraId();
                    if (selectedId.equals(candidateId)) {
                        matching.add(cameraInfo);
                    }
                }
                return matching;
            }
        };
        return new CameraSelector.Builder()
                .addCameraFilter(filter)
                .build();
    }
}
