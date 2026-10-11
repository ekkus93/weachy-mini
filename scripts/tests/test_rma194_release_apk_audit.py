#!/usr/bin/env python3
"""Deterministic, non-device RMA-194 shipping APK archive audit regressions."""

from __future__ import annotations

import importlib.util
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "rma194_apk_audit", ROOT / "scripts/verify_rma194_release_apk.py"
)
assert SPEC is not None and SPEC.loader is not None
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)

REQUIRED_LIBRARIES = (
    "libil2cpp.so",
    "libmujoco.so",
    "libreachy_sim.so",
    "libreachy_llama.so",
)


class Rma194ApkArchiveAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.apk = Path(self.temp.name) / "fixture.apk"

    def build(self, *, omit: tuple[str, ...] = (), extra: tuple = ()) -> None:
        entries = [
            ("AndroidManifest.xml", b"fixture binary manifest"),
            ("assets/bin/Data/globalgamemanagers", b"fixture Unity payload"),
        ]
        for name in REQUIRED_LIBRARIES:
            entries.append((f"lib/arm64-v8a/{name}", b"fixture ELF"))
        with zipfile.ZipFile(self.apk, "w") as archive:
            for name, content in (*entries, *extra):
                if name not in omit:
                    archive.writestr(name, content)

    def test_minimal_nondevice_fixture_is_structurally_accepted(self) -> None:
        self.build()
        result = AUDIT.inspect_apk(self.apk)
        self.assertEqual("passed", result["status"])
        self.assertEqual(4, result["arm64_native_library_count"])

    def test_missing_il2cpp_is_rejected(self) -> None:
        self.build(omit=("lib/arm64-v8a/libil2cpp.so",))
        with self.assertRaisesRegex(ValueError, "libil2cpp.so"):
            AUDIT.inspect_apk(self.apk)

    def test_empty_unity_directory_is_not_a_data_payload(self) -> None:
        self.build(
            omit=("assets/bin/Data/globalgamemanagers",),
            extra=(("assets/bin/Data/", b""),),
        )
        with self.assertRaisesRegex(ValueError, "Unity data payload"):
            AUDIT.inspect_apk(self.apk)

    def test_case_changed_manifest_is_rejected_cleanly(self) -> None:
        self.build(
            omit=("AndroidManifest.xml",),
            extra=(("androidmanifest.xml", b"fixture"),),
        )
        with self.assertRaisesRegex(ValueError, "AndroidManifest.xml is missing"):
            AUDIT.inspect_apk(self.apk)

    def test_empty_manifest_is_rejected(self) -> None:
        self.build(
            omit=("AndroidManifest.xml",),
            extra=(("AndroidManifest.xml", b""),),
        )
        with self.assertRaisesRegex(ValueError, "nonempty file"):
            AUDIT.inspect_apk(self.apk)

    def test_empty_required_native_library_is_rejected(self) -> None:
        self.build(
            omit=("lib/arm64-v8a/libmujoco.so",),
            extra=(("lib/arm64-v8a/libmujoco.so", b""),),
        )
        with self.assertRaisesRegex(ValueError, "empty required ARM64"):
            AUDIT.inspect_apk(self.apk)

    def test_deferred_ar_payload_is_rejected(self) -> None:
        self.build(extra=(("lib/arm64-v8a/libarcore_sdk_c.so", b"fixture"),))
        with self.assertRaisesRegex(ValueError, "Deferred AR/XR"):
            AUDIT.inspect_apk(self.apk)

    def test_noncanonical_path_is_rejected(self) -> None:
        self.build(extra=(("assets/bin//Data/hidden", b"fixture"),))
        with self.assertRaisesRegex(ValueError, "noncanonical"):
            AUDIT.inspect_apk(self.apk)

    def test_symlink_entry_is_rejected(self) -> None:
        link = zipfile.ZipInfo("assets/bin/Data/redirect")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        self.build(extra=((link, b"somewhere/else"),))
        with self.assertRaisesRegex(ValueError, "nonregular"):
            AUDIT.inspect_apk(self.apk)


if __name__ == "__main__":
    unittest.main()
