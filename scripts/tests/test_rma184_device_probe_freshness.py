"""Exercise RMA-184 probe freshness against a simulated adb device."""

from __future__ import annotations

import json
import os
import pathlib
import subprocess
import tempfile
import textwrap
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROBE = ROOT / "scripts" / "run_rma184_device_probe_android.sh"

FAKE_ADB = textwrap.dedent(
    """\
    #!/usr/bin/env python3
    import json
    import os
    from pathlib import Path
    import shutil
    import sys

    args = sys.argv[1:]
    if args[:1] == ["-s"]:
        args = args[2:]
    remote = Path(os.environ["MOCK_REMOTE"])
    mode = os.environ["MOCK_MODE"]
    if args == ["get-state"]:
        print("device")
    elif args[:3] == ["shell", "pm", "grant"]:
        pass
    elif args[:3] == ["shell", "am", "force-stop"]:
        pass
    elif args[:3] == ["shell", "rm", "-f"]:
        if mode == "remove_error":
            sys.exit(1)
        if mode != "leave_stale":
            remote.unlink(missing_ok=True)
    elif args[:4] == ["shell", "test", "!", "-f"]:
        sys.exit(0 if not remote.is_file() else 1)
    elif args[:3] == ["shell", "am", "start"]:
        if mode in ("fresh", "limited", "forged_support", "missing_soc", "wrong_type", "bad_camera_counts"):
            report = {
                "schema_version": 1, "status": "passed", "error": "",
                "manufacturer": "samsung", "model": "SM-A546E",
                "soc": "Exynos", "processor": "ARMv8",
                "logical_processor_count": 8, "operating_system": "Android",
                "android_api_level": 36, "system_memory_mib": 5426,
                "graphics_api": "Vulkan", "graphics_device": "Mali-G68",
                "camera_permission": "Granted", "camera_count": 4,
                "available_camera_count": 4, "rear_camera_available": True,
                "front_camera_available": True,
                "on_device_asr": "Available", "offline_tts": "Available",
                "support_status": "Supported",
                "support_diagnostic": "Core runtime and full offline speech interaction are supported.",
            }
            if mode in ("limited", "forged_support"):
                report["on_device_asr"] = "Faulted"
                if mode == "limited":
                    report["support_status"] = "SupportedWithLimitations"
            if mode == "missing_soc":
                del report["soc"]
            if mode == "wrong_type":
                report["system_memory_mib"] = "5426"
            if mode == "bad_camera_counts":
                report["available_camera_count"] = 5
            remote.write_text(json.dumps(report), encoding="utf-8")
    elif args[:3] == ["shell", "test", "-f"]:
        sys.exit(0 if remote.is_file() else 1)
    elif args[:1] == ["pull"]:
        shutil.copyfile(remote, args[-1])
    else:
        print(f"unexpected adb call: {args}", file=sys.stderr)
        sys.exit(2)
"""
)


class Rma184ProbeFreshnessTests(unittest.TestCase):
    def run_probe(self, mode: str) -> tuple[subprocess.CompletedProcess[str], bool]:
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            adb = root / "adb"
            adb.write_text(FAKE_ADB, encoding="utf-8")
            adb.chmod(0o755)
            remote = root / "device-report.json"
            remote.write_text('{"schema_version": 1, "status": "passed"}', encoding="utf-8")
            output = root / "report.json"
            output.write_text("old local evidence", encoding="utf-8")
            env = {
                **os.environ,
                "ADB": str(adb),
                "MOCK_REMOTE": str(remote),
                "MOCK_MODE": mode,
                "RMA184_OUTPUT": str(output),
            }
            result = subprocess.run(
                ["bash", str(PROBE)],
                env=env,
                text=True,
                capture_output=True,
                check=False,
                timeout=15,
            )
            if result.returncode == 0:
                self.assertEqual(json.loads(output.read_text())["model"], "SM-A546E")
            return result, output.exists()

    def test_fresh_report_replaces_stale_report(self) -> None:
        result, exists = self.run_probe("fresh")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(exists)
        self.assertIn("probe passed", result.stdout)

    def test_accepts_faulted_asr_only_with_limited_support(self) -> None:
        result, exists = self.run_probe("limited")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(exists)
        self.assertIn("SupportedWithLimitations", result.stdout)

    def test_rejects_forged_full_support(self) -> None:
        result, _ = self.run_probe("forged_support")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("support_status contradicts", result.stderr)

    def test_rejects_missing_identity(self) -> None:
        result, _ = self.run_probe("missing_soc")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("soc must be", result.stderr)

    def test_rejects_non_numeric_memory(self) -> None:
        result, _ = self.run_probe("wrong_type")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("system_memory_mib must be", result.stderr)

    def test_rejects_impossible_camera_counts(self) -> None:
        result, _ = self.run_probe("bad_camera_counts")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("available cameras exceed", result.stderr)

    def test_refuses_failed_stale_report_removal(self) -> None:
        result, exists = self.run_probe("remove_error")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(exists)
        self.assertIn("refusing stale evidence", result.stderr)

    def test_refuses_stale_report_that_survives_removal(self) -> None:
        result, exists = self.run_probe("leave_stale")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(exists)
        self.assertIn("still present", result.stderr)


if __name__ == "__main__":
    unittest.main()
