#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
from pathlib import Path

ROOT = Path.cwd().resolve()
MODULE_PATH = ROOT / "scripts/rma111_retry_applicator.py"
SPEC = importlib.util.spec_from_file_location("rma111_retry_applicator", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise SystemExit("Could not load the RMA-111 retry applicator")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
ORIGINAL = MODULE.correct_payload_preconditions


def replace_exact(
    source: str,
    old: str,
    new: str,
    expected_count: int,
    label: str,
) -> str:
    count = source.count(old)
    if count != expected_count:
        raise SystemExit(f"Unexpected RMA-111 {label} count: {count}")
    return source.replace(old, new)


def corrected_payload_preconditions() -> None:
    ORIGINAL()

    acceptance_path = (
        ROOT / "scripts/run_rma111_lightweight_tracking_acceptance_android.sh"
    )
    acceptance = acceptance_path.read_text(encoding="utf-8")
    acceptance = replace_exact(
        acceptance,
        "            || true\n",
        "",
        1,
        "ADB suppression",
    )
    acceptance_path.write_text(acceptance, encoding="utf-8")

    tracker_path = (
        ROOT
        / "Assets/ReachyMini/Runtime/Core/Perception/ReachyLightweightTracking.cs"
    )
    tracker = tracker_path.read_text(encoding="utf-8")
    malformed_descriptor = """            Descriptor = new ProviderDescriptor(
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                backend.BackendVersion,
                VisionProviderKind.LightweightTracker,
                VisionProviderLocation.OnDevice);
"""
    corrected_descriptor = """            Descriptor = new ProviderDescriptor(
                VisionProviderKind.LightweightTracker,
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                "On-device lightweight tracker",
                backend.BackendVersion,
                VisionProviderLocation.OnDevice);
"""
    tracker = replace_exact(
        tracker,
        malformed_descriptor,
        corrected_descriptor,
        1,
        "ProviderDescriptor constructor",
    )
    tracker_path.write_text(tracker, encoding="utf-8")

    tests_path = (
        ROOT
        / "managed/ReachyMini.Camera.Tests/Rma111LightweightTrackingContracts.cs"
    )
    tests = tests_path.read_text(encoding="utf-8")
    tests = replace_exact(
        tests,
        """            Throws<ArgumentException>(
                () => new ReachyTrackingFramePixels(
                    identity,
                    2,
                    2,
                    new byte[15],
                    new byte[4]),
                "short RGBA buffer");
            Throws<ArgumentException>(
                () => new ReachyTrackingFramePixels(
                    identity,
                    2,
                    2,
                    new byte[16],
                    new byte[3]),
                "short validity buffer");
""",
        """            Throws<ArgumentException>(
                () =>
                {
                    var pixels = new ReachyTrackingFramePixels(
                        identity,
                        2,
                        2,
                        new byte[15],
                        new byte[4]);
                    GC.KeepAlive(pixels);
                },
                "short RGBA buffer");
            Throws<ArgumentException>(
                () =>
                {
                    var pixels = new ReachyTrackingFramePixels(
                        identity,
                        2,
                        2,
                        new byte[16],
                        new byte[3]);
                    GC.KeepAlive(pixels);
                },
                "short validity buffer");
""",
        1,
        "constructor-failure ownership",
    )
    tests = replace_exact(
        tests,
        "            TestResources resources = TestResources.Create(\n",
        "            await using TestResources resources = TestResources.Create(\n",
        4,
        "test resource ownership",
    )
    tests = replace_exact(
        tests,
        "            TestResources resources1 = TestResources.Create(\n",
        "            await using TestResources resources1 = TestResources.Create(\n",
        1,
        "first concurrent resource ownership",
    )
    tests = replace_exact(
        tests,
        "            TestResources resources2 = TestResources.Create(\n",
        "            await using TestResources resources2 = TestResources.Create(\n",
        1,
        "second concurrent resource ownership",
    )
    tests = replace_exact(
        tests,
        "            IVisualTracker tracker,\n",
        "            ReachyOnDeviceLightweightTracker tracker,\n",
        1,
        "concrete tracker helper parameter",
    )
    tests = replace_exact(
        tests,
        """                if (IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(TestResources));
                }
""",
        """                ObjectDisposedException.ThrowIf(IsDisposed, this);
""",
        1,
        "test resource disposed check",
    )
    tests = replace_exact(
        tests,
        "            public int VlmInvocationCount => 0;\n",
        "            public int VlmInvocationCount { get; }\n",
        1,
        "VLM invocation property",
    )
    tests = replace_exact(
        tests,
        """                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(FakeBackend));
                }
""",
        """                ObjectDisposedException.ThrowIf(disposed, this);
""",
        1,
        "backend disposed check",
    )
    tests_path.write_text(tests, encoding="utf-8")

    camera_bridge_path = (
        ROOT
        / "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyCameraFrameBridge.java"
    )
    camera_bridge = camera_bridge_path.read_text(encoding="utf-8")
    camera_bridge = replace_exact(
        camera_bridge,
        "import androidx.annotation.NonNull;\n",
        "import androidx.annotation.NonNull;\n"
        "import androidx.annotation.OptIn;\n",
        1,
        "AndroidX OptIn import",
    )
    camera_bridge = replace_exact(
        camera_bridge,
        "import androidx.camera.camera2.interop.Camera2CameraInfo;\n",
        "import androidx.camera.camera2.interop.Camera2CameraInfo;\n"
        "import androidx.camera.camera2.interop.ExperimentalCamera2Interop;\n",
        1,
        "CameraX experimental import",
    )
    camera_bridge = replace_exact(
        camera_bridge,
        "    private static CameraSelector exactCameraSelector(final String selectedId) {\n",
        "    @OptIn(markerClass = ExperimentalCamera2Interop.class)\n"
        "    private static CameraSelector exactCameraSelector(final String selectedId) {\n",
        1,
        "private CameraX interop opt-in",
    )
    camera_bridge_path.write_text(camera_bridge, encoding="utf-8")


MODULE.correct_payload_preconditions = corrected_payload_preconditions
Path(__file__).unlink()
MODULE.main()
