package com.ekkus93.weachy.camera;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.os.Handler;
import android.os.Looper;
import android.util.Size;
import android.view.Surface;

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

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.Executor;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class ReachyCameraFrameBridge {
    // Package-private (not private): read/written directly by
    // ReachyCameraFrameLifecycleController's *Locked helpers, which are
    // always invoked while holding this same LOCK instance. See the
    // "Tricky Shared State" guidance in docs/LARGE_FILE_REFACTOR_TODO.md
    // section 7 for why these fields and LOCK itself are widened instead of
    // wrapped in accessors.
    static final Object LOCK = new Object();
    // Package-private (not private): posted to directly by
    // ReachyCameraFrameAnalyzer.analyzeFrame's failure path, which runs
    // without LOCK held (it acquires LOCK itself for its two critical
    // sections). See the "Tricky Shared State" guidance in
    // docs/LARGE_FILE_REFACTOR_TODO.md section 7.
    static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static final Executor MAIN_EXECUTOR = new Executor() {
        @Override
        public void execute(Runnable command) {
            MAIN_HANDLER.post(command);
        }
    };
    static long generation;
    static long sessionId;
    static long deliveredSequence;
    static String state = "Stopped";
    static String message = "Camera frame acquisition is stopped.";
    static String errorCode = "";
    static String cameraId = "";
    static ReachyCameraDescriptor descriptor;
    static ReachyFrameSnapshot latestFrame;
    static ProcessCameraProvider cameraProvider;
    static Camera boundCamera;
    static Observer<CameraState> cameraStateObserver;
    static ReachyCameraLifecycleOwner lifecycleOwner;
    static Preview preview;
    static ImageAnalysis imageAnalysis;
    static ReachyDiscardingPreviewSurfaceProvider previewSurfaceProvider;
    static ExecutorService analyzerExecutor;

    private ReachyCameraFrameBridge() {
    }

    public static String start(
            Activity activity,
            long requestedSessionId,
            String requestedCameraId,
            int width,
            int height) {
        requireMainThread();
        try {
            requireActivity(activity);
            if (requestedSessionId <= 0L) {
                throw new IllegalArgumentException(
                        "The camera acquisition session identifier must be positive.");
            }
            if (requestedCameraId == null || requestedCameraId.trim().isEmpty()) {
                throw new IllegalArgumentException(
                        "Camera acquisition requires a stable camera identifier.");
            }
            if (width <= 0 || height <= 0) {
                throw new IllegalArgumentException(
                        "Camera acquisition dimensions must be positive.");
            }
            if (activity.checkSelfPermission(Manifest.permission.CAMERA)
                    != PackageManager.PERMISSION_GRANTED) {
                synchronized (LOCK) {
                    ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                    ReachyCameraFrameLifecycleController.setInactiveLocked(
                            "PermissionRevoked",
                            "permission_denied",
                            "Android camera permission is not granted.");
                    return ReachyCameraFrameAnalyzer.snapshotLocked();
                }
            }

            ReachyCameraDescriptor requestedDescriptor =
                    ReachyCameraDescriptor.load(activity, requestedCameraId);
            int targetRotation = activity.getWindowManager()
                    .getDefaultDisplay()
                    .getRotation();
            requireSurfaceRotation(targetRotation);
            final long expectedGeneration;
            final ListenableFuture<ProcessCameraProvider> providerFuture;
            synchronized (LOCK) {
                if ("Stopping".equals(state)) {
                    return ReachyCameraFrameAnalyzer.snapshotLocked();
                }
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ++generation;
                expectedGeneration = generation;
                sessionId = requestedSessionId;
                ReachyCameraTextureFrameBridge.beginSession(
                        expectedGeneration,
                        sessionId);
                deliveredSequence = 0L;
                state = "Starting";
                message = "Waiting for CameraX to bind Preview and ImageAnalysis.";
                errorCode = "";
                cameraId = requestedCameraId;
                descriptor = requestedDescriptor;
                latestFrame = null;
                lifecycleOwner = new ReachyCameraLifecycleOwner();
                lifecycleOwner.start();
                analyzerExecutor = Executors.newSingleThreadExecutor(
                        new ReachyCameraFrameThreadFactory("reachy-camera-analysis"));
                providerFuture = ProcessCameraProvider.getInstance(activity);
            }

            providerFuture.addListener(
                    new Runnable() {
                        @Override
                        public void run() {
                            bindProvider(
                                    providerFuture,
                                    expectedGeneration,
                                    requestedCameraId,
                                    width,
                                    height,
                                    targetRotation);
                        }
                    },
                    MAIN_EXECUTOR);
            synchronized (LOCK) {
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
        } catch (SecurityException exception) {
            synchronized (LOCK) {
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ReachyCameraFrameLifecycleController.setInactiveLocked(
                        "PermissionRevoked",
                        "permission_denied",
                        ReachyCameraErrorUtil.safeMessage(exception));
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
        } catch (CameraAccessException exception) {
            synchronized (LOCK) {
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ReachyCameraFrameLifecycleController.setInactiveLocked(
                        "Unavailable",
                        ReachyCameraErrorUtil.cameraAccessCode(exception),
                        ReachyCameraErrorUtil.safeMessage(exception));
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
        } catch (RuntimeException exception) {
            synchronized (LOCK) {
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ReachyCameraFrameLifecycleController.setInactiveLocked(
                        "Faulted",
                        "camera_start_failed",
                        ReachyCameraErrorUtil.safeMessage(exception));
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
        }
    }

    public static String pause(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (lifecycleOwner != null &&
                    ("Starting".equals(state) || "Running".equals(state))) {
                lifecycleOwner.pause();
                state = "Paused";
                message = "CameraX lifecycle is paused and camera use cases are suspended.";
            }
            return ReachyCameraFrameAnalyzer.snapshotLocked();
        }
    }

    public static String resume(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (activity.checkSelfPermission(Manifest.permission.CAMERA)
                    != PackageManager.PERMISSION_GRANTED) {
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ReachyCameraFrameLifecycleController.setInactiveLocked(
                        "PermissionRevoked",
                        "permission_denied",
                        "Camera permission was revoked while acquisition was paused.");
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
            if (lifecycleOwner != null && "Paused".equals(state)) {
                lifecycleOwner.start();
                state = "Starting";
                errorCode = "";
                message =
                        "CameraX lifecycle resumed; waiting for the camera device to reopen.";
            }
            return ReachyCameraFrameAnalyzer.snapshotLocked();
        }
    }

    public static String stop(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (!ReachyCameraFrameLifecycleController.isActiveState(state) ||
                    "Stopping".equals(state)) {
                return ReachyCameraFrameAnalyzer.snapshotLocked();
            }
            state = "Stopping";
            message =
                    "CameraX use cases are unbinding; waiting for camera device CLOSED.";
            errorCode = "";
            ++generation;
            try {
                ReachyCameraFrameLifecycleController.beginGracefulStopLocked();
            } catch (RuntimeException exception) {
                ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                ReachyCameraFrameLifecycleController.setInactiveLocked(
                        "Faulted",
                        "camera_stop_failed",
                        ReachyCameraErrorUtil.safeMessage(exception));
            }
            return ReachyCameraFrameAnalyzer.snapshotLocked();
        }
    }

    public static String snapshot() {
        synchronized (LOCK) {
            return ReachyCameraFrameAnalyzer.snapshotLocked();
        }
    }

    public static ReachyCameraTextureFrameBridge.FrameLease
            acquireLatestTextureFrame(
                    long requestedSessionId,
                    long afterSequence) {
        final long expectedGeneration;
        synchronized (LOCK) {
            if (!"Running".equals(state) ||
                    requestedSessionId <= 0L ||
                    requestedSessionId != sessionId) {
                return null;
            }
            expectedGeneration = generation;
        }
        return ReachyCameraTextureFrameBridge.acquireLatest(
                expectedGeneration,
                requestedSessionId,
                afterSequence);
    }

    public static void shutdown(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            ++generation;
            ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
            ReachyCameraFrameLifecycleController.setInactiveLocked(
                    "Stopped",
                    "",
                    "Camera frame acquisition shut down.");
        }
    }

    private static void bindProvider(
            ListenableFuture<ProcessCameraProvider> providerFuture,
            long expectedGeneration,
            String requestedCameraId,
            int width,
            int height,
            int targetRotation) {
        requireMainThread();
        try {
            ProcessCameraProvider provider = providerFuture.get();
            synchronized (LOCK) {
                if (expectedGeneration != generation ||
                        sessionId == 0L ||
                        !"Starting".equals(state)) {
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
                        MAIN_EXECUTOR,
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
                cameraProvider = provider;
                boundCamera = nextCamera;
                preview = nextPreview;
                imageAnalysis = nextAnalysis;
                previewSurfaceProvider = nextSurfaceProvider;
                state = "Starting";
                message =
                        "CameraX use cases are bound; waiting for the camera device to open.";
                errorCode = "";

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
                cameraStateObserver = nextObserver;
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

    private static void handleCameraState(
            CameraState cameraState,
            long expectedGeneration) {
        requireMainThread();
        if (cameraState == null) {
            return;
        }
        synchronized (LOCK) {
            if ("Stopping".equals(state)) {
                handleStoppingCameraStateLocked(cameraState);
                return;
            }
            if (expectedGeneration != generation ||
                    sessionId == 0L ||
                    !ReachyCameraFrameLifecycleController.isActiveState(state)) {
                return;
            }

            CameraState.StateError cameraError = cameraState.getError();
            if (cameraError != null) {
                String nextErrorCode =
                        ReachyCameraErrorUtil.cameraStateErrorCode(cameraError.getCode());
                String detail = ReachyCameraErrorUtil.cameraStateErrorDetail(cameraError);
                if (cameraError.getType() == CameraState.ErrorType.CRITICAL) {
                    ++generation;
                    ReachyCameraFrameLifecycleController.stopBoundUseCasesLocked();
                    ReachyCameraFrameLifecycleController.setInactiveLocked(
                            ReachyCameraErrorUtil.cameraStateErrorIsUnavailable(cameraError.getCode())
                                    ? "Unavailable"
                                    : "Faulted",
                            nextErrorCode,
                            detail);
                    return;
                }

                if (!"Paused".equals(state)) {
                    state = "Starting";
                    errorCode = nextErrorCode;
                    message =
                            "CameraX is recovering from " +
                            nextErrorCode + ": " + detail;
                }
                return;
            }

            switch (cameraState.getType()) {
                case OPEN:
                    if (!"Paused".equals(state)) {
                        state = "Running";
                        errorCode = "";
                        message =
                                "CameraX camera device is open with Preview and ImageAnalysis active.";
                    }
                    break;
                case OPENING:
                    if (!"Paused".equals(state)) {
                        state = "Starting";
                        errorCode = "";
                        message = "CameraX camera device is opening.";
                    }
                    break;
                case PENDING_OPEN:
                    if (!"Paused".equals(state)) {
                        state = "Starting";
                        errorCode = "";
                        message =
                                "CameraX is waiting for the selected camera to become available.";
                    }
                    break;
                case CLOSING:
                    if (!"Paused".equals(state) &&
                            !"Stopping".equals(state)) {
                        message = "CameraX camera device is closing.";
                    }
                    break;
                case CLOSED:
                    if (!"Paused".equals(state) &&
                            !"Stopping".equals(state)) {
                        state = "Starting";
                        message =
                                "CameraX camera device is closed and awaiting a reopen transition.";
                    }
                    break;
                default:
                    break;
            }
        }
    }

    @OptIn(markerClass = ExperimentalCamera2Interop.class)
    private static CameraSelector exactCameraSelector(final String selectedId) {
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

    private static void handleStoppingCameraStateLocked(
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
                message =
                        "CameraX camera device is closing; restart remains blocked.";
                break;
            case CLOSED:
                ReachyCameraFrameLifecycleController.completeGracefulStopLocked();
                break;
            default:
                message =
                        "CameraX use cases are unbound; waiting for camera device CLOSED.";
                break;
        }
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("The Unity activity is required.");
        }
    }

    // Package-private (not private): called directly by
    // ReachyCameraFrameLifecycleController.failOnMain, which is the one
    // moved method that is not called with LOCK already held.
    static void requireMainThread() {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            throw new IllegalStateException(
                    "CameraX lifecycle operations must run on the Android main thread.");
        }
    }

    private static void requireSurfaceRotation(int rotation) {
        if (rotation != Surface.ROTATION_0 &&
                rotation != Surface.ROTATION_90 &&
                rotation != Surface.ROTATION_180 &&
                rotation != Surface.ROTATION_270) {
            throw new IllegalArgumentException(
                    "Android returned invalid display rotation " + rotation + ".");
        }
    }

}
