package com.ekkus.weachy.bridge;

/** First-party Android bridge entry point. */
public final class ReachyAndroidBridge {
    private static final int API_VERSION = 1;

    private ReachyAndroidBridge() {
        throw new AssertionError("No instances");
    }

    /** Returns the Java bridge API version. */
    public static int apiVersion() {
        return API_VERSION;
    }
}
