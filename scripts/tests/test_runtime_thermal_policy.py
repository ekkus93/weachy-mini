#!/usr/bin/env python3
"""Contracts for Unity runtime frame pacing and thermal policy."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
POLICY = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRuntimeThermalPolicy.cs"
RUNTIME = ROOT / "Assets/ReachyMini/Runtime"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class RuntimeThermalPolicyContracts(unittest.TestCase):
    def test_runtime_policy_applies_before_scene_load_with_safe_defaults(self) -> None:
        source = read(POLICY)
        for token in (
            "public static class ReachyRuntimeThermalPolicy",
            "DefaultTargetFrameRate = 30",
            "MinimumTargetFrameRate = 15",
            "MaximumTargetFrameRate = 120",
            "RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)",
            "Application.targetFrameRate = boundedTarget",
            "QualitySettings.vSyncCount = 0",
            "QualitySettings.vSyncCount = 1",
        ):
            self.assertIn(token, source)

    def test_policy_is_configurable_but_fail_closed_by_default(self) -> None:
        source = read(POLICY)
        for token in (
            "WEACHY_TARGET_FRAME_RATE",
            "WEACHY_DISABLE_RUNTIME_THERMAL_POLICY",
            "Environment.GetEnvironmentVariable(",
            "ResolveTargetFrameRate()",
            "BoundTargetFrameRate(",
            "targetFrameRate < MinimumTargetFrameRate",
            "targetFrameRate > MaximumTargetFrameRate",
        ):
            self.assertIn(token, source)
        self.assertIn("return DefaultTargetFrameRate;", source)

    def test_mobile_uses_target_frame_rate_not_vsync_for_pacing(self) -> None:
        source = read(POLICY)
        self.assertIn("#if UNITY_ANDROID || UNITY_IOS", source)
        self.assertIn("Mobile Unity players ignore vSyncCount", source)
        self.assertIn("Application.targetFrameRate", source)

    def test_no_other_runtime_code_sets_unbounded_frame_pacing(self) -> None:
        offenders: list[str] = []
        for path in sorted(RUNTIME.rglob("*.cs")):
            if path == POLICY:
                continue
            text = read(path)
            if "Application.targetFrameRate" in text or "QualitySettings.vSyncCount" in text:
                offenders.append(str(path.relative_to(ROOT)))
        self.assertEqual([], offenders)


if __name__ == "__main__":
    unittest.main()
