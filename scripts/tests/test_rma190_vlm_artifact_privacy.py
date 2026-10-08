#!/usr/bin/env python3
"""RMA-190 physical VLM artifact privacy contract (no device required)."""

from __future__ import annotations

import re
import subprocess
import sys
import tempfile
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
        self.assertNotIn('> "${REPORT_DIR}/camera.txt"', source)
        self.assertNotIn('> "${REPORT_DIR}/activity.txt"', source)

    def test_logcat_is_app_pid_and_invocation_scoped(self) -> None:
        source = self.source
        self.assertIn('shell pidof "${PACKAGE_NAME}"', source)
        self.assertIn('"${app_pid}" =~ ^[0-9]+$', source)
        self.assertIn('-n "${LOG_START:-}"', source)
        self.assertIn("shell date", source)
        self.assertIn('logcat -d -v threadtime --pid="${app_pid}" -T "${LOG_START}"', source)
        self.assertIn("python3 -c", source)
        self.assertIn("deque(maxlen=48)", source)
        self.assertIn("deque(maxlen=24)", source)
        self.assertIn('events.append("RMA-190 stage: " + status.group(1).lower())', source)
        self.assertNotIn("events.append(line)", source)
        self.assertIn('read_report > "${REPORT_DIR}/device-report.json"', source)

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
            re.compile(r'dumpsys package "\$\{PACKAGE_NAME\}".*?grep -E', re.S),
        )


    def test_diagnostic_filter_emits_bounded_lines_without_private_payloads(self) -> None:
        """Execute the shipping inline filter, not a separately copied algorithm."""
        source = self.source.split("python3 -c '\n", 1)[1].split(
            "\n' \"\${REPORT_DIR}/resource-transitions.txt\"", 1
        )[0]
        raw_logs = (
            "10-08 16:01:01.000 123 456 I Unity: RMA-190 stage started prompt=SECRET_PROMPT\n"
            "10-08 16:01:02.000 123 456 I Unity: RMA-181 sampled resource transition: "
            "mode=degraded, reason=low_memory\n"
            "10-08 16:01:03.000 123 456 I Unity: RMA-190 stage completed url=SECRET_URL\n"
            "10-08 16:01:04.000 123 456 I Unity: unrelated camera image SECRET_IMAGE\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            transitions = Path(directory) / "resource-transitions.txt"
            result = subprocess.run(
                [sys.executable, "-c", source, str(transitions)],
                input=raw_logs,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertEqual(
                "RMA-190 stage: started\nRMA-190 stage: completed\n",
                result.stdout,
            )
            self.assertEqual(
                "RMA-181 sampled resource transition: mode=degraded, reason=low_memory\n",
                transitions.read_text(encoding="utf-8"),
            )
            self.assertNotIn("SECRET", result.stdout + transitions.read_text())

    def test_android_library_packages_loopback_network_security_xml(self) -> None:
        plugin = ROOT / "Assets/Plugins/Android/ReachyProviderSecurity.androidlib"
        gradle = (plugin / "build.gradle").read_text(encoding="utf-8")
        self.assertIn("manifest.srcFile 'AndroidManifest.xml'", gradle)
        self.assertIn("res.srcDirs = ['res']", gradle)
        manifest = (plugin / "AndroidManifest.xml").read_text(encoding="utf-8")
        self.assertIn("@xml/reachy_provider_network_security", manifest)
        self.assertTrue(
            (plugin / "res/xml/reachy_provider_network_security.xml").is_file()
        )


if __name__ == "__main__":
    unittest.main()
