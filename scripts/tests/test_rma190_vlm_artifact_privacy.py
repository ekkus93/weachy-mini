#!/usr/bin/env python3
"""RMA-190 physical VLM artifact privacy contract (no device required)."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts/run_rma190_rear_camera_vlm_end_to_end_android.sh"


class Rma190VlmArtifactPrivacyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = RUNNER.read_text(encoding="utf-8")

    def test_no_global_or_visual_device_capture(self) -> None:
        source = self.source
        for forbidden in (
            "screencap -p",
            "exec-out screencap",
            "dumpsys media.camera",
            "dumpsys activity activities",
            "logcat -c",
            "device-screen-final.png",
        ):
            self.assertNotIn(forbidden, source)
        self.assertNotRegex(source, r"logcat\s+-d\s+-v\s+threadtime\s*>")
        self.assertNotIn("> "${REPORT_DIR}/camera.txt"", source)
        self.assertNotIn("> "${REPORT_DIR}/activity.txt"", source)

    def test_logcat_is_app_pid_and_invocation_scoped(self) -> None:
        source = self.source
        self.assertIn("shell pidof "${PACKAGE_NAME}"", source)
        self.assertIn(""${app_pid}" =~ ^[0-9]+$", source)
        self.assertIn("-n "${LOG_START:-}"", source)
        self.assertIn("shell date", source)
        self.assertIn("logcat -d -v threadtime --pid="${app_pid}" -T "${LOG_START}"", source)
        self.assertIn("python3 -c", source)
        self.assertIn("deque(maxlen=48)", source)
        self.assertIn("deque(maxlen=24)", source)
        self.assertIn("events.append("RMA-190 stage: " + status.group(1).lower())", source)
        self.assertNotIn("events.append(line)", source)
        self.assertIn("read_report > "${REPORT_DIR}/device-report.json"", source)

    def test_sanitized_evidence_is_retained(self) -> None:
        source = self.source
        for required in (
            "fixture-metadata.json",
            "adb-reverse.txt",
            "apk.sha256",
            "report.sha256",
            "package.txt",
            "raw_request_persisted",
            "authorization_value_persisted",
            "transformed_reachy_eye_frame",
        ):
            self.assertIn(required, source)
        self.assertRegex(
            source,
            re.compile(r"dumpsys package "\$\{PACKAGE_NAME\}".*?grep -E", re.S),
        )


if __name__ == "__main__":
    unittest.main()
