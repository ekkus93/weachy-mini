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

import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.Executor;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class ReachyCameraFrameBridge {
    private static final Object LOCK = new Object();
    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static final Executor MAIN_EXECUTOR = new Executor() {
        @Override
        public void execute(Runnable command) {
            MAIN_HANDLER.post(command);
        }
    };
    private static long generation;
    private static long sessionId;
    private static long deliveredSequence;
    private static String state = "Stopped";
    private static String message = "Camera frame acquisition is stopped.";
    private static String errorCode = "";
    private static String cameraId = "";
    private static ReachyCameraDescriptor descriptor;
    private static ReachyFrameSnapshot latestFrame;
    private static ProcessCameraProvider cameraProvider;
    private static Camera boundCamera;
    private static Observer<CameraState> cameraStateObserver;
    private static ReachyCameraLifecycleOwner lifecycleOwner;
    private static Preview preview;
    private static ImageAnalysis imageAnalysis;
    private static ReachyDiscardingPreviewSurfaceProvider previewSurfaceProvider;
    private static ExecutorService analyzerExecutor;

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
                    stopBoundUseCasesLocked();
                    setInactiveLocked(
                            "PermissionRevoked",
                            "permission_denied",
                            "Android camera permission is not granted.");
                    return snapshotLocked();
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
                    return snapshotLocked();
                }
                stopBoundUseCasesLocked();
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
                return snapshotLocked();
            }
        } catch (SecurityException exception) {
            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "PermissionRevoked",
                        "permission_denied",
                        ReachyCameraErrorUtil.safeMessage(exception));
                return snapshotLocked();
            }
        } catch (CameraAccessException exception) {
            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "Unavailable",
                        ReachyCameraErrorUtil.cameraAccessCode(exception),
                        ReachyCameraErrorUtil.safeMessage(exception));
                return snapshotLocked();
            }
        } catch (RuntimeException exception) {
            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "Faulted",
                        "camera_start_failed",
                        ReachyCameraErrorUtil.safeMessage(exception));
                return snapshotLocked();
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
            return snapshotLocked();
        }
    }

    public static String resume(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (activity.checkSelfPermission(Manifest.permission.CAMERA)
                    != PackageManager.PERMISSION_GRANTED) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "PermissionRevoked",
                        "permission_denied",
                        "Camera permission was revoked while acquisition was paused.");
                return snapshotLocked();
            }
            if (lifecycleOwner != null && "Paused".equals(state)) {
                lifecycleOwner.start();
                state = "Starting";
                errorCode = "";
                message =
                        "CameraX lifecycle resumed; waiting for the camera device to reopen.";
            }
            return snapshotLocked();
        }
    }

    public static String stop(Activity activity) {
        requireMainThread();
        requireActivity(activity);
        synchronized (LOCK) {
            if (!isActiveState(state) || "Stopping".equals(state)) {
                return snapshotLocked();
            }
            state = "Stopping";
            message =
                    "CameraX use cases are unbinding; waiting for camera device CLOSED.";
            errorCode = "";
            ++generation;
            try {
                beginGracefulStopLocked();
            } catch (RuntimeException exception) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "Faulted",
                        "camera_stop_failed",
                        ReachyCameraErrorUtil.safeMessage(exception));
            }
            return snapshotLocked();
        }
    }

    public static String snapshot() {
        synchronized (LOCK) {
            return snapshotLocked();
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
            stopBoundUseCasesLocked();
            setInactiveLocked(
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
                        requireAnalyzerExecutorLocked(),
                        new ImageAnalysis.Analyzer() {
                            @Override
                            public void analyze(@NonNull ImageProxy imageProxy) {
                                analyzeFrame(imageProxy, analyzerGeneration);
                            }
                        });

                CameraSelector selector = exactCameraSelector(requestedCameraId);
                Camera nextCamera = provider.bindToLifecycle(
                        requireLifecycleOwnerLocked(),
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
            failOnMain(
                    expectedGeneration,
                    "camera_provider_failed",
                    ReachyCameraErrorUtil.safeMessage(exception.getCause() == null
                            ? exception
                            : exception.getCause()));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            failOnMain(
                    expectedGeneration,
                    "camera_provider_interrupted",
                    ReachyCameraErrorUtil.safeMessage(exception));
        } catch (RuntimeException exception) {
            failOnMain(
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
                    !isActiveState(state)) {
                return;
            }

            CameraState.StateError cameraError = cameraState.getError();
            if (cameraError != null) {
                String nextErrorCode =
                        ReachyCameraErrorUtil.cameraStateErrorCode(cameraError.getCode());
                String detail = ReachyCameraErrorUtil.cameraStateErrorDetail(cameraError);
                if (cameraError.getType() == CameraState.ErrorType.CRITICAL) {
                    ++generation;
                    stopBoundUseCasesLocked();
                    setInactiveLocked(
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

    private static void analyzeFrame(
            ImageProxy imageProxy,
            long expectedGeneration) {
        try {
            final long frameSession;
            final long frameSequence;
            final ReachyCameraDescriptor frameDescriptor;
            synchronized (LOCK) {
                if (expectedGeneration != generation ||
                        !"Running".equals(state) ||
                        descriptor == null) {
                    return;
                }
                frameSession = sessionId;
                frameSequence = ++deliveredSequence;
                frameDescriptor = descriptor;
            }

            ReachyFrameSnapshot frame = ReachyFrameSnapshot.from(
                    imageProxy,
                    frameSession,
                    frameSequence,
                    frameDescriptor);
            ReachyCameraTextureFrameBridge.Publication texturePublication =
                    ReachyCameraTextureFrameBridge.publish(
                            imageProxy,
                            expectedGeneration,
                            frame.sessionId,
                            frame.sequence,
                            frame.timestampNanoseconds,
                            frame.cameraId,
                            frame.facing,
                            frame.sensorOrientationDegrees,
                            frame.rotationDegrees,
                            frame.crop);
            frame.applyTexturePublication(texturePublication);
            synchronized (LOCK) {
                if (expectedGeneration != generation ||
                        !"Running".equals(state) ||
                        frameSession != sessionId) {
                    return;
                }
                latestFrame = frame;
                message = texturePublication.textureFramePublished
                        ? "Camera frame " + frameSequence +
                            " copied once into a detached direct YUV texture slot."
                        : texturePublication.detail;
            }
        } catch (RuntimeException exception) {
            final String failureMessage = ReachyCameraErrorUtil.safeMessage(exception);
            MAIN_HANDLER.post(new Runnable() {
                @Override
                public void run() {
                    failOnMain(
                            expectedGeneration,
                            "camera_frame_metadata_failed",
                            failureMessage);
                }
            });
        } finally {
            imageProxy.close();
        }
    }

    private static void failOnMain(
            long expectedGeneration,
            String code,
            String detail) {
        requireMainThread();
        synchronized (LOCK) {
            if (expectedGeneration != generation) {
                return;
            }
            ++generation;
            stopBoundUseCasesLocked();
            setInactiveLocked("Faulted", code, detail);
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

    private static void beginGracefulStopLocked() {
        if (boundCamera == null) {
            stopBoundUseCasesLocked();
            setInactiveLocked(
                    "Stopped",
                    "",
                    "No CameraX device was bound; stop completed without a close transition.");
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
        if (!"Stopping".equals(state) || boundCamera == null) {
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
            String detail = ReachyCameraErrorUtil.cameraStateErrorDetail(cameraError);
            stopBoundUseCasesLocked();
            setInactiveLocked(
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
                completeGracefulStopLocked();
                break;
            default:
                message =
                        "CameraX use cases are unbound; waiting for camera device CLOSED.";
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
                "Stopped",
                "",
                "CameraX camera device reached CLOSED; Preview and ImageAnalysis are fully released.");
    }

    private static void stopBoundUseCasesLocked() {
        if (boundCamera != null && cameraStateObserver != null) {
            boundCamera.getCameraInfo()
                    .getCameraState()
                    .removeObserver(cameraStateObserver);
        }
        cameraStateObserver = null;
        boundCamera = null;
        if (imageAnalysis != null) {
            imageAnalysis.clearAnalyzer();
        }
        if (preview != null) {
            preview.setSurfaceProvider((Preview.SurfaceProvider) null);
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
        if (lifecycleOwner != null) {
            lifecycleOwner.destroy();
        }
        if (previewSurfaceProvider != null) {
            previewSurfaceProvider.close();
        }
        if (analyzerExecutor != null) {
            analyzerExecutor.shutdownNow();
        }
        ReachyCameraTextureFrameBridge.endSession(generation);
        cameraProvider = null;
        lifecycleOwner = null;
        preview = null;
        imageAnalysis = null;
        previewSurfaceProvider = null;
        analyzerExecutor = null;
        descriptor = null;
        latestFrame = null;
        deliveredSequence = 0L;
    }

    private static void setInactiveLocked(
            String nextState,
            String nextErrorCode,
            String nextMessage) {
        state = nextState;
        errorCode = nextErrorCode == null ? "" : nextErrorCode;
        message = nextMessage == null || nextMessage.trim().isEmpty()
                ? "Camera acquisition changed state without diagnostics."
                : nextMessage;
        sessionId = 0L;
        cameraId = "";
        descriptor = null;
        latestFrame = null;
        deliveredSequence = 0L;
    }

    private static String snapshotLocked() {
        try {
            JSONObject root = new JSONObject();
            root.put("status", "Faulted".equals(state) ? "error" : "ok");
            root.put("state", state);
            root.put("errorCode", errorCode);
            root.put("message", message);
            root.put("sessionId", sessionId);
            root.put("cameraId", cameraId);
            root.put("facing", descriptor == null ? "unknown" : descriptor.facing);
            root.put("analysisBackpressure", "keep_only_latest");
            root.put("previewSink", "analysis_yuv_gpu_texture_bridge");
            root.put(
                    "cpuPixelCopyPerformed",
                    latestFrame != null && latestFrame.cpuPixelCopyPerformed);
            root.put(
                    "textureBridge",
                    ReachyCameraTextureFrameBridge.snapshotJson());
            root.put("latestFrame", latestFrame == null
                    ? JSONObject.NULL
                    : latestFrame.toJson());
            return root.toString();
        } catch (JSONException exception) {
            return "{\"status\":\"error\",\"state\":\"Faulted\",\"errorCode\":\"json_encoding_failed\",\"message\":\"Camera acquisition failed while encoding diagnostics.\",\"sessionId\":0,\"cameraId\":\"\",\"facing\":\"unknown\",\"analysisBackpressure\":\"keep_only_latest\",\"previewSink\":\"analysis_yuv_gpu_texture_bridge\",\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
        }
    }

    private static boolean isActiveState(String value) {
        return "Starting".equals(value) ||
                "Running".equals(value) ||
                "Paused".equals(value) ||
                "Stopping".equals(value);
    }

    private static ExecutorService requireAnalyzerExecutorLocked() {
        if (analyzerExecutor == null) {
            throw new IllegalStateException(
                    "The CameraX analyzer executor is unavailable.");
        }
        return analyzerExecutor;
    }

    private static ReachyCameraLifecycleOwner requireLifecycleOwnerLocked() {
        if (lifecycleOwner == null) {
            throw new IllegalStateException(
                    "The CameraX lifecycle owner is unavailable.");
        }
        return lifecycleOwner;
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("The Unity activity is required.");
        }
    }

    private static void requireMainThread() {
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
