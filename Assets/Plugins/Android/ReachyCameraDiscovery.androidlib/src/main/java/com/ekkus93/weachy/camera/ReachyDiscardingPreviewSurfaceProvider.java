package com.ekkus93.weachy.camera;

import android.graphics.ImageFormat;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Size;

import androidx.annotation.NonNull;
import androidx.camera.core.Preview;
import androidx.camera.core.SurfaceRequest;

import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicBoolean;

final class ReachyDiscardingPreviewSurfaceProvider implements
        Preview.SurfaceProvider,
        AutoCloseable {
    private static final Executor DIRECT_EXECUTOR = new Executor() {
        @Override
        public void execute(Runnable command) {
            command.run();
        }
    };

    private final Object providerLock = new Object();
    private final HandlerThread handlerThread;
    private final Handler handler;
    private PreviewSurfaceLease activeLease;
    private boolean closed;

    ReachyDiscardingPreviewSurfaceProvider() {
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
}
