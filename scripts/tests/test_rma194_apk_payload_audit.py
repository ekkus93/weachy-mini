#!/usr/bin/env python3
"""Synthetic positive and negative fixtures for the RMA-194 APK audit."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

from scripts.tests.rma194_apk_fixture import elf64_aarch64_shared

SCRIPT = Path(__file__).resolve().parents[1] / "verify_rma194_release_apk.py"


class Rma194ApkPayloadAuditTests(unittest.TestCase):
    def run_fixture(
        self, extra: dict[str, bytes] | None = None
    ) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "candidate.apk"
            entries = {
                "AndroidManifest.xml": b"<manifest package='com.ekkus.weachymini'/>",
                "lib/arm64-v8a/libil2cpp.so": elf64_aarch64_shared(),
                "lib/arm64-v8a/libmujoco.so": elf64_aarch64_shared(),
                "lib/arm64-v8a/libreachy_sim.so": elf64_aarch64_shared(),
                "lib/arm64-v8a/libreachy_llama.so": elf64_aarch64_shared(),
                "assets/bin/Data/globalgamemanagers": b"unity-test-fixture",
            }
            if extra:
                entries.update(extra)
            with zipfile.ZipFile(apk, "w") as archive:
                for name, content in entries.items():
                    archive.writestr(name, content)
            return subprocess.run(
                [sys.executable, str(SCRIPT), str(apk)],
                check=False,
                capture_output=True,
                text=True,
            )

    def test_valid_arm64_unity_fixture(self) -> None:
        result = self.run_fixture()
        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("passed", report["status"])
        self.assertEqual(64, len(report["apk_sha256"]))

    def test_device_build_audits_packaged_apk_after_unity(self) -> None:
        build = (SCRIPT.parent / "build_unity_android.sh").read_text(encoding="utf-8")
        audit_call = 'python3 "${ROOT_DIR}/scripts/verify_rma194_release_apk.py"'
        self.assertIn('if [[ "${MODE}" == "device-feasibility" ]]; then', build)
        self.assertIn(audit_call, build)
        self.assertIn('"${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk"', build)
        clean = 'rm -f -- "${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk"'
        self.assertIn(clean, build)
        self.assertLess(build.index(clean), build.index('-executeMethod "${METHOD}"'))
        self.assertLess(build.index('-executeMethod "${METHOD}"'), build.index(audit_call))

    def test_rejects_missing_production_runtime_library(self) -> None:
        result = self.run_fixture({"lib/arm64-v8a/libreachy_sim.so": b""})
        # An empty library is not a valid shipping runtime even if its name is present.
        self.assertNotEqual(0, result.returncode)
        self.assertIn("empty ARM64 native library", result.stderr)

    def test_rejects_arcore_library(self) -> None:
        result = self.run_fixture({"lib/arm64-v8a/libarcore_sdk_c.so": b"bad"})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Deferred AR/XR payload", result.stderr)

    def test_rejects_xr_assembly(self) -> None:
        result = self.run_fixture({"assets/bin/Data/Managed/Unity.XR.ARFoundation.dll": b"x"})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Deferred AR/XR payload", result.stderr)

    def test_rejects_arcore_manifest(self) -> None:
        result = self.run_fixture({"AndroidManifest.xml": "com.google.ar.core".encode("utf-16le")})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("manifest declaration", result.stderr)

    def test_rejects_path_traversal(self) -> None:
        result = self.run_fixture({"assets/../com/google/ar/core/Foo.class": b"x"})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("noncanonical entry names", result.stderr)

    def test_rejects_missing_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "candidate.apk"
            with zipfile.ZipFile(apk, "w") as archive:
                archive.writestr("lib/arm64-v8a/libreachy_sim.so", b"native")
            result = subprocess.run(
                [sys.executable, str(SCRIPT), str(apk)],
                check=False,
                capture_output=True,
                text=True,
            )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("AndroidManifest.xml is missing", result.stderr)


if __name__ == "__main__":
    unittest.main()
