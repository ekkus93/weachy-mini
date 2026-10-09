#!/usr/bin/env python3
"""RMA-184: fail fast on missing physical Android device before expensive APK builds."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/local-unity-android-validation.yml"
NO_ARTIFACTS = ROOT / ".github/workflows/local-unity-android-device-no-artifacts.yml"


class Rma184DevicePreflightTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_physical_pin_runs_after_independent_unity_tests_before_apk_build(self) -> None:
        steps = self.workflow
        self.assertLess(
            steps.index("- name: Run Unity tests"),
            steps.index("- name: Pin one physical"),
        )
        self.assertLess(steps.index("- name: Pin one physical"), steps.index("- name: Build ARM64"))
        self.assertLess(
            steps.index("- name: Pin one physical"),
            steps.index("- name: Verify ARM64"),
        )

    def test_build_only_dispatch_does_not_require_connected_device(self) -> None:
        pin = self.workflow.split("- name: Pin one physical Android device", 1)[1]
        pin = pin.split("\n      - name:", 1)[0]
        self.assertIn("github.event_name == 'push' || inputs.install_physical_device", pin)
        self.assertIn("REACHY_PHYSICAL_DEVICE_SERIAL", pin)
        self.assertIn("adb devices -l", pin)
        self.assertIn("exit 1", pin)
        self.assertIn("REACHY_ANDROID_SERIAL", pin)

    def test_apk_build_retains_explicit_build_only_dispatch(self) -> None:
        build = self.workflow.split("- name: Build ARM64 API-26 device APK", 1)[1]
        build = build.split("\n      - name:", 1)[0]
        self.assertIn("inputs.build_device_apk", build)
        self.assertIn("inputs.install_physical_device", build)
        self.assertIn("scripts/build_unity_android.sh device-feasibility", build)


    def test_no_artifacts_device_is_pinned_before_apk_build(self) -> None:
        source = NO_ARTIFACTS.read_text(encoding="utf-8")
        self.assertLess(source.index("- name: Run Unity tests"), source.index("- name: Pin one"))
        self.assertLess(source.index("- name: Pin one"), source.index("- name: Build ARM64"))
        self.assertLess(source.index("- name: Pin one"), source.index("- name: Verify ARM64"))
        self.assertLess(source.index("- name: Pin one"), source.index("- name: Install and run"))

    def test_no_artifacts_device_pin_keeps_identity_and_failure_controls(self) -> None:
        source = NO_ARTIFACTS.read_text(encoding="utf-8")
        pin = source.split("- name: Pin one physical Android device", 1)[1]
        pin = pin.split("\n      - name:", 1)[0]
        for token in (
            "REACHY_PHYSICAL_DEVICE_SERIAL",
            "adb devices",
            "ro.product.cpu.abi",
            "ro.build.version.sdk",
            "arm64-v8a",
            "sdk >= 26",
            "adb devices -l",
            "exit 1",
            "REACHY_ANDROID_SERIAL",
        ):
            self.assertIn(token, pin)

if __name__ == "__main__":
    unittest.main()
