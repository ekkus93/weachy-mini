package com.ekkus93.weachy.camera;

import android.graphics.ImageFormat;
import android.graphics.Rect;

import androidx.camera.core.ImageProxy;

import org.json.JSONException;
import org.json.JSONObject;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Copies CameraX YUV planes into a bounded ring of direct buffers.
 *
 * <p>The analyzer owns the ImageProxy and closes it after publish returns. Unity
 * can only lease detached direct buffers, so no closed CameraX buffer is ever
 * sampled. A slot is never overwritten while leased.</p>
 */
public final class ReachyCameraTextureFrameBridge {
    private static final int SLOT_COUNT = 3;
    private static final int FREE = 0;
    private static final int WRITING = 1;
    private static final int READY = 2;
    private static final int LEASED = 3;

    private static final Object LOCK = new Object();
    private static final Slot[] SLOTS = new Slot[SLOT_COUNT];

    private static long activeGeneration;
    private static long activeSessionId;
    private static long nextToken = 1L;
    private static long publishedFrameCount;
    private static long droppedFrameCount;
    private static long acquiredFrameCount;
    private static long releasedFrameCount;

    static {
        for (int index = 0; index < SLOT_COUNT; ++index) {
            SLOTS[index] = new Slot(index);
        }
    }

    private ReachyCameraTextureFrameBridge() {
    }

    static void beginSession(long generation, long sessionId) {
        if (generation <= 0L || sessionId <= 0L) {
            throw new IllegalArgumentException(
                    "Texture bridge sessions require positive generation and session identifiers.");
        }
        synchronized (LOCK) {
            activeGeneration = generation;
            activeSessionId = sessionId;
            invalidateUnleasedSlotsLocked();
        }
    }

    static void endSession(long generation) {
        synchronized (LOCK) {
            activeGeneration = generation;
            activeSessionId = 0L;
            invalidateUnleasedSlotsLocked();
        }
    }

    static Publication publish(
            ImageProxy imageProxy,
            long generation,
            long sessionId,
            long sequence,
            long timestampNanoseconds,
            String cameraId,
            String facing,
            int sensorOrientationDegrees,
            int rotationDegrees,
            Rect crop) {
        if (imageProxy == null) {
            throw new IllegalArgumentException("A CameraX image is required.");
        }
        if (imageProxy.getFormat() != ImageFormat.YUV_420_888) {
            throw new IllegalStateException(
                    "Texture bridge requires YUV_420_888, received " +
                    imageProxy.getFormat() + ".");
        }
        if (generation <= 0L || sessionId <= 0L || sequence <= 0L ||
                timestampNanoseconds <= 0L) {
            throw new IllegalArgumentException(
                    "Texture bridge frame identities and timestamps must be positive.");
        }
        if (cameraId == null || cameraId.trim().isEmpty()) {
            throw new IllegalArgumentException(
                    "Texture bridge frames require a camera identifier.");
        }
        if (facing == null || facing.trim().isEmpty()) {
            throw new IllegalArgumentException(
                    "Texture bridge frames require lens-facing metadata.");
        }
        requireRightAngle(sensorOrientationDegrees, "sensor orientation");
        requireRightAngle(rotationDegrees, "output rotation");

        int width = imageProxy.getWidth();
        int height = imageProxy.getHeight();
        if (width <= 0 || height <= 0) {
            throw new IllegalStateException(
                    "CameraX returned invalid texture dimensions " +
                    width + "x" + height + ".");
        }
        Rect frameCrop = crop == null ? new Rect(0, 0, width, height) : new Rect(crop);
        if (frameCrop.left < 0 || frameCrop.top < 0 ||
                frameCrop.right > width || frameCrop.bottom > height ||
                frameCrop.width() <= 0 || frameCrop.height() <= 0) {
            throw new IllegalStateException(
                    "CameraX returned an invalid texture crop " + frameCrop + ".");
        }

        final Slot slot;
        final long token;
        synchronized (LOCK) {
            if (generation != activeGeneration || sessionId != activeSessionId) {
                return Publication.stale();
            }
            slot = reserveWritableSlotLocked();
            if (slot == null) {
                ++droppedFrameCount;
                return Publication.dropped();
            }
            token = nextToken++;
            slot.state = WRITING;
            slot.generation = generation;
            slot.token = token;
        }

        boolean copied = false;
        try {
            ImageProxy.PlaneProxy[] planes = imageProxy.getPlanes();
            if (planes == null || planes.length != 3) {
                throw new IllegalStateException(
                        "YUV_420_888 must expose exactly three planes.");
            }
            int chromaWidth = (width + 1) / 2;
            int chromaHeight = (height + 1) / 2;
            slot.ensureCapacity(width, height, chromaWidth, chromaHeight);
            copyPlane(planes[0], width, height, slot.yBuffer, "Y");
            copyPlane(planes[1], chromaWidth, chromaHeight, slot.uBuffer, "U");
            copyPlane(planes[2], chromaWidth, chromaHeight, slot.vBuffer, "V");
            copied = true;

            String colorStandard = chooseColorStandard(width, height);
            String colorRange = "limited";
            boolean mirrored = "front".equals(facing);
            synchronized (LOCK) {
                if (slot.state != WRITING || slot.token != token ||
                        generation != activeGeneration ||
                        sessionId != activeSessionId) {
                    slot.resetIfOwned(token);
                    return Publication.staleAfterCopy();
                }

                freeSupersededReadySlotsLocked(slot.index);
                slot.sessionId = sessionId;
                slot.sequence = sequence;
                slot.timestampNanoseconds = timestampNanoseconds;
                slot.cameraId = cameraId;
                slot.facing = facing;
                slot.sensorOrientationDegrees = sensorOrientationDegrees;
                slot.rotationDegrees = rotationDegrees;
                slot.width = width;
                slot.height = height;
                slot.chromaWidth = chromaWidth;
                slot.chromaHeight = chromaHeight;
                slot.crop.set(frameCrop);
                slot.mirrored = mirrored;
                slot.colorStandard = colorStandard;
                slot.colorRange = colorRange;
                slot.state = READY;
                ++publishedFrameCount;
                return Publication.published(colorStandard, colorRange, mirrored);
            }
        } finally {
            if (!copied) {
                synchronized (LOCK) {
                    slot.resetIfOwned(token);
                }
            }
        }
    }

    static FrameLease acquireLatest(
            long generation,
            long sessionId,
            long afterSequence) {
        synchronized (LOCK) {
            if (generation != activeGeneration ||
                    sessionId <= 0L || sessionId != activeSessionId) {
                return null;
            }
            Slot selected = null;
            for (Slot candidate : SLOTS) {
                if (candidate.state != READY ||
                        candidate.generation != generation ||
                        candidate.sessionId != sessionId ||
                        candidate.sequence <= afterSequence) {
                    continue;
                }
                if (selected == null || candidate.sequence > selected.sequence) {
                    selected = candidate;
                }
            }
            if (selected == null) {
                return null;
            }
            selected.state = LEASED;
            ++acquiredFrameCount;
            return new FrameLease(selected);
        }
    }

    static JSONObject snapshotJson() throws JSONException {
        synchronized (LOCK) {
            JSONObject value = new JSONObject();
            value.put("mode", "direct_yuv_plane_ring");
            value.put("slotCount", SLOT_COUNT);
            value.put("activeGeneration", activeGeneration);
            value.put("activeSessionId", activeSessionId);
            value.put("publishedFrameCount", publishedFrameCount);
            value.put("droppedFrameCount", droppedFrameCount);
            value.put("acquiredFrameCount", acquiredFrameCount);
            value.put("releasedFrameCount", releasedFrameCount);
            int ready = 0;
            int leased = 0;
            int writing = 0;
            long latestSequence = 0L;
            long latestTimestamp = 0L;
            for (Slot slot : SLOTS) {
                if (slot.state == READY) {
                    ++ready;
                    if (slot.sequence > latestSequence) {
                        latestSequence = slot.sequence;
                        latestTimestamp = slot.timestampNanoseconds;
                    }
                } else if (slot.state == LEASED) {
                    ++leased;
                } else if (slot.state == WRITING) {
                    ++writing;
                }
            }
            value.put("readySlotCount", ready);
            value.put("leasedSlotCount", leased);
            value.put("writingSlotCount", writing);
            value.put("latestReadySequence", latestSequence);
            value.put("latestReadyTimestampNanoseconds", latestTimestamp);
            value.put("imageProxyRetained", false);
            value.put("planeBufferRetained", false);
            return value;
        }
    }

    private static Slot reserveWritableSlotLocked() {
        Slot readyCandidate = null;
        for (Slot slot : SLOTS) {
            if (slot.state == FREE) {
                return slot;
            }
            if (slot.state == READY &&
                    (readyCandidate == null || slot.sequence < readyCandidate.sequence)) {
                readyCandidate = slot;
            }
        }
        return readyCandidate;
    }

    private static void freeSupersededReadySlotsLocked(int retainedIndex) {
        for (Slot slot : SLOTS) {
            if (slot.index != retainedIndex && slot.state == READY) {
                slot.reset();
            }
        }
    }

    private static void invalidateUnleasedSlotsLocked() {
        for (Slot slot : SLOTS) {
            if (slot.state == READY || slot.state == FREE) {
                slot.reset();
            }
        }
    }

    private static void release(int slotIndex, long token) {
        synchronized (LOCK) {
            if (slotIndex < 0 || slotIndex >= SLOT_COUNT) {
                return;
            }
            Slot slot = SLOTS[slotIndex];
            if (slot.state == LEASED && slot.token == token) {
                slot.reset();
                ++releasedFrameCount;
            }
        }
    }

    private static void copyPlane(
            ImageProxy.PlaneProxy plane,
            int width,
            int height,
            ByteBuffer destination,
            String name) {
        if (plane == null) {
            throw new IllegalStateException(name + " plane is missing.");
        }
        int rowStride = plane.getRowStride();
        int pixelStride = plane.getPixelStride();
        if (rowStride <= 0 || pixelStride <= 0) {
            throw new IllegalStateException(
                    name + " plane exposes invalid strides row=" + rowStride +
                    " pixel=" + pixelStride + ".");
        }
        ByteBuffer source = plane.getBuffer().duplicate();
        int base = source.position();
        int limit = source.limit();
        int required = Math.multiplyExact(width, height);
        if (destination.capacity() < required) {
            throw new IllegalStateException(
                    name + " destination capacity is smaller than the packed plane.");
        }
        destination.clear();
        for (int row = 0; row < height; ++row) {
            int rowOffset = Math.addExact(base, Math.multiplyExact(row, rowStride));
            for (int column = 0; column < width; ++column) {
                int sourceIndex = Math.addExact(
                        rowOffset,
                        Math.multiplyExact(column, pixelStride));
                if (sourceIndex < base || sourceIndex >= limit) {
                    throw new IllegalStateException(
                            name + " plane is shorter than its stride metadata at row " +
                            row + ", column " + column + ".");
                }
                destination.put(source.get(sourceIndex));
            }
        }
        destination.flip();
    }

    private static String chooseColorStandard(int width, int height) {
        return width >= 1280 || height >= 720 ? "bt709" : "bt601";
    }

    private static void requireRightAngle(int value, String label) {
        if (value != 0 && value != 90 && value != 180 && value != 270) {
            throw new IllegalArgumentException(
                    "Invalid " + label + " " + value + ".");
        }
    }

    static final class Publication {
        final boolean imagePlanesAccessed;
        final boolean cpuPixelCopyPerformed;
        final boolean textureFramePublished;
        final boolean stale;
        final String colorStandard;
        final String colorRange;
        final boolean mirrored;
        final String detail;

        private Publication(
                boolean imagePlanesAccessed,
                boolean cpuPixelCopyPerformed,
                boolean textureFramePublished,
                boolean stale,
                String colorStandard,
                String colorRange,
                boolean mirrored,
                String detail) {
            this.imagePlanesAccessed = imagePlanesAccessed;
            this.cpuPixelCopyPerformed = cpuPixelCopyPerformed;
            this.textureFramePublished = textureFramePublished;
            this.stale = stale;
            this.colorStandard = colorStandard;
            this.colorRange = colorRange;
            this.mirrored = mirrored;
            this.detail = detail;
        }

        static Publication published(
                String colorStandard,
                String colorRange,
                boolean mirrored) {
            return new Publication(
                    true,
                    true,
                    true,
                    false,
                    colorStandard,
                    colorRange,
                    mirrored,
                    "YUV planes copied once into a detached direct-buffer slot.");
        }

        static Publication dropped() {
            return new Publication(
                    false,
                    false,
                    false,
                    false,
                    "unknown",
                    "unknown",
                    false,
                    "No free texture slot was available; the texture frame was dropped without blocking CameraX.");
        }

        static Publication stale() {
            return new Publication(
                    false,
                    false,
                    false,
                    true,
                    "unknown",
                    "unknown",
                    false,
                    "The frame belonged to a stale texture session and was not copied.");
        }

        static Publication staleAfterCopy() {
            return new Publication(
                    true,
                    true,
                    false,
                    true,
                    "unknown",
                    "unknown",
                    false,
                    "The texture session changed while copying; the detached slot was invalidated.");
        }
    }

    public static final class FrameLease implements AutoCloseable {
        private final int slotIndex;
        private final long token;
        private final long sessionId;
        private final long sequence;
        private final long timestampNanoseconds;
        private final String cameraId;
        private final String facing;
        private final int sensorOrientationDegrees;
        private final int rotationDegrees;
        private final int width;
        private final int height;
        private final int chromaWidth;
        private final int chromaHeight;
        private final Rect crop;
        private final boolean mirrored;
        private final String colorStandard;
        private final String colorRange;
        private final ByteBuffer yBuffer;
        private final ByteBuffer uBuffer;
        private final ByteBuffer vBuffer;
        private final AtomicBoolean closed = new AtomicBoolean();

        private FrameLease(Slot slot) {
            slotIndex = slot.index;
            token = slot.token;
            sessionId = slot.sessionId;
            sequence = slot.sequence;
            timestampNanoseconds = slot.timestampNanoseconds;
            cameraId = slot.cameraId;
            facing = slot.facing;
            sensorOrientationDegrees = slot.sensorOrientationDegrees;
            rotationDegrees = slot.rotationDegrees;
            width = slot.width;
            height = slot.height;
            chromaWidth = slot.chromaWidth;
            chromaHeight = slot.chromaHeight;
            crop = new Rect(slot.crop);
            mirrored = slot.mirrored;
            colorStandard = slot.colorStandard;
            colorRange = slot.colorRange;
            yBuffer = duplicateForLease(slot.yBuffer, width * height);
            uBuffer = duplicateForLease(slot.uBuffer, chromaWidth * chromaHeight);
            vBuffer = duplicateForLease(slot.vBuffer, chromaWidth * chromaHeight);
        }

        public long getSessionId() {
            return sessionId;
        }

        public long getSequence() {
            return sequence;
        }

        public long getTimestampNanoseconds() {
            return timestampNanoseconds;
        }

        public String getCameraId() {
            return cameraId;
        }

        public String getFacing() {
            return facing;
        }

        public int getSensorOrientationDegrees() {
            return sensorOrientationDegrees;
        }

        public int getRotationDegrees() {
            return rotationDegrees;
        }

        public int getWidth() {
            return width;
        }

        public int getHeight() {
            return height;
        }

        public int getChromaWidth() {
            return chromaWidth;
        }

        public int getChromaHeight() {
            return chromaHeight;
        }

        public int getCropLeft() {
            return crop.left;
        }

        public int getCropTop() {
            return crop.top;
        }

        public int getCropRight() {
            return crop.right;
        }

        public int getCropBottom() {
            return crop.bottom;
        }

        public boolean isMirrored() {
            return mirrored;
        }

        public String getColorStandard() {
            return colorStandard;
        }

        public String getColorRange() {
            return colorRange;
        }

        public int getYLength() {
            return width * height;
        }

        public int getULength() {
            return chromaWidth * chromaHeight;
        }

        public int getVLength() {
            return chromaWidth * chromaHeight;
        }

        public ByteBuffer getYBuffer() {
            ensureOpen();
            return yBuffer;
        }

        public ByteBuffer getUBuffer() {
            ensureOpen();
            return uBuffer;
        }

        public ByteBuffer getVBuffer() {
            ensureOpen();
            return vBuffer;
        }

        @Override
        public void close() {
            if (closed.compareAndSet(false, true)) {
                release(slotIndex, token);
            }
        }

        private void ensureOpen() {
            if (closed.get()) {
                throw new IllegalStateException(
                        "The camera texture frame lease is already closed.");
            }
        }

        private static ByteBuffer duplicateForLease(
                ByteBuffer source,
                int length) {
            ByteBuffer duplicate = source.duplicate().order(ByteOrder.nativeOrder());
            duplicate.position(0);
            duplicate.limit(length);
            return duplicate.slice().order(ByteOrder.nativeOrder());
        }
    }

    private static final class Slot {
        final int index;
        int state = FREE;
        long generation;
        long token;
        long sessionId;
        long sequence;
        long timestampNanoseconds;
        String cameraId = "";
        String facing = "unknown";
        int sensorOrientationDegrees;
        int rotationDegrees;
        int width;
        int height;
        int chromaWidth;
        int chromaHeight;
        final Rect crop = new Rect();
        boolean mirrored;
        String colorStandard = "unknown";
        String colorRange = "unknown";
        ByteBuffer yBuffer = ByteBuffer.allocateDirect(1).order(ByteOrder.nativeOrder());
        ByteBuffer uBuffer = ByteBuffer.allocateDirect(1).order(ByteOrder.nativeOrder());
        ByteBuffer vBuffer = ByteBuffer.allocateDirect(1).order(ByteOrder.nativeOrder());

        Slot(int index) {
            this.index = index;
        }

        void ensureCapacity(
                int nextWidth,
                int nextHeight,
                int nextChromaWidth,
                int nextChromaHeight) {
            int yLength = Math.multiplyExact(nextWidth, nextHeight);
            int chromaLength = Math.multiplyExact(nextChromaWidth, nextChromaHeight);
            if (yBuffer.capacity() != yLength) {
                yBuffer = ByteBuffer.allocateDirect(yLength)
                        .order(ByteOrder.nativeOrder());
            }
            if (uBuffer.capacity() != chromaLength) {
                uBuffer = ByteBuffer.allocateDirect(chromaLength)
                        .order(ByteOrder.nativeOrder());
            }
            if (vBuffer.capacity() != chromaLength) {
                vBuffer = ByteBuffer.allocateDirect(chromaLength)
                        .order(ByteOrder.nativeOrder());
            }
        }

        void resetIfOwned(long expectedToken) {
            if (token == expectedToken && state != LEASED) {
                reset();
            }
        }

        void reset() {
            state = FREE;
            generation = 0L;
            token = 0L;
            sessionId = 0L;
            sequence = 0L;
            timestampNanoseconds = 0L;
            cameraId = "";
            facing = "unknown";
            sensorOrientationDegrees = 0;
            rotationDegrees = 0;
            width = 0;
            height = 0;
            chromaWidth = 0;
            chromaHeight = 0;
            crop.setEmpty();
            mirrored = false;
            colorStandard = "unknown";
            colorRange = "unknown";
        }
    }
}
