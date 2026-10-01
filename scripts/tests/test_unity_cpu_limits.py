#!/usr/bin/env python3
"""Contracts for thermally bounded Unity CI invocations."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WRAPPER = ROOT / "scripts" / "run_unity_limited.sh"
BUILD_ANDROID = ROOT / "scripts" / "build_unity_android.sh"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class UnityCpuLimitContracts(unittest.TestCase):
    def test_unity_wrapper_injects_worker_and_cpu_limits(self) -> None:
        source = read(WRAPPER)
        self.assertTrue(source.startswith("#!/usr/bin/env bash\n"))
        for token in (
            'UNITY_JOB_WORKERS="${UNITY_JOB_WORKERS:-2}"',
            'UNITY_CPU_QUOTA="${UNITY_CPU_QUOTA:-300%}"',
            'UNITY_CPU_AFFINITY="${UNITY_CPU_AFFINITY:-0-3}"',
            'UNITY_NICE="${UNITY_NICE:-10}"',
            "-job-worker-count",
            "systemd-run",
            "CPUQuota=${UNITY_CPU_QUOTA}",
            "CPUAffinity=${UNITY_CPU_AFFINITY}",
            "taskset -c",
            "nice -n",
        ):
            self.assertIn(token, source)

    def test_wrapper_preflights_systemd_and_falls_back_without_hiding_limits(self) -> None:
        source = read(WRAPPER)
        self.assertIn("can_use_systemd_scope()", source)
        self.assertIn("systemd-run \\\n        --user \\\n        --scope", source)
        self.assertIn("systemd-run CPU scope unavailable; falling back to taskset", source)
        self.assertIn(
            "systemd-run/taskset CPU limits unavailable; falling back to nice "
            "plus Unity job-worker-count only.",
            source,
        )
        self.assertIn("WEACHY_UNITY_CPU_LIMITS", source)
        self.assertIn("WEACHY_UNITY_SYSTEMD_SCOPE", source)

    def test_android_builds_must_go_through_unity_limit_wrapper(self) -> None:
        source = read(BUILD_ANDROID)
        self.assertIn('UNITY_LIMIT_WRAPPER="${ROOT_DIR}/scripts/run_unity_limited.sh"', source)
        self.assertIn('"${UNITY_LIMIT_WRAPPER}"', source)
        self.assertIn('"${UNITY_EDITOR}" \\', source)
        self.assertNotIn(
            'WEACHY_ANDROID_COMPILE_SDK_PACKAGE="${COMPILE_SDK_PACKAGE}" \\\n    "${UNITY_EDITOR}"',
            source,
        )


if __name__ == "__main__":
    unittest.main()
