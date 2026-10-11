#!/usr/bin/env python3
"""Regression contracts for RMA-002 paired-build evidence."""

from __future__ import annotations

import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from scripts.rma002_compare_build_evidence import EvidenceError, compare, read_record

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/rma002_compare_build_evidence.py"
SHA = "a" * 40
LOCK_HASH = hashlib.sha256((ROOT / "toolchain.lock.json").read_bytes()).hexdigest()


def record(label: str) -> dict:
    return {
        "schema_version": 1,
        "source_sha": SHA,
        "toolchain_lock_sha256": LOCK_HASH,
        "environment": {
            "environment_id": f"environment-{label}",
            "provisioning_id": f"provision-{label}",
            "provisioning_evidence": f"provisioning-{label}.log",
            "os": "Ubuntu 24.04",
            "architecture": "x86_64",
            "clean_checkout": True,
            "fresh_provisioning": True,
            "toolchain_verified": True,
        },
        "toolchain_versions": {
            "unity_editor": "6000.5.2f1",
            "jdk": "17",
            "android_sdk": "37.0",
            "android_ndk": "28.2.13676358",
            "android_cmake": "3.31.6",
            "gradle": "9.5.0",
            "android_gradle_plugin": "9.3.1",
        },
        "build": {
            "command": "./scripts/build_unity_android.sh device-feasibility",
            "exit_code": 0,
            "application_id": "com.ekkus.weachymini",
            "abi": "arm64-v8a",
            "min_sdk": 26,
            "target_sdk": 37,
            "apk_sha256": ("c" if label == "one" else "d") * 64,
            "build_log_evidence": f"build-{label}.log",
        },
    }


class BuildComparisonTests(unittest.TestCase):
    def setUp(self) -> None:
        self.a = record("one")
        self.b = record("two")

    def test_comparable_records_remain_unqualified(self) -> None:
        result = compare(self.a, self.b, SHA)
        self.assertTrue(result["comparable_records"])
        self.assertFalse(result["apk_hashes_equal"])
        self.assertFalse(result["todo_gate_closed"])

    def test_equal_apk_hashes_are_not_physical_evidence(self) -> None:
        self.b["build"]["apk_sha256"] = self.a["build"]["apk_sha256"]
        result = compare(self.a, self.b, SHA)
        self.assertTrue(result["apk_hashes_equal"])
        self.assertEqual(result["evidence_status"], "requires_independent_human_review")
        self.assertFalse(result["todo_gate_closed"])

    def test_reused_environment_identity_rejected(self) -> None:
        for key in ("environment_id", "provisioning_id", "provisioning_evidence"):
            with self.subTest(key=key):
                other = copy.deepcopy(self.b)
                other["environment"][key] = self.a["environment"][key]
                with self.assertRaises(EvidenceError):
                    compare(self.a, other, SHA)

    def test_mismatched_source_toolchain_or_package_rejected(self) -> None:
        for section, key, value in (
            (None, "source_sha", "f" * 40),
            (None, "toolchain_lock_sha256", "f" * 64),
            ("toolchain_versions", "unity_editor", "6000.5.1f1"),
            ("build", "target_sdk", 36),
        ):
            with self.subTest(key=key):
                other = copy.deepcopy(self.b)
                (other if section is None else other[section])[key] = value
                with self.assertRaises(EvidenceError):
                    compare(self.a, other, SHA)

    def test_false_claims_and_invalid_build_data_rejected(self) -> None:
        mutations = (
            ("environment", "clean_checkout", False),
            ("environment", "fresh_provisioning", "true"),
            ("environment", "toolchain_verified", False),
            ("build", "exit_code", False),
            ("build", "min_sdk", True),
            ("build", "apk_sha256", "wrong"),
            ("build", "application_id", "com.example.other"),
            ("build", "command", "echo ok"),
        )
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "record.json"
            for section, key, value in mutations:
                with self.subTest(key=key):
                    candidate = copy.deepcopy(self.a)
                    candidate[section][key] = value
                    path.write_text(json.dumps(candidate), encoding="utf-8")
                    with self.assertRaises(EvidenceError):
                        read_record(path)

    def test_agreeing_records_cannot_override_checked_out_toolchain_pins(self) -> None:
        mutations = (
            (None, "toolchain_lock_sha256", "f" * 64),
            ("toolchain_versions", "unity_editor", "6000.5.1f1"),
            ("toolchain_versions", "jdk", "21"),
            ("toolchain_versions", "android_sdk", "36.0"),
            ("toolchain_versions", "android_ndk", "27.0.0"),
            ("toolchain_versions", "android_cmake", "3.30.0"),
            ("toolchain_versions", "gradle", "9.4.0"),
            ("toolchain_versions", "android_gradle_plugin", "9.2.0"),
        )
        for section, key, value in mutations:
            with self.subTest(key=key):
                one = copy.deepcopy(self.a)
                two = copy.deepcopy(self.b)
                (one if section is None else one[section])[key] = value
                (two if section is None else two[section])[key] = value
                with self.assertRaises(EvidenceError):
                    compare(one, two, SHA)
                with tempfile.TemporaryDirectory() as temp:
                    path = Path(temp) / "bad.json"
                    path.write_text(json.dumps(one), encoding="utf-8")
                    with self.assertRaises(EvidenceError):
                        read_record(path)

    def test_pin_check_preserves_fail_closed_release_gate(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp) / "build.json"
            file.write_text(json.dumps(self.a), encoding="utf-8")
            self.assertEqual(read_record(file)["toolchain_lock_sha256"], LOCK_HASH)
        result = compare(self.a, self.b, SHA)
        self.assertFalse(result["todo_gate_closed"])

    def test_cli_rejects_mismatch_without_success_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            first = Path(temp) / "a.json"
            second = Path(temp) / "b.json"
            first.write_text(json.dumps(self.a), encoding="utf-8")
            second.write_text(json.dumps(self.b), encoding="utf-8")
            args = [
                sys.executable, str(SCRIPT), str(first), str(second),
                "--expected-sha", SHA,
            ]
            passed = subprocess.run(args, capture_output=True, text=True, check=False)
            self.assertEqual(passed.returncode, 0, passed.stderr)
            self.assertFalse(json.loads(passed.stdout)["todo_gate_closed"])
            self.b["source_sha"] = "f" * 40
            second.write_text(json.dumps(self.b), encoding="utf-8")
            rejected = subprocess.run(args, capture_output=True, text=True, check=False)
            self.assertEqual(rejected.returncode, 1)
            self.assertFalse(rejected.stdout)


if __name__ == "__main__":
    unittest.main()
