#!/usr/bin/env python3
"""Contracts for thermally bounded Android toolchain invocations."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ANDROID_WRAPPER = ROOT / "scripts" / "run_android_limited.sh"
UNITY_WRAPPER = ROOT / "scripts" / "run_unity_limited.sh"
BUILD_ANDROID = ROOT / "scripts" / "build_unity_android.sh"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class AndroidCpuLimitContracts(unittest.TestCase):
    def test_android_wrapper_exports_gradle_jvm_and_native_worker_caps(self) -> None:
        source = read(ANDROID_WRAPPER)
        self.assertTrue(source.startswith("#!/usr/bin/env bash\n"))
        for token in (
            'ANDROID_GRADLE_WORKERS="${ANDROID_GRADLE_WORKERS:-2}"',
            'ANDROID_NATIVE_JOBS="${ANDROID_NATIVE_JOBS:-2}"',
            'ANDROID_CPU_QUOTA="${ANDROID_CPU_QUOTA:-300%}"',
            'ANDROID_CPU_AFFINITY="${ANDROID_CPU_AFFINITY:-0-3}"',
            'ANDROID_NICE="${ANDROID_NICE:-10}"',
            'CMAKE_BUILD_PARALLEL_LEVEL="${ANDROID_NATIVE_JOBS}"',
            "org.gradle.workers.max=${ANDROID_GRADLE_WORKERS}",
            "-XX:ActiveProcessorCount=${ANDROID_GRADLE_WORKERS}",
            'MAKEFLAGS="-j${ANDROID_NATIVE_JOBS}',
        ):
            self.assertIn(token, source)

    def test_android_wrapper_applies_cgroup_affinity_and_fallbacks(self) -> None:
        source = read(ANDROID_WRAPPER)
        for token in (
            "systemd-run",
            "CPUQuota=${ANDROID_CPU_QUOTA}",
            "CPUAffinity=${ANDROID_CPU_AFFINITY}",
            "taskset -c",
            "nice -n",
            "WEACHY_ANDROID_CPU_LIMITS",
            "WEACHY_ANDROID_SYSTEMD_SCOPE",
        ):
            self.assertIn(token, source)
        self.assertIn("systemd-run Android CPU scope unavailable", source)
        self.assertIn("Gradle/CMake/JVM worker caps", source)

    def test_unity_android_build_wraps_sdkmanager_and_unity_android_build_tree(self) -> None:
        source = read(BUILD_ANDROID)
        self.assertIn(
            'ANDROID_LIMIT_WRAPPER="${ROOT_DIR}/scripts/run_android_limited.sh"',
            source,
        )
        self.assertIn("Android CPU-limit wrapper is missing or not executable", source)

        continuation = "\\"
        android_wrapper = '"${ANDROID_LIMIT_WRAPPER}" ' + continuation
        self.assertGreaterEqual(source.count(android_wrapper), 2)
        self.assertIn('JAVA_HOME="${UNITY_JAVA_HOME}"', source)
        self.assertIn(
            'WEACHY_ANDROID_COMPILE_SDK_PACKAGE="${COMPILE_SDK_PACKAGE}"',
            source,
        )
        self.assertIn('"${UNITY_LIMIT_WRAPPER}" ' + continuation, source)
        self.assertIn('"${UNITY_EDITOR}" ' + continuation, source)

        unity_build_tree = (
            android_wrapper
            + "\n    env "
            + continuation
            + '\n    WEACHY_ANDROID_COMPILE_SDK_PACKAGE="${COMPILE_SDK_PACKAGE}" '
            + continuation
            + '\n    "${UNITY_LIMIT_WRAPPER}" '
            + continuation
            + '\n    "${UNITY_EDITOR}" '
            + continuation
        )
        self.assertIn(unity_build_tree, source)

    def test_unity_and_android_wrappers_are_separate_layers(self) -> None:
        android_source = read(ANDROID_WRAPPER)
        unity_source = read(UNITY_WRAPPER)
        self.assertIn("ANDROID_GRADLE_WORKERS", android_source)
        self.assertIn("UNITY_JOB_WORKERS", unity_source)
        self.assertNotIn("UNITY_JOB_WORKERS", android_source)
        self.assertNotIn("ANDROID_GRADLE_WORKERS", unity_source)

    def test_no_unbounded_parallel_cmake_builds_in_shell_scripts(self) -> None:
        offenders: list[str] = []
        pattern = re.compile(r"cmake\s+--build[^\n]*--parallel(?:\s*(?:\\\n\s*)?$)")
        for script in sorted((ROOT / "scripts").glob("*.sh")):
            text = read(script)
            if script.name == "run_android_limited.sh":
                continue
            if pattern.search(text) and "ANDROID_NATIVE_JOBS" not in text:
                offenders.append(str(script.relative_to(ROOT)))
        self.assertEqual([], offenders)


if __name__ == "__main__":
    unittest.main()
