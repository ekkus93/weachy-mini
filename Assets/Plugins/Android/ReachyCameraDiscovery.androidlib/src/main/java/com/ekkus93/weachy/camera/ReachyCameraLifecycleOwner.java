package com.ekkus93.weachy.camera;

import androidx.annotation.NonNull;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;

final class ReachyCameraLifecycleOwner implements LifecycleOwner {
    private final LifecycleRegistry registry = new LifecycleRegistry(this);
    private boolean created;
    private boolean started;
    private boolean destroyed;

    ReachyCameraLifecycleOwner() {
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
