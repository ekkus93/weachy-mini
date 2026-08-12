package com.ekkus93.weachy.camera;

import android.graphics.ImageFormat;
import android.graphics.Rect;

import androidx.camera.core.ImageProxy;

import org.json.JSONException;
import org.json.JSONObject;

final class ReachyFrameSnapshot {
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
    final ReachyFrameIntrinsics intrinsics;
    final Rect activeArray;
    boolean imagePlanesAccessed;
    boolean cpuPixelCopyPerformed;
    boolean textureFramePublished;
    boolean textureFrameStale;
    boolean mirrored;
    String colorStandard = "unknown";
    String colorRange = "unknown";
    String textureDetail = "Texture publication has not run.";

    ReachyFrameSnapshot(
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
            ReachyFrameIntrinsics intrinsics,
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

    static ReachyFrameSnapshot from(
            ImageProxy imageProxy,
            long frameSessionId,
            long frameSequence,
            ReachyCameraDescriptor cameraDescriptor) {
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
        return new ReachyFrameSnapshot(
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
