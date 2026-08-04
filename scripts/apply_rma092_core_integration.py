#!/usr/bin/env python3
from pathlib import Path


def replace_exact(path: str, old: str, new: str, count: int = 1) -> None:
    target = Path(path)
    original = target.read_text(encoding="utf-8")
    occurrences = original.count(old)
    if occurrences != count:
        raise SystemExit(
            f"{path}: expected {count} occurrence(s), found {occurrences}: {old[:120]!r}"
        )
    updated = original.replace(old, new, count)
    if updated == original:
        raise SystemExit(f"{path}: replacement produced no change")
    target.write_text(updated, encoding="utf-8")


def patch_java_bridge() -> None:
    path = (
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyCameraFrameBridge.java"
    )
    replace_exact(
        path,
        """                sessionId = requestedSessionId;
                deliveredSequence = 0L;
""",
        """                sessionId = requestedSessionId;
                ReachyCameraTextureFrameBridge.beginSession(
                        expectedGeneration,
                        sessionId);
                deliveredSequence = 0L;
""",
    )
    replace_exact(
        path,
        """    public static String snapshot() {
        synchronized (LOCK) {
            return snapshotLocked();
        }
    }

    public static void shutdown(Activity activity) {
""",
        """    public static String snapshot() {
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
            if (!\"Running\".equals(state) ||
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
""",
    )
    replace_exact(
        path,
        """            FrameSnapshot frame = FrameSnapshot.from(
                    imageProxy,
                    frameSession,
                    frameSequence,
                    frameDescriptor);
            synchronized (LOCK) {
                if (expectedGeneration != generation ||
                        !\"Running\".equals(state) ||
                        frameSession != sessionId) {
                    return;
                }
                latestFrame = frame;
                message =
                        \"Camera frame \" + frameSequence +
                        \" acquired without accessing image planes or copying pixels to CPU memory.\";
            }
""",
        """            FrameSnapshot frame = FrameSnapshot.from(
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
                        !\"Running\".equals(state) ||
                        frameSession != sessionId) {
                    return;
                }
                latestFrame = frame;
                message = texturePublication.textureFramePublished
                        ? \"Camera frame \" + frameSequence +
                            \" copied once into a detached direct YUV texture slot.\"
                        : texturePublication.detail;
            }
""",
    )
    replace_exact(
        path,
        """        if (analyzerExecutor != null) {
            analyzerExecutor.shutdownNow();
        }
        cameraProvider = null;
""",
        """        if (analyzerExecutor != null) {
            analyzerExecutor.shutdownNow();
        }
        ReachyCameraTextureFrameBridge.endSession(generation);
        cameraProvider = null;
""",
    )
    replace_exact(
        path,
        """            root.put(\"analysisBackpressure\", \"keep_only_latest\");
            root.put(\"previewSink\", \"private_discard_surface_until_rma092\");
            root.put(\"cpuPixelCopyPerformed\", false);
            root.put(\"latestFrame\", latestFrame == null
                    ? JSONObject.NULL
                    : latestFrame.toJson());
""",
        """            root.put(\"analysisBackpressure\", \"keep_only_latest\");
            root.put(\"previewSink\", \"analysis_yuv_gpu_texture_bridge\");
            root.put(
                    \"cpuPixelCopyPerformed\",
                    latestFrame != null && latestFrame.cpuPixelCopyPerformed);
            root.put(
                    \"textureBridge\",
                    ReachyCameraTextureFrameBridge.snapshotJson());
            root.put(\"latestFrame\", latestFrame == null
                    ? JSONObject.NULL
                    : latestFrame.toJson());
""",
    )
    replace_exact(
        path,
        """        final FrameIntrinsics intrinsics;
        final Rect activeArray;

        FrameSnapshot(
""",
        """        final FrameIntrinsics intrinsics;
        final Rect activeArray;
        boolean imagePlanesAccessed;
        boolean cpuPixelCopyPerformed;
        boolean textureFramePublished;
        boolean textureFrameStale;
        boolean mirrored;
        String colorStandard = \"unknown\";
        String colorRange = \"unknown\";
        String textureDetail = \"Texture publication has not run.\";

        FrameSnapshot(
""",
    )
    replace_exact(
        path,
        """        JSONObject toJson() throws JSONException {
""",
        """        void applyTexturePublication(
                ReachyCameraTextureFrameBridge.Publication publication) {
            if (publication == null) {
                throw new IllegalArgumentException(
                        \"Texture publication diagnostics are required.\");
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
""",
    )
    replace_exact(
        path,
        """            value.put(\"imagePlanesAccessed\", false);
            value.put(\"cpuPixelCopyPerformed\", false);
            return value;
""",
        """            value.put(\"imagePlanesAccessed\", imagePlanesAccessed);
            value.put(\"cpuPixelCopyPerformed\", cpuPixelCopyPerformed);
            value.put(\"textureFramePublished\", textureFramePublished);
            value.put(\"textureFrameStale\", textureFrameStale);
            value.put(\"mirrored\", mirrored);
            value.put(\"colorStandard\", colorStandard);
            value.put(\"colorRange\", colorRange);
            value.put(\"textureDetail\", textureDetail);
            return value;
""",
    )
    replace_exact(
        path,
        """return \"{\\\"status\\\":\\\"error\\\",\\\"state\\\":\\\"Faulted\\\",\\\"errorCode\\\":\\\"json_encoding_failed\\\",\\\"message\\\":\\\"Camera acquisition failed while encoding diagnostics.\\\",\\\"sessionId\\\":0,\\\"cameraId\\\":\\\"\\\",\\\"facing\\\":\\\"unknown\\\",\\\"analysisBackpressure\\\":\\\"keep_only_latest\\\",\\\"previewSink\\\":\\\"private_discard_surface_until_rma092\\\",\\\"cpuPixelCopyPerformed\\\":false,\\\"latestFrame\\\":null}\";
""",
        """return \"{\\\"status\\\":\\\"error\\\",\\\"state\\\":\\\"Faulted\\\",\\\"errorCode\\\":\\\"json_encoding_failed\\\",\\\"message\\\":\\\"Camera acquisition failed while encoding diagnostics.\\\",\\\"sessionId\\\":0,\\\"cameraId\\\":\\\"\\\",\\\"facing\\\":\\\"unknown\\\",\\\"analysisBackpressure\\\":\\\"keep_only_latest\\\",\\\"previewSink\\\":\\\"analysis_yuv_gpu_texture_bridge\\\",\\\"cpuPixelCopyPerformed\\\":false,\\\"latestFrame\\\":null}\";
""",
    )


def patch_acquisition() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraAcquisition.cs"
    replace_exact(
        path,
        """        string Stop();

        string Snapshot();
""",
        """        string Stop();

        IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            long sessionId,
            long afterSequence);

        string Snapshot();
""",
    )
    replace_exact(
        path,
        """        public void RefreshNow()
        {
            EnsureReady();
            ApplyPlatformSnapshot(RequirePlatform().Snapshot());
        }

        private void Awake()
""",
        """        public void RefreshNow()
        {
            EnsureReady();
            ApplyPlatformSnapshot(RequirePlatform().Snapshot());
        }

        public IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            ulong afterSequence)
        {
            EnsureReady();
            ReachyCameraAcquisitionSnapshot snapshot = state.Current;
            if (snapshot.State != ReachyCameraAcquisitionState.Running ||
                snapshot.SessionId == 0UL)
            {
                return null;
            }
            return RequirePlatform().AcquireLatestTextureFrame(
                checked((long)snapshot.SessionId),
                checked((long)afterSequence));
        }

        private void Awake()
""",
    )
    replace_exact(
        path,
        """        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(\"snapshot\") ??
                throw new InvalidOperationException(
                    \"The Android CameraX bridge returned null from snapshot.\");
#else
            throw new PlatformNotSupportedException(
                \"CameraX frame acquisition requires an Android player.\");
#endif
        }

        public void Dispose()
""",
        """        public IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            long requestedSessionId,
            long afterSequence)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            AndroidJavaObject? javaLease =
                bridge.CallStatic<AndroidJavaObject>(
                    \"acquireLatestTextureFrame\",
                    requestedSessionId,
                    afterSequence);
            return javaLease == null
                ? null
                : new ReachyAndroidJavaCameraTextureFrameLease(javaLease);
#else
            throw new PlatformNotSupportedException(
                \"CameraX frame acquisition requires an Android player.\");
#endif
        }

        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(\"snapshot\") ??
                throw new InvalidOperationException(
                    \"The Android CameraX bridge returned null from snapshot.\");
#else
            throw new PlatformNotSupportedException(
                \"CameraX frame acquisition requires an Android player.\");
#endif
        }

        public void Dispose()
""",
    )


def patch_ui_thread_platform() -> None:
    path = (
        "Assets/ReachyMini/Runtime/Application/"
        "ReachyAndroidUiThreadCameraAcquisitionPlatform.cs"
    )
    replace_exact(
        path,
        """        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(\"snapshot\");
#else
            throw Unsupported();
#endif
        }

        public void Dispose()
""",
        """        public IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            long requestedSessionId,
            long afterSequence)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            AndroidJavaObject? javaLease =
                bridge.CallStatic<AndroidJavaObject>(
                    \"acquireLatestTextureFrame\",
                    requestedSessionId,
                    afterSequence);
            return javaLease == null
                ? null
                : new ReachyAndroidJavaCameraTextureFrameLease(javaLease);
#else
            throw Unsupported();
#endif
        }

        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(\"snapshot\");
#else
            throw Unsupported();
#endif
        }

        public void Dispose()
""",
    )


def patch_bootstrap() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyCameraAcquisitionBootstrap.cs"
    replace_exact(
        path,
        """        public const string AcquisitionObjectName =
            \"ReachyAndroidCameraAcquisition\";

        private const int AndroidScreenOrientationUnspecified = -1;
""",
        """        public const string AcquisitionObjectName =
            \"ReachyAndroidCameraAcquisition\";
        public const string TextureBridgeObjectName =
            \"ReachyAndroidCameraTextureBridge\";

        private const int AndroidScreenOrientationUnspecified = -1;
""",
    )
    replace_exact(
        path,
        """                ReachyAndroidCameraAcquisition acquisition =
                    GetOrCreateAcquisition(discovery);
                InstallAcceptanceEvidenceIfRequested(
                    discovery,
                    acquisition);
""",
        """                ReachyAndroidCameraAcquisition acquisition =
                    GetOrCreateAcquisition(discovery);
                _ = GetOrCreateTextureBridge(acquisition);
                InstallAcceptanceEvidenceIfRequested(
                    discovery,
                    acquisition);
""",
    )
    replace_exact(
        path,
        """        private static void InstallAcceptanceEvidenceIfRequested(
""",
        """        private static ReachyAndroidCameraTextureBridge
            GetOrCreateTextureBridge(
                ReachyAndroidCameraAcquisition acquisition)
        {
            ReachyAndroidCameraTextureBridge? existing =
                acquisition.GetComponent<ReachyAndroidCameraTextureBridge>();
            if (existing != null)
            {
                existing.Configure(acquisition);
                return existing;
            }

            ReachyAndroidCameraTextureBridge bridge =
                acquisition.gameObject.AddComponent<
                    ReachyAndroidCameraTextureBridge>();
            bridge.Configure(acquisition);
            return bridge;
        }

        private static void InstallAcceptanceEvidenceIfRequested(
""",
    )


def patch_texture_bridge_resources() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraTextureBridge.cs"
    replace_exact(
        path,
        """                        RequireOutputTexture(),
                        conversionMaterial);
""",
        """                        RequireOutputTexture(),
                        conversionMaterial!);
""",
    )
    replace_exact(
        path,
        """            acquisition = null;
            DestroyResources();
""",
        """            acquisition = null;
            DestroyAllResources();
""",
    )
    replace_exact(
        path,
        """            DestroyResources();
            if (current.State != ReachyCameraTextureBridgeState.Faulted &&
""",
        """            DestroyFrameResources();
            if (current.State != ReachyCameraTextureBridgeState.Faulted &&
""",
    )
    replace_exact(
        path,
        """        private void DestroyResources()
        {
            DestroyObject(yTexture);
            DestroyObject(uTexture);
            DestroyObject(vTexture);
            yTexture = null;
            uTexture = null;
            vTexture = null;
            DestroyOutputTexture();
            DestroyObject(conversionMaterial);
            conversionMaterial = null;
        }
""",
        """        private void DestroyFrameResources()
        {
            DestroyObject(yTexture);
            DestroyObject(uTexture);
            DestroyObject(vTexture);
            yTexture = null;
            uTexture = null;
            vTexture = null;
            DestroyOutputTexture();
        }

        private void DestroyAllResources()
        {
            DestroyFrameResources();
            DestroyObject(conversionMaterial);
            conversionMaterial = null;
        }
""",
    )


def patch_existing_tests() -> None:
    path = "Assets/ReachyMini/Tests/Editor/ReachyAndroidCameraAcquisitionTests.cs"
    replace_exact(
        path,
        """            public string Snapshot()
            {
                return NextSnapshot;
            }

            public void Dispose()
""",
        """            public IReachyCameraTextureFrameLease?
                AcquireLatestTextureFrame(
                    long requestedSessionId,
                    long afterSequence)
            {
                _ = requestedSessionId;
                _ = afterSequence;
                return null;
            }

            public string Snapshot()
            {
                return NextSnapshot;
            }

            public void Dispose()
""",
    )


def main() -> None:
    patch_java_bridge()
    patch_acquisition()
    patch_ui_thread_platform()
    patch_bootstrap()
    patch_texture_bridge_resources()
    patch_existing_tests()


if __name__ == "__main__":
    main()
