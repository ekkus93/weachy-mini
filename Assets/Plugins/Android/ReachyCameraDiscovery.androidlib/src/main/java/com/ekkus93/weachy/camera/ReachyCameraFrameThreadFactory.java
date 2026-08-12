package com.ekkus93.weachy.camera;

import java.util.concurrent.ThreadFactory;
import java.util.concurrent.atomic.AtomicInteger;

final class ReachyCameraFrameThreadFactory implements ThreadFactory {
    private final String baseName;
    private final AtomicInteger count = new AtomicInteger();

    ReachyCameraFrameThreadFactory(String baseName) {
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
