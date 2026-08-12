package com.ekkus93.weachy.camera;

import androidx.camera.core.ImageProxy;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * Per-frame analysis callback and JSON snapshot serialization for
 * {@link ReachyCameraFrameBridge}.
 *
 * <p>{@code analyzeFrame} is invoked directly by CameraX's {@code ImageAnalysis.Analyzer}
 * callback without {@link ReachyCameraFrameBridge#LOCK} held; it acquires the lock itself
 * for each of its two critical sections. {@code snapshotLocked} assumes the caller already
 * holds {@code ReachyCameraFrameBridge.LOCK}.
 */
final class ReachyCameraFrameAnalyzer {

    private ReachyCameraFrameAnalyzer() {
    }

    static void analyzeFrame(
            ImageProxy imageProxy,
            long expectedGeneration) {
        try {
            final long frameSession;
            final long frameSequence;
            final ReachyCameraDescriptor frameDescriptor;
            synchronized (ReachyCameraFrameBridge.LOCK) {
                if (expectedGeneration != ReachyCameraFrameBridge.generation ||
                        !"Running".equals(ReachyCameraFrameBridge.state) ||
                        ReachyCameraFrameBridge.descriptor == null) {
                    return;
                }
                frameSession = ReachyCameraFrameBridge.sessionId;
                frameSequence = ++ReachyCameraFrameBridge.deliveredSequence;
                frameDescriptor = ReachyCameraFrameBridge.descriptor;
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
            synchronized (ReachyCameraFrameBridge.LOCK) {
                if (expectedGeneration != ReachyCameraFrameBridge.generation ||
                        !"Running".equals(ReachyCameraFrameBridge.state) ||
                        frameSession != ReachyCameraFrameBridge.sessionId) {
                    return;
                }
                ReachyCameraFrameBridge.latestFrame = frame;
                ReachyCameraFrameBridge.message = texturePublication.textureFramePublished
                        ? "Camera frame " + frameSequence +
                            " copied once into a detached direct YUV texture slot."
                        : texturePublication.detail;
            }
        } catch (RuntimeException exception) {
            final String failureMessage = ReachyCameraErrorUtil.safeMessage(exception);
            ReachyCameraFrameBridge.MAIN_HANDLER.post(new Runnable() {
                @Override
                public void run() {
                    ReachyCameraFrameLifecycleController.failOnMain(
                            expectedGeneration,
                            "camera_frame_metadata_failed",
                            failureMessage);
                }
            });
        } finally {
            imageProxy.close();
        }
    }

    static String snapshotLocked() {
        try {
            JSONObject root = new JSONObject();
            root.put("status", "Faulted".equals(ReachyCameraFrameBridge.state) ? "error" : "ok");
            root.put("state", ReachyCameraFrameBridge.state);
            root.put("errorCode", ReachyCameraFrameBridge.errorCode);
            root.put("message", ReachyCameraFrameBridge.message);
            root.put("sessionId", ReachyCameraFrameBridge.sessionId);
            root.put("cameraId", ReachyCameraFrameBridge.cameraId);
            root.put("facing", ReachyCameraFrameBridge.descriptor == null
                    ? "unknown"
                    : ReachyCameraFrameBridge.descriptor.facing);
            root.put("analysisBackpressure", "keep_only_latest");
            root.put("previewSink", "analysis_yuv_gpu_texture_bridge");
            root.put(
                    "cpuPixelCopyPerformed",
                    ReachyCameraFrameBridge.latestFrame != null &&
                            ReachyCameraFrameBridge.latestFrame.cpuPixelCopyPerformed);
            root.put(
                    "textureBridge",
                    ReachyCameraTextureFrameBridge.snapshotJson());
            root.put("latestFrame", ReachyCameraFrameBridge.latestFrame == null
                    ? JSONObject.NULL
                    : ReachyCameraFrameBridge.latestFrame.toJson());
            return root.toString();
        } catch (JSONException exception) {
            return "{\"status\":\"error\",\"state\":\"Faulted\",\"errorCode\":\"json_encoding_failed\",\"message\":\"Camera acquisition failed while encoding diagnostics.\",\"sessionId\":0,\"cameraId\":\"\",\"facing\":\"unknown\",\"analysisBackpressure\":\"keep_only_latest\",\"previewSink\":\"analysis_yuv_gpu_texture_bridge\",\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
        }
    }
}
