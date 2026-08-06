package com.ekkus93.weachy.camera;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.Looper;
import android.util.Size;
import android.util.SizeF;
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
import androidx.camera.core.SurfaceRequest;
import androidx.camera.core.resolutionselector.ResolutionSelector;
import androidx.camera.core.resolutionselector.ResolutionStrategy;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;
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
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

public final class ReachyCameraFrameBridge {
    private static final Object LOCK = new Object();
    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static final Executor MAIN_EXECUTOR = new Executor() {
        @Override
        public void execute(Runnable command) {
            MAIN_HANDLER.post(command);
        }
    };
    private static final Executor DIRECT_EXECUTOR = new Executor() {
        @Override
        public void execute(Runnable command) {
            command.run();
        }
    };

    private static long generation;
    private static long sessionId;
    private static long deliveredSequence;
    private static String state = "Stopped";
    private static String message = "Camera frame acquisition is stopped.";
    private static String errorCode = "";
    private static String cameraId = "";
    private static CameraDescriptor descriptor;
    private static FrameSnapshot latestFrame;
    private static ProcessCameraProvider cameraProvider;
    private static Camera boundCamera;
    private static Observer<CameraState> cameraStateObserver;
    private static CameraLifecycleOwner lifecycleOwner;
    private static Preview preview;
    private static ImageAnalysis imageAnalysis;
    private static DiscardingPreviewSurfaceProvider previewSurfaceProvider;
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

            CameraDescriptor requestedDescriptor =
                    CameraDescriptor.load(activity, requestedCameraId);
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
                lifecycleOwner = new CameraLifecycleOwner();
                lifecycleOwner.start();
                analyzerExecutor = Executors.newSingleThreadExecutor(
                        new NamedThreadFactory("reachy-camera-analysis"));
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
                        safeMessage(exception));
                return snapshotLocked();
            }
        } catch (CameraAccessException exception) {
            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "Unavailable",
                        cameraAccessCode(exception),
                        safeMessage(exception));
                return snapshotLocked();
            }
        } catch (RuntimeException exception) {
            synchronized (LOCK) {
                stopBoundUseCasesLocked();
                setInactiveLocked(
                        "Faulted",
                        "camera_start_failed",
                        safeMessage(exception));
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
                        safeMessage(exception));
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
                DiscardingPreviewSurfaceProvider nextSurfaceProvider =
                        new DiscardingPreviewSurfaceProvider();
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
                    safeMessage(exception.getCause() == null
                            ? exception
                            : exception.getCause()));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            failOnMain(
                    expectedGeneration,
                    "camera_provider_interrupted",
                    safeMessage(exception));
        } catch (RuntimeException exception) {
            failOnMain(
                    expectedGeneration,
                    "camera_bind_failed",
                    safeMessage(exception));
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
                        cameraStateErrorCode(cameraError.getCode());
                String detail = cameraStateErrorDetail(cameraError);
                if (cameraError.getType() == CameraState.ErrorType.CRITICAL) {
                    ++generation;
                    stopBoundUseCasesLocked();
                    setInactiveLocked(
                            cameraStateErrorIsUnavailable(cameraError.getCode())
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
            final CameraDescriptor frameDescriptor;
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

            FrameSnapshot frame = FrameSnapshot.from(
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
            final String failureMessage = safeMessage(exception);
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
            String detail = cameraStateErrorDetail(cameraError);
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

    private static CameraLifecycleOwner requireLifecycleOwnerLocked() {
        if (lifecycleOwner == null) {
            throw new IllegalStateException(
                    "The CameraX lifecycle owner is unavailable.");
        }
        return lifecycleOwner;
    }

    private static String cameraAccessCode(CameraAccessException exception) {
        switch (exception.getReason()) {
            case CameraAccessException.CAMERA_DISABLED:
                return "camera_disabled";
            case CameraAccessException.CAMERA_DISCONNECTED:
                return "camera_disconnected";
            case CameraAccessException.CAMERA_IN_USE:
                return "camera_in_use";
            case CameraAccessException.MAX_CAMERAS_IN_USE:
                return "max_cameras_in_use";
            case CameraAccessException.CAMERA_ERROR:
            default:
                return "camera_access_error";
        }
    }

    private static String cameraStateErrorCode(int code) {
        switch (code) {
            case CameraState.ERROR_STREAM_CONFIG:
                return "camera_stream_config";
            case CameraState.ERROR_CAMERA_IN_USE:
                return "camera_in_use";
            case CameraState.ERROR_MAX_CAMERAS_IN_USE:
                return "max_cameras_in_use";
            case CameraState.ERROR_OTHER_RECOVERABLE_ERROR:
                return "camera_recoverable_error";
            case CameraState.ERROR_CAMERA_DISABLED:
                return "camera_disabled";
            case CameraState.ERROR_CAMERA_FATAL_ERROR:
                return "camera_fatal_error";
            case CameraState.ERROR_DO_NOT_DISTURB_MODE_ENABLED:
                return "camera_do_not_disturb_enabled";
            case CameraState.ERROR_CAMERA_REMOVED:
                return "camera_removed";
            default:
                return "camera_state_error_" + code;
        }
    }

    private static boolean cameraStateErrorIsUnavailable(int code) {
        return code == CameraState.ERROR_CAMERA_IN_USE ||
                code == CameraState.ERROR_MAX_CAMERAS_IN_USE ||
                code == CameraState.ERROR_CAMERA_DISABLED ||
                code == CameraState.ERROR_DO_NOT_DISTURB_MODE_ENABLED ||
                code == CameraState.ERROR_CAMERA_REMOVED;
    }

    private static String cameraStateErrorDetail(
            CameraState.StateError cameraError) {
        Throwable cause = cameraError.getCause();
        if (cause != null) {
            return safeMessage(cause);
        }
        return "CameraX reported " +
                cameraError.getType() +
                " error " +
                cameraStateErrorCode(cameraError.getCode()) + ".";
    }

    private static String safeMessage(Throwable throwable) {
        String detail = throwable.getMessage();
        return detail == null || detail.trim().isEmpty()
                ? throwable.getClass().getSimpleName()
                : detail;
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

    private static final class CameraLifecycleOwner implements LifecycleOwner {
        private final LifecycleRegistry registry = new LifecycleRegistry(this);
        private boolean created;
        private boolean started;
        private boolean destroyed;

        CameraLifecycleOwner() {
            registry.handleLifecycleEvent(Lifecycle.Event.ON_CREATE);
            created = true;
        }

        @NonNull
        @Override
        public Lifecycle getLifecycle() {
            return registry;
        }

        void start() {
            if (destroyed || !created || started) {
                return;
            }
            registry.handleLifecycleEvent(Lifecycle.Event.ON_START);
            started = true;
        }

        void pause() {
            if (destroyed || !started) {
                return;
            }
            registry.handleLifecycleEvent(Lifecycle.Event.ON_STOP);
            started = false;
        }

        void destroy() {
            if (destroyed) {
                return;
            }
            if (started) {
                registry.handleLifecycleEvent(Lifecycle.Event.ON_STOP);
                started = false;
            }
            if (created) {
                registry.handleLifecycleEvent(Lifecycle.Event.ON_DESTROY);
                created = false;
            }
            destroyed = true;
        }
    }

    private static final class CameraDescriptor {
        final String cameraId;
        final String facing;
        final int sensorOrientationDegrees;
        final Rect activeArray;
        final FrameIntrinsics intrinsics;

        CameraDescriptor(
                String cameraId,
                String facing,
                int sensorOrientationDegrees,
                Rect activeArray,
                FrameIntrinsics intrinsics) {
            this.cameraId = cameraId;
            this.facing = facing;
            this.sensorOrientationDegrees = sensorOrientationDegrees;
            this.activeArray = new Rect(activeArray);
            this.intrinsics = intrinsics;
        }

        static CameraDescriptor load(
                Activity activity,
                String selectedCameraId) throws CameraAccessException {
            CameraManager manager = (CameraManager) activity.getSystemService(
                    Context.CAMERA_SERVICE);
            if (manager == null) {
                throw new IllegalStateException(
                        "Android returned no camera service.");
            }
            CameraCharacteristics characteristics =
                    manager.getCameraCharacteristics(selectedCameraId);
            Integer facingValue = characteristics.get(
                    CameraCharacteristics.LENS_FACING);
            Integer orientationValue = characteristics.get(
                    CameraCharacteristics.SENSOR_ORIENTATION);
            Rect activeArrayValue = characteristics.get(
                    CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE);
            if (activeArrayValue == null ||
                    activeArrayValue.width() <= 0 ||
                    activeArrayValue.height() <= 0) {
                throw new IllegalStateException(
                        "The selected camera exposes no valid active sensor array.");
            }
            int orientation = orientationValue == null ? 0 : orientationValue;
            if (orientation != 0 && orientation != 90 &&
                    orientation != 180 && orientation != 270) {
                throw new IllegalStateException(
                        "The selected camera exposes an invalid sensor orientation: " +
                        orientation);
            }
            return new CameraDescriptor(
                    selectedCameraId,
                    facingLabel(facingValue),
                    orientation,
                    activeArrayValue,
                    FrameIntrinsics.from(characteristics, activeArrayValue));
        }

        private static String facingLabel(Integer facing) {
            if (facing == null) {
                return "unknown";
            }
            if (facing == CameraCharacteristics.LENS_FACING_FRONT) {
                return "front";
            }
            if (facing == CameraCharacteristics.LENS_FACING_BACK) {
                return "rear";
            }
            if (facing == CameraCharacteristics.LENS_FACING_EXTERNAL) {
                return "external";
            }
            return "unknown";
        }
    }

    private static final class FrameIntrinsics {
        final String source;
        final float focalLengthX;
        final float focalLengthY;
        final float principalPointX;
        final float principalPointY;
        final float skew;
        final String provenance;

        FrameIntrinsics(
                String source,
                float focalLengthX,
                float focalLengthY,
                float principalPointX,
                float principalPointY,
                float skew,
                String provenance) {
            this.source = source;
            this.focalLengthX = focalLengthX;
            this.focalLengthY = focalLengthY;
            this.principalPointX = principalPointX;
            this.principalPointY = principalPointY;
            this.skew = skew;
            this.provenance = provenance;
        }

        static FrameIntrinsics from(
                CameraCharacteristics characteristics,
                Rect activeArray) {
            float[] calibration = characteristics.get(
                    CameraCharacteristics.LENS_INTRINSIC_CALIBRATION);
            if (calibration != null && calibration.length >= 5 &&
                    calibration[0] > 0.0f && calibration[1] > 0.0f &&
                    isFinite(calibration[0]) && isFinite(calibration[1]) &&
                    isFinite(calibration[2]) && isFinite(calibration[3]) &&
                    isFinite(calibration[4])) {
                return new FrameIntrinsics(
                        "android_calibration",
                        calibration[0],
                        calibration[1],
                        calibration[2],
                        calibration[3],
                        calibration[4],
                        "Camera2 LENS_INTRINSIC_CALIBRATION in active-sensor-array coordinates");
            }

            SizeF physicalSize = characteristics.get(
                    CameraCharacteristics.SENSOR_INFO_PHYSICAL_SIZE);
            float[] focalLengths = characteristics.get(
                    CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS);
            if (physicalSize != null &&
                    physicalSize.getWidth() > 0.0f &&
                    physicalSize.getHeight() > 0.0f &&
                    focalLengths != null && focalLengths.length > 0 &&
                    focalLengths[0] > 0.0f && isFinite(focalLengths[0])) {
                float fx = focalLengths[0] *
                        activeArray.width() / physicalSize.getWidth();
                float fy = focalLengths[0] *
                        activeArray.height() / physicalSize.getHeight();
                return new FrameIntrinsics(
                        "uncalibrated_pinhole_estimate",
                        fx,
                        fy,
                        activeArray.exactCenterX(),
                        activeArray.exactCenterY(),
                        0.0f,
                        "Uncalibrated pinhole estimate from Camera2 physical sensor size, available focal length, and active sensor array");
            }

            return new FrameIntrinsics(
                    "uncalibrated_pinhole_estimate",
                    (float) activeArray.width(),
                    (float) activeArray.height(),
                    activeArray.exactCenterX(),
                    activeArray.exactCenterY(),
                    0.0f,
                    "Uncalibrated normalized pinhole fallback derived only from the active sensor array; persist checkerboard calibration before metric reprojection");
        }

        JSONObject toJson(Rect activeArray) throws JSONException {
            JSONObject value = new JSONObject();
            value.put("source", source);
            value.put("fx", focalLengthX);
            value.put("fy", focalLengthY);
            value.put("cx", principalPointX);
            value.put("cy", principalPointY);
            value.put("skew", skew);
            value.put("coordinateSpace", "active_sensor_array");
            value.put("activeArrayLeft", activeArray.left);
            value.put("activeArrayTop", activeArray.top);
            value.put("activeArrayRight", activeArray.right);
            value.put("activeArrayBottom", activeArray.bottom);
            value.put("provenance", provenance);
            return value;
        }

        private static boolean isFinite(float value) {
            return !Float.isNaN(value) && !Float.isInfinite(value);
        }
    }

    private static final class FrameSnapshot {
        final long sessionId;
        final long sequence;
        final long timestampNanoseconds;
        final String cameraId;
        final String facing;
        final int sensorOrientationDegrees;
        final int rotationDegrees;
        final int width;
        final int height;
        final Rect crop;
        final FrameIntrinsics intrinsics;
        final Rect activeArray;
        boolean imagePlanesAccessed;
        boolean cpuPixelCopyPerformed;
        boolean textureFramePublished;
        boolean textureFrameStale;
        boolean mirrored;
        String colorStandard = "unknown";
        String colorRange = "unknown";
        String textureDetail = "Texture publication has not run.";

        FrameSnapshot(
                long sessionId,
                long sequence,
                long timestampNanoseconds,
                String cameraId,
                String facing,
                int sensorOrientationDegrees,
                int rotationDegrees,
                int width,
                int height,
                Rect crop,
                FrameIntrinsics intrinsics,
                Rect activeArray) {
            this.sessionId = sessionId;
            this.sequence = sequence;
            this.timestampNanoseconds = timestampNanoseconds;
            this.cameraId = cameraId;
            this.facing = facing;
            this.sensorOrientationDegrees = sensorOrientationDegrees;
            this.rotationDegrees = rotationDegrees;
            this.width = width;
            this.height = height;
            this.crop = new Rect(crop);
            this.intrinsics = intrinsics;
            this.activeArray = new Rect(activeArray);
        }

        static FrameSnapshot from(
                ImageProxy imageProxy,
                long frameSessionId,
                long frameSequence,
                CameraDescriptor cameraDescriptor) {
            if (imageProxy.getFormat() != ImageFormat.YUV_420_888) {
                throw new IllegalStateException(
                        "CameraX ImageAnalysis returned unexpected format " +
                        imageProxy.getFormat() + ".");
            }
            long timestamp = imageProxy.getImageInfo().getTimestamp();
            int rotation = imageProxy.getImageInfo().getRotationDegrees();
            Rect frameCrop = imageProxy.getCropRect();
            if (timestamp <= 0L) {
                throw new IllegalStateException(
                        "CameraX returned a nonpositive frame timestamp.");
            }
            if (rotation != 0 && rotation != 90 &&
                    rotation != 180 && rotation != 270) {
                throw new IllegalStateException(
                        "CameraX returned invalid frame rotation " + rotation + ".");
            }
            if (frameCrop.left < 0 || frameCrop.top < 0 ||
                    frameCrop.right > imageProxy.getWidth() ||
                    frameCrop.bottom > imageProxy.getHeight() ||
                    frameCrop.width() <= 0 || frameCrop.height() <= 0) {
                throw new IllegalStateException(
                        "CameraX returned an invalid frame crop " + frameCrop + ".");
            }
            return new FrameSnapshot(
                    frameSessionId,
                    frameSequence,
                    timestamp,
                    cameraDescriptor.cameraId,
                    cameraDescriptor.facing,
                    cameraDescriptor.sensorOrientationDegrees,
                    rotation,
                    imageProxy.getWidth(),
                    imageProxy.getHeight(),
                    frameCrop,
                    cameraDescriptor.intrinsics,
                    cameraDescriptor.activeArray);
        }

        void applyTexturePublication(
                ReachyCameraTextureFrameBridge.Publication publication) {
            if (publication == null) {
                throw new IllegalArgumentException(
                        "Texture publication diagnostics are required.");
            }
            imagePlanesAccessed = publication.imagePlanesAccessed;
            cpuPixelCopyPerformed = publication.cpuPixelCopyPerformed;
            textureFramePublished = publication.textureFramePublished;
            textureFrameStale = publication.stale;
            mirrored = publication.mirrored;
            colorStandard = publication.colorStandard;
            colorRange = publication.colorRange;
            textureDetail = publication.detail;
        }

        JSONObject toJson() throws JSONException {
            JSONObject value = new JSONObject();
            value.put("sessionId", sessionId);
            value.put("sequence", sequence);
            value.put("timestampNanoseconds", timestampNanoseconds);
            value.put("cameraId", cameraId);
            value.put("facing", facing);
            value.put("sensorOrientationDegrees", sensorOrientationDegrees);
            value.put("rotationDegrees", rotationDegrees);
            value.put("width", width);
            value.put("height", height);
            JSONObject cropValue = new JSONObject();
            cropValue.put("left", crop.left);
            cropValue.put("top", crop.top);
            cropValue.put("right", crop.right);
            cropValue.put("bottom", crop.bottom);
            value.put("crop", cropValue);
            value.put("pixelFormat", "YUV_420_888");
            value.put("intrinsics", intrinsics.toJson(activeArray));
            value.put("imagePlanesAccessed", imagePlanesAccessed);
            value.put("cpuPixelCopyPerformed", cpuPixelCopyPerformed);
            value.put("textureFramePublished", textureFramePublished);
            value.put("textureFrameStale", textureFrameStale);
            value.put("mirrored", mirrored);
            value.put("colorStandard", colorStandard);
            value.put("colorRange", colorRange);
            value.put("textureDetail", textureDetail);
            return value;
        }
    }

    private static final class DiscardingPreviewSurfaceProvider implements
            Preview.SurfaceProvider,
            AutoCloseable {
        private final Object providerLock = new Object();
        private final HandlerThread handlerThread;
        private final Handler handler;
        private PreviewSurfaceLease activeLease;
        private boolean closed;

        DiscardingPreviewSurfaceProvider() {
            handlerThread = new HandlerThread("reachy-camera-preview-sink");
            handlerThread.start();
            handler = new Handler(handlerThread.getLooper());
        }

        @Override
        public void onSurfaceRequested(@NonNull SurfaceRequest request) {
            synchronized (providerLock) {
                if (closed) {
                    request.willNotProvideSurface();
                    return;
                }
                if (activeLease != null) {
                    throw new IllegalStateException(
                            "CameraX requested a new preview surface before completing the previous request.");
                }
                Size resolution = request.getResolution();
                ImageReader reader = ImageReader.newInstance(
                        resolution.getWidth(),
                        resolution.getHeight(),
                        ImageFormat.PRIVATE,
                        2);
                final PreviewSurfaceLease lease =
                        new PreviewSurfaceLease(reader);
                activeLease = lease;
                reader.setOnImageAvailableListener(
                        new ImageReader.OnImageAvailableListener() {
                            @Override
                            public void onImageAvailable(ImageReader source) {
                                Image image = null;
                                try {
                                    image = source.acquireLatestImage();
                                } finally {
                                    if (image != null) {
                                        image.close();
                                    }
                                }
                            }
                        },
                        handler);
                request.provideSurface(
                        reader.getSurface(),
                        DIRECT_EXECUTOR,
                        result -> {
                            lease.close();
                            synchronized (providerLock) {
                                if (activeLease == lease) {
                                    activeLease = null;
                                }
                            }
                        });
            }
        }

        @Override
        public void close() {
            synchronized (providerLock) {
                if (closed) {
                    return;
                }
                closed = true;
                if (activeLease != null) {
                    activeLease.close();
                    activeLease = null;
                }
            }
            handlerThread.quitSafely();
        }
    }

    private static final class PreviewSurfaceLease implements AutoCloseable {
        private final ImageReader reader;
        private final AtomicBoolean closed = new AtomicBoolean();

        PreviewSurfaceLease(ImageReader reader) {
            this.reader = reader;
        }

        @Override
        public void close() {
            if (closed.compareAndSet(false, true)) {
                reader.setOnImageAvailableListener(null, null);
                reader.close();
            }
        }
    }

    private static final class NamedThreadFactory implements ThreadFactory {
        private final String baseName;
        private final AtomicInteger count = new AtomicInteger();

        NamedThreadFactory(String baseName) {
            this.baseName = baseName;
        }

        @Override
        public Thread newThread(Runnable runnable) {
            Thread thread = new Thread(
                    runnable,
                    baseName + "-" + count.incrementAndGet());
            thread.setDaemon(true);
            return thread;
        }
    }
}
