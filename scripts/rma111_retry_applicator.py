#!/usr/bin/env python3
from __future__ import annotations

import base64
import hashlib
import io
import json
import os
import shutil
import subprocess
import tarfile
import zlib
from pathlib import Path

ROOT = Path.cwd().resolve()
PAYLOAD_SHA = "5e6f318c823e5116313b81d3611a9c1d3d316bde0d0ae5c2585c52ddfe3ba055"
ARCHIVE_SHA = "666ae5da4e7ef6aa17b873ee5b50506f6d1f9441569a1abe979ccc88a9932d51"
CHUNK_NAMES = [
    ".rma111_payload_00",
    ".rma111_payload_01",
    ".rma111_payload_10",
    ".rma111_payload_11",
]


def run(*args: str, timeout: int | None = None) -> None:
    subprocess.run(args, cwd=ROOT, check=True, timeout=timeout)


def verify_and_extract() -> None:
    chunks = sorted((ROOT / "scripts").glob(".rma111_payload_*"))
    if [path.name for path in chunks] != CHUNK_NAMES:
        raise SystemExit(
            "Unexpected RMA-111 payload chunks: "
            + ", ".join(path.name for path in chunks)
        )
    payload = "".join(path.read_text(encoding="utf-8") for path in chunks)
    actual_payload_sha = hashlib.sha256(payload.encode("utf-8")).hexdigest()
    if actual_payload_sha != PAYLOAD_SHA:
        raise SystemExit(f"RMA-111 payload hash mismatch: {actual_payload_sha}")
    archive_bytes = zlib.decompress(base64.b85decode(payload.encode("ascii")))
    actual_archive_sha = hashlib.sha256(archive_bytes).hexdigest()
    if actual_archive_sha != ARCHIVE_SHA:
        raise SystemExit(f"RMA-111 archive hash mismatch: {actual_archive_sha}")
    with tarfile.open(fileobj=io.BytesIO(archive_bytes), mode="r:") as archive:
        members = archive.getmembers()
        if len(members) != 16:
            raise SystemExit(
                f"Unexpected RMA-111 archive entry count: {len(members)}"
            )
        for member in members:
            target = (ROOT / member.name).resolve()
            try:
                target.relative_to(ROOT)
            except ValueError as exc:
                raise SystemExit(
                    f"Unsafe RMA-111 archive path: {member.name}"
                ) from exc
            if not member.isfile():
                raise SystemExit(
                    f"Non-file RMA-111 archive entry: {member.name}"
                )
        archive.extractall(ROOT)
    for chunk in chunks:
        chunk.unlink()
    print(
        "RMA-111 payload verified: "
        f"payload={actual_payload_sha} archive={actual_archive_sha}"
    )


def correct_payload_preconditions() -> None:
    path = ROOT / "scripts/rma111_apply.py"
    source = path.read_text(encoding="utf-8")

    old_dimension = "if (width, height) != (250, 313):"
    new_dimension = "if (width, height) != (250, 312):"
    dimension_count = source.count(old_dimension)
    if dimension_count != 1:
        raise SystemExit(
            "Unexpected RMA-111 fixture-dimension precondition count: "
            f"{dimension_count}"
        )
    source = source.replace(old_dimension, new_dimension)

    old_dependencies = '''        """    implementation 'androidx.camera:camera-lifecycle:1.6.1'
    implementation 'androidx.annotation:annotation:1.9.1'
""",
        """    implementation 'androidx.camera:camera-lifecycle:1.6.1'
    implementation 'com.google.mlkit:face-detection:16.1.7'
    implementation 'com.google.mlkit:segmentation-selfie:16.0.0-beta6'
    implementation 'androidx.annotation:annotation:1.9.1'
""",
'''
    new_dependencies = '''        """    implementation "androidx.camera:camera-lifecycle:${cameraxVersion}"
""",
        """    implementation "androidx.camera:camera-lifecycle:${cameraxVersion}"
    implementation 'com.google.mlkit:face-detection:16.1.7'
    implementation 'com.google.mlkit:segmentation-selfie:16.0.0-beta6'
""",
'''
    dependency_count = source.count(old_dependencies)
    if dependency_count != 1:
        raise SystemExit(
            "Unexpected RMA-111 dependency-patch precondition count: "
            f"{dependency_count}"
        )
    source = source.replace(old_dependencies, new_dependencies)
    path.write_text(source, encoding="utf-8")


def apply_repository_edits() -> None:
    run("python3", "scripts/rma111_apply.py", timeout=60)
    if (ROOT / "scripts/rma111_apply.py").exists():
        raise SystemExit("RMA-111 source patcher did not remove itself")
    acceptance = (
        ROOT / "scripts/run_rma111_lightweight_tracking_acceptance_android.sh"
    )
    if not acceptance.is_file() or not os.access(acceptance, os.X_OK):
        raise SystemExit("RMA-111 physical acceptance script is not executable")


def run_managed_contracts() -> None:
    project = "managed/ReachyMini.Camera.Tests/ReachyMini.Camera.Tests.csproj"
    run("dotnet", "restore", project, timeout=180)
    run(
        "dotnet",
        "run",
        "--project",
        project,
        "--configuration",
        "Release",
        "--no-restore",
        timeout=180,
    )


def install_android_sdk() -> None:
    android = json.loads(
        (ROOT / "toolchain.lock.json").read_text(encoding="utf-8")
    )["android"]
    android_home = Path(os.environ["ANDROID_HOME"])
    sdkmanager = android_home / "cmdline-tools/latest/bin/sdkmanager"
    if not sdkmanager.is_file():
        resolved = shutil.which("sdkmanager")
        if resolved is None:
            raise SystemExit("sdkmanager is unavailable")
        sdkmanager = Path(resolved)
    run(
        str(sdkmanager),
        "--channel=3",
        "--install",
        "platform-tools",
        f"platforms;android-{android['compile_sdk_package']}",
        f"build-tools;{android['build_tools']}",
        timeout=600,
    )


def compile_android_library() -> None:
    runner_temp = Path(os.environ["RUNNER_TEMP"])
    harness = runner_temp / "rma111-android-harness"
    if harness.exists():
        shutil.rmtree(harness)
    shutil.copytree(
        ROOT / "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib",
        harness / "library",
    )
    (harness / "settings.gradle").write_text(
        """pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}
rootProject.name = 'rma111-android-harness'
include ':library'
""",
        encoding="utf-8",
    )
    (harness / "build.gradle").write_text(
        """plugins {
    id 'com.android.library' version '9.3.1' apply false
}
""",
        encoding="utf-8",
    )
    (harness / "local.properties").write_text(
        f"sdk.dir={os.environ['ANDROID_HOME']}\n",
        encoding="utf-8",
    )
    subprocess.run(
        [
            "gradle",
            "--no-daemon",
            "-p",
            str(harness),
            ":library:compileDebugJavaWithJavac",
            ":library:lintDebug",
        ],
        check=True,
        timeout=900,
    )


def verify_source_contracts() -> None:
    required = {
        "core tracker": (
            ROOT
            / "Assets/ReachyMini/Runtime/Core/Perception/ReachyLightweightTracking.cs"
        ),
        "Unity staging": (
            ROOT
            / "Assets/ReachyMini/Runtime/Application/ReachyUnityTrackingFrameResources.cs"
        ),
        "Android backend": (
            ROOT
            / "Assets/ReachyMini/Runtime/Application/ReachyAndroidMlKitTrackingBackend.cs"
        ),
        "Java bridge": (
            ROOT
            / "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
            "com/ekkus93/weachy/camera/ReachyMlKitTrackingBridge.java"
        ),
        "managed contracts": (
            ROOT
            / "managed/ReachyMini.Camera.Tests/Rma111LightweightTrackingContracts.cs"
        ),
        "Unity contracts": (
            ROOT
            / "Assets/ReachyMini/Tests/Editor/ReachyLightweightTrackingTests.cs"
        ),
        "fixture": (
            ROOT
            / "Assets/ReachyMini/Runtime/Application/ReachyRma111Fixture.generated.cs"
        ),
    }
    for label, path in required.items():
        if not path.is_file():
            raise SystemExit(f"Missing RMA-111 {label}: {path}")
    combined = "\n".join(
        path.read_text(encoding="utf-8") for path in required.values()
    )
    build_gradle = (
        ROOT / "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/build.gradle"
    ).read_text(encoding="utf-8")
    for token in (
        "ReachyOnDeviceLightweightTracker",
        "ReachyStableTrackStore",
        "AsyncGPUReadback",
        "IsDetectionCenterValid",
        "face-000001",
        "invalid_center_suppressed",
        "vlm_invocation_count",
        "com.google.mlkit:face-detection:16.1.7",
        "com.google.mlkit:segmentation-selfie:16.0.0-beta6",
    ):
        if token not in combined and token not in build_gradle:
            raise SystemExit(f"Missing RMA-111 token: {token}")
    for token in (
        "play-services-mlkit",
        "ReadPixels(",
        "fallbackProvider",
        "|| true",
    ):
        if token in combined:
            raise SystemExit(f"Forbidden RMA-111 pattern: {token}")
    run("git", "diff", "--check")
    changed = subprocess.check_output(
        ["git", "diff", "--name-only"], cwd=ROOT, text=True
    ).splitlines()
    if any(path.startswith(".github/workflows/") for path in changed):
        raise SystemExit("Retry applicator changed a workflow file")
    tracked = subprocess.check_output(
        ["git", "ls-files"], cwd=ROOT, text=True
    ).splitlines()
    if any(
        path.startswith("managed/") and ("/bin/" in path or "/obj/" in path)
        for path in tracked
    ):
        raise SystemExit("Generated managed build output is tracked")


def commit() -> None:
    Path(__file__).unlink()
    run("git", "config", "user.name", "Phillip Chin")
    run("git", "config", "user.email", "ekkus93@gmail.com")
    run("git", "add", "-A")
    staged = subprocess.check_output(
        ["git", "diff", "--cached", "--name-only"], cwd=ROOT, text=True
    ).splitlines()
    workflow_changes = [
        path for path in staged if path.startswith(".github/workflows/")
    ]
    if workflow_changes:
        raise SystemExit(
            "Refusing workflow changes from GITHUB_TOKEN: "
            + ", ".join(workflow_changes)
        )
    run("git", "commit", "-m", "RMA-111: implement on-device lightweight tracking")
    run("git", "push", "origin", "HEAD:master", timeout=120)


def main() -> None:
    verify_and_extract()
    correct_payload_preconditions()
    apply_repository_edits()
    run_managed_contracts()
    install_android_sdk()
    compile_android_library()
    verify_source_contracts()
    commit()


if __name__ == "__main__":
    main()
