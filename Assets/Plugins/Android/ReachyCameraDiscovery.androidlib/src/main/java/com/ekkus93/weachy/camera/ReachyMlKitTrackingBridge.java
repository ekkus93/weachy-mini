package com.ekkus93.weachy.camera;

import android.graphics.Bitmap;
import android.graphics.Rect;

import com.google.android.gms.tasks.Task;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.face.Face;
import com.google.mlkit.vision.face.FaceDetection;
import com.google.mlkit.vision.face.FaceDetector;
import com.google.mlkit.vision.face.FaceDetectorOptions;
import com.google.mlkit.vision.segmentation.Segmentation;
import com.google.mlkit.vision.segmentation.SegmentationMask;
import com.google.mlkit.vision.segmentation.Segmenter;
import com.google.mlkit.vision.segmentation.selfie.SelfieSegmenterOptions;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.List;

public final class ReachyMlKitTrackingBridge implements AutoCloseable {
    public interface Callback {
        void onSuccess(String requestId, String payload);

        void onFailure(String requestId, String code, String message);
    }

    private static final double PERSON_THRESHOLD = 0.65;
    private static final int MAX_PIXEL_COUNT = 2048 * 2048;

    private final Object lock = new Object();
    private final FaceDetector faceDetector;
    private final Segmenter personSegmenter;
    private RequestState activeRequest;
    private boolean closed;

    public ReachyMlKitTrackingBridge() {
        FaceDetectorOptions faceOptions =
                new FaceDetectorOptions.Builder()
                        .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                        .setLandmarkMode(FaceDetectorOptions.LANDMARK_MODE_NONE)
                        .setContourMode(FaceDetectorOptions.CONTOUR_MODE_NONE)
                        .setClassificationMode(FaceDetectorOptions.CLASSIFICATION_MODE_NONE)
                        .enableTracking()
                        .setMinFaceSize(0.10f)
                        .build();
        SelfieSegmenterOptions personOptions =
                new SelfieSegmenterOptions.Builder()
                        .setDetectorMode(SelfieSegmenterOptions.SINGLE_IMAGE_MODE)
                        .build();
        faceDetector = FaceDetection.getClient(faceOptions);
        personSegmenter = Segmentation.getClient(personOptions);
    }

    public void detect(
            String requestId,
            byte[] rgbaTopLeft,
            int width,
            int height,
            Callback callback) {
        requireText(requestId, "requestId");
        if (rgbaTopLeft == null) {
            throw new IllegalArgumentException("rgbaTopLeft is required");
        }
        if (callback == null) {
            throw new IllegalArgumentException("callback is required");
        }
        int pixelCount = checkedPixelCount(width, height);
        int expectedLength = Math.multiplyExact(pixelCount, 4);
        if (rgbaTopLeft.length != expectedLength) {
            throw new IllegalArgumentException(
                    "RGBA input length does not match its dimensions");
        }

        Bitmap bitmap = createBitmap(rgbaTopLeft, width, height);
        RequestState state;
        synchronized (lock) {
            if (closed) {
                bitmap.recycle();
                callback.onFailure(requestId, "closed", "ML Kit tracking bridge is closed.");
                return;
            }
            if (activeRequest != null) {
                bitmap.recycle();
                callback.onFailure(
                        requestId,
                        "busy",
                        "ML Kit tracking already has an in-flight request; requests are not queued.");
                return;
            }
            state = new RequestState(requestId, bitmap, callback);
            activeRequest = state;
        }

        InputImage image = InputImage.fromBitmap(bitmap, 0);
        Task<List<Face>> faceTask;
        try {
            faceTask = faceDetector.process(image);
        } catch (Exception error) {
            failImmediately(state, "face_detection_start_failed", error);
            return;
        }
        faceTask
                .addOnSuccessListener(faces -> completeFaces(state, faces))
                .addOnFailureListener(error -> failPart(state, "face_detection_failed", error));

        Task<SegmentationMask> personTask;
        try {
            personTask = personSegmenter.process(image);
        } catch (Exception error) {
            failPart(state, "person_segmentation_start_failed", error);
            return;
        }
        personTask
                .addOnSuccessListener(mask -> completePerson(state, mask))
                .addOnFailureListener(error -> failPart(state, "person_segmentation_failed", error));
    }

    public void cancel(String requestId) {
        requireText(requestId, "requestId");
        synchronized (lock) {
            if (activeRequest != null
                    && activeRequest.requestId.equals(requestId)) {
                activeRequest.cancelled = true;
            }
        }
    }

    @Override
    public void close() {
        synchronized (lock) {
            if (closed) {
                return;
            }
            closed = true;
            if (activeRequest != null) {
                activeRequest.cancelled = true;
            }
        }
        faceDetector.close();
        personSegmenter.close();
    }

    private void completeFaces(RequestState state, List<Face> faces) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            state.faces = faces;
            state.completedParts++;
            finishIfCompleteLocked(state);
        }
    }

    private void completePerson(RequestState state, SegmentationMask mask) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            state.person = personDetection(mask);
            state.completedParts++;
            finishIfCompleteLocked(state);
        }
    }

    private void failImmediately(
            RequestState state,
            String code,
            Exception error) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            activeRequest = null;
            try {
                if (!state.cancelled && !closed) {
                    state.callback.onFailure(
                            state.requestId,
                            code,
                            safeMessage(error));
                }
            } finally {
                state.bitmap.recycle();
            }
        }
    }

    private void failPart(RequestState state, String code, Exception error) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            if (state.errorCode == null) {
                state.errorCode = code;
                state.errorMessage = safeMessage(error);
            }
            state.completedParts++;
            finishIfCompleteLocked(state);
        }
    }

    private void finishIfCompleteLocked(RequestState state) {
        if (state.completedParts != 2) {
            return;
        }
        activeRequest = null;
        try {
            if (state.cancelled || closed) {
                return;
            }
            if (state.errorCode != null) {
                state.callback.onFailure(
                        state.requestId,
                        state.errorCode,
                        state.errorMessage);
                return;
            }
            state.callback.onSuccess(
                    state.requestId,
                    buildPayload(state));
        } catch (JSONException error) {
            state.callback.onFailure(
                    state.requestId,
                    "payload_failed",
                    safeMessage(error));
        } finally {
            state.bitmap.recycle();
        }
    }

    private boolean isCurrent(RequestState state) {
        return activeRequest == state && state.completedParts < 2;
    }

    private static String buildPayload(RequestState state)
            throws JSONException {
        JSONArray detections = new JSONArray();
        if (state.faces != null) {
            for (Face face : state.faces) {
                Rect bounds = face.getBoundingBox();
                Integer trackingId = face.getTrackingId();
                detections.put(detectionJson(
                        "face",
                        trackingId == null ? "" : "face:" + trackingId,
                        1.0,
                        bounds.left,
                        bounds.top,
                        bounds.width(),
                        bounds.height(),
                        state.bitmap.getWidth(),
                        state.bitmap.getHeight()));
            }
        }
        if (state.person != null) {
            PersonDetection person = state.person;
            detections.put(detectionJson(
                    "person",
                    "",
                    person.confidence,
                    person.left,
                    person.top,
                    person.width,
                    person.height,
                    person.imageWidth,
                    person.imageHeight));
        }

        JSONObject payload = new JSONObject();
        payload.put("schema_version", 1);
        payload.put("request_id", state.requestId);
        payload.put("backend", "google-mlkit-bundled-face-selfie");
        payload.put("detections", detections);
        return payload.toString();
    }

    private static JSONObject detectionJson(
            String classification,
            String providerTrackingId,
            double confidence,
            int left,
            int top,
            int width,
            int height,
            int imageWidth,
            int imageHeight) throws JSONException {
        double normalizedLeft = clamp01((double) left / imageWidth);
        double normalizedTop = clamp01((double) top / imageHeight);
        double normalizedRight = clamp01(
                (double) Math.addExact(left, width) / imageWidth);
        double normalizedBottom = clamp01(
                (double) Math.addExact(top, height) / imageHeight);
        double normalizedWidth = normalizedRight - normalizedLeft;
        double normalizedHeight = normalizedBottom - normalizedTop;
        if (normalizedWidth <= 0.0 || normalizedHeight <= 0.0) {
            throw new JSONException("Detection bounds are empty after normalization.");
        }

        JSONObject detection = new JSONObject();
        detection.put("classification", classification);
        detection.put("provider_tracking_id", providerTrackingId);
        detection.put("confidence", clamp01(confidence));
        detection.put("left", normalizedLeft);
        detection.put("top", normalizedTop);
        detection.put("width", normalizedWidth);
        detection.put("height", normalizedHeight);
        return detection;
    }

    private static PersonDetection personDetection(SegmentationMask mask) {
        int width = mask.getWidth();
        int height = mask.getHeight();
        int pixelCount = checkedPixelCount(width, height);
        ByteBuffer byteBuffer = mask.getBuffer().duplicate();
        byteBuffer.order(ByteOrder.nativeOrder());
        FloatBuffer values = byteBuffer.asFloatBuffer();
        if (values.remaining() < pixelCount) {
            throw new IllegalStateException(
                    "Selfie segmentation mask is shorter than its dimensions.");
        }

        int minimumX = width;
        int minimumY = height;
        int maximumX = -1;
        int maximumY = -1;
        int accepted = 0;
        double totalConfidence = 0.0;
        for (int y = 0; y < height; ++y) {
            for (int x = 0; x < width; ++x) {
                float confidence = values.get();
                if (confidence >= PERSON_THRESHOLD) {
                    minimumX = Math.min(minimumX, x);
                    minimumY = Math.min(minimumY, y);
                    maximumX = Math.max(maximumX, x);
                    maximumY = Math.max(maximumY, y);
                    accepted++;
                    totalConfidence += confidence;
                }
            }
        }
        if (accepted == 0) {
            return null;
        }
        return new PersonDetection(
                minimumX,
                minimumY,
                maximumX - minimumX + 1,
                maximumY - minimumY + 1,
                width,
                height,
                totalConfidence / accepted);
    }

    private static Bitmap createBitmap(
            byte[] rgbaTopLeft,
            int width,
            int height) {
        int pixelCount = checkedPixelCount(width, height);
        int[] argb = new int[pixelCount];
        for (int index = 0; index < pixelCount; ++index) {
            int offset = index * 4;
            int red = rgbaTopLeft[offset] & 0xff;
            int green = rgbaTopLeft[offset + 1] & 0xff;
            int blue = rgbaTopLeft[offset + 2] & 0xff;
            int alpha = rgbaTopLeft[offset + 3] & 0xff;
            argb[index] =
                    (alpha << 24) | (red << 16) | (green << 8) | blue;
        }
        return Bitmap.createBitmap(
                argb,
                width,
                height,
                Bitmap.Config.ARGB_8888);
    }

    private static int checkedPixelCount(int width, int height) {
        if (width <= 0 || height <= 0) {
            throw new IllegalArgumentException(
                    "Tracking dimensions must be positive.");
        }
        int pixelCount = Math.multiplyExact(width, height);
        if (pixelCount > MAX_PIXEL_COUNT) {
            throw new IllegalArgumentException(
                    "Tracking input exceeds the bounded pixel count.");
        }
        return pixelCount;
    }

    private static String safeMessage(Exception error) {
        if (error == null) {
            return "unknown failure";
        }
        String message = error.getMessage();
        if (message == null || message.trim().isEmpty()) {
            return error.getClass().getSimpleName();
        }
        String normalized = message.replace('\n', ' ').replace('\r', ' ').trim();
        return normalized.length() <= 240
                ? normalized
                : normalized.substring(0, 240);
    }

    private static void requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required");
        }
    }

    private static double clamp01(double value) {
        return Math.max(0.0, Math.min(1.0, value));
    }

    private static final class RequestState {
        private final String requestId;
        private final Bitmap bitmap;
        private final Callback callback;
        private List<Face> faces;
        private PersonDetection person;
        private int completedParts;
        private String errorCode;
        private String errorMessage = "unknown failure";
        private boolean cancelled;

        private RequestState(
                String requestId,
                Bitmap bitmap,
                Callback callback) {
            this.requestId = requestId;
            this.bitmap = bitmap;
            this.callback = callback;
        }
    }

    private static final class PersonDetection {
        private final int left;
        private final int top;
        private final int width;
        private final int height;
        private final int imageWidth;
        private final int imageHeight;
        private final double confidence;

        private PersonDetection(
                int left,
                int top,
                int width,
                int height,
                int imageWidth,
                int imageHeight,
                double confidence) {
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
            this.imageWidth = imageWidth;
            this.imageHeight = imageHeight;
            this.confidence = confidence;
        }
    }
}
