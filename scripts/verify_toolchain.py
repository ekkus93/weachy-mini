#!/usr/bin/env python3
"""Validate the pinned toolchain manifest and an optional developer installation."""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = ROOT / "toolchain.lock.json"


class ToolchainError(RuntimeError):
    """Raised when the toolchain manifest or installed tools are invalid."""


def load_manifest() -> dict[str, Any]:
    """Load and minimally validate the toolchain lock file."""
    try:
        manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ToolchainError(f"Cannot read {MANIFEST_PATH}: {exc}") from exc

    required_top_level = {"schema_version", "unity", "android", "quality_tools"}
    missing = sorted(required_top_level.difference(manifest))
    if missing:
        raise ToolchainError(f"Toolchain manifest is missing keys: {', '.join(missing)}")
    if manifest["schema_version"] != 1:
        raise ToolchainError(f"Unsupported toolchain schema: {manifest['schema_version']}")

    android = manifest["android"]
    for key in (
        "compile_sdk",
        "target_sdk",
        "android_gradle_plugin",
        "gradle",
        "jdk_major",
        "ndk",
        "cmake",
        "abi",
    ):
        if key not in android:
            raise ToolchainError(f"Toolchain manifest android section is missing {key!r}")

    return manifest


def run_version(command: list[str]) -> str:
    """Run a version command and return combined output."""
    try:
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ToolchainError(f"Cannot run {' '.join(command)}: {exc}") from exc

    output = f"{completed.stdout}\n{completed.stderr}".strip()
    if completed.returncode != 0:
        raise ToolchainError(
            f"Command {' '.join(command)} failed with exit code {completed.returncode}: {output}"
        )
    return output


def require_executable(name: str) -> str:
    """Resolve an executable or raise an actionable error."""
    executable = shutil.which(name)
    if executable is None:
        raise ToolchainError(f"Required executable {name!r} was not found on PATH.")
    return executable


def require_pattern(label: str, output: str, pattern: str, expected: str) -> None:
    """Require a version pattern and expected value."""
    match = re.search(pattern, output)
    if match is None:
        raise ToolchainError(f"Could not parse {label} version from: {output}")
    actual = match.group(1)
    if actual != expected:
        raise ToolchainError(f"{label} version mismatch: expected {expected}, found {actual}")


def verify_installed_tools(manifest: dict[str, Any]) -> None:
    """Verify locally installed tools against pinned versions."""
    android = manifest["android"]
    quality = manifest["quality_tools"]

    python_version = f"{sys.version_info.major}.{sys.version_info.minor}"
    minimum_python = quality["python_minimum"]
    if tuple(map(int, python_version.split("."))) < tuple(map(int, minimum_python.split("."))):
        raise ToolchainError(
            f"Python {minimum_python}+ is required; this interpreter is {python_version}."
        )

    cmake_output = run_version([require_executable("cmake"), "--version"])
    require_pattern("CMake", cmake_output, r"cmake version (\d+\.\d+\.\d+)", android["cmake"])

    java_output = run_version([require_executable("java"), "-version"])
    java_match = re.search(r'version "(\d+)', java_output)
    if java_match is None:
        raise ToolchainError(f"Could not parse Java version from: {java_output}")
    if int(java_match.group(1)) != android["jdk_major"]:
        raise ToolchainError(
            f"JDK version mismatch: expected {android['jdk_major']}, found {java_match.group(1)}"
        )

    unity_editor = os.environ.get("UNITY_EDITOR")
    if not unity_editor:
        raise ToolchainError("UNITY_EDITOR must point to the pinned Unity editor executable.")
    unity_path = Path(unity_editor)
    if not unity_path.is_file():
        raise ToolchainError(f"UNITY_EDITOR is not a file: {unity_path}")
    unity_output = run_version([str(unity_path), "-version", "-batchmode", "-quit"])
    expected_unity = manifest["unity"]["editor_version"]
    if expected_unity not in unity_output:
        raise ToolchainError(
            "Unity version mismatch: expected output containing "
            f"{expected_unity!r}, got {unity_output!r}"
        )

    android_home = os.environ.get("ANDROID_SDK_ROOT") or os.environ.get("ANDROID_HOME")
    if not android_home:
        raise ToolchainError("ANDROID_SDK_ROOT or ANDROID_HOME must identify the Android SDK.")
    sdk_root = Path(android_home)
    required_paths = [
        sdk_root / "platforms" / f"android-{android['compile_sdk']}",
        sdk_root / "build-tools" / android["build_tools"],
        sdk_root / "ndk" / android["ndk"],
        sdk_root / "cmake" / android["cmake"],
    ]
    missing_paths = [str(path) for path in required_paths if not path.exists()]
    if missing_paths:
        raise ToolchainError(
            "Android SDK packages are missing:\n  - " + "\n  - ".join(missing_paths)
        )


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--manifest-only",
        action="store_true",
        help="Validate only toolchain.lock.json without checking installed SDKs.",
    )
    return parser.parse_args()


def main() -> int:
    """Run validation and return a process exit code."""
    args = parse_args()
    try:
        manifest = load_manifest()
        if not args.manifest_only:
            verify_installed_tools(manifest)
    except ToolchainError as exc:
        print(f"toolchain validation failed: {exc}", file=sys.stderr)
        return 1

    mode = "manifest" if args.manifest_only else "installed toolchain"
    print(f"toolchain validation passed ({mode})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
