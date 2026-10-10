#!/usr/bin/env python3
"""Contracts for RMA-002 independent-build evidence validation."""

from __future__ import annotations

import copy
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/rma002_independent_build_evidence.py"

spec = importlib.util.spec_from_file_location("rma002_evidence", SCRIPT)
assert spec is not None and spec.loader is not None
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


def valid_record(machine: str, provisioning: str) -> dict:
    lock_path = ROOT / "toolchain.lock.json"
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    libraries = {name: "1" * 64 for name in evidence.REQUIRED}
    return {
        "schema": evidence.SCHEMA,
        "machine": machine,
        "provisioning": provisioning,
        "source_sha": "a" * 40,
        "clean_checkout": True,
        "installed_toolchain_verified": True,
        "toolchain_sha256": evidence.digest(lock_path),
        "unity_version": lock["unity"]["editor_version"],
        "apk_sha256": "b" * 64,
        "badging": {
            "package": "com.ekkus.weachymini",
            "min_sdk": lock["android"]["unity_device_feasibility_min_sdk"],
            "target_sdk": lock["android"]["target_sdk"],
        },
        "native_libraries": libraries,
    }


class IndependentBuildEvidenceTests(unittest.TestCase):
    def test_compatible_records_remain_non_release_evidence(self) -> None:
        result = evidence.compare(
            valid_record("machine-a", "install-a"),
            valid_record("machine-b", "install-b"),
        )
        self.assertEqual(
            "structurally_compatible_not_release_accepted",
            result["status"],
        )
        self.assertTrue(result["operator_must_verify_independent_provisioning"])

    def test_same_machine_or_provisioning_is_rejected(self) -> None:
        first = valid_record("machine-a", "install-a")
        with self.assertRaises(ValueError):
            evidence.compare(first, valid_record("machine-a", "install-b"))
        with self.assertRaises(ValueError):
            evidence.compare(first, valid_record("machine-b", "install-a"))

    def test_record_identity_fields_are_revalidated(self) -> None:
        for field, value in (
            ("machine", "../host"),
            ("source_sha", "not-a-sha"),
            ("toolchain_sha256", "0" * 64),
            ("apk_sha256", "ABC"),
        ):
            record = valid_record("machine-a", "install-a")
            record[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                evidence.validate_record(record)

    def test_package_and_sdk_pins_are_revalidated(self) -> None:
        for field, value in (
            ("package", "example.fixture"),
            ("min_sdk", -1),
            ("target_sdk", -1),
        ):
            record = valid_record("machine-a", "install-a")
            record["badging"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                evidence.validate_record(record)

    def test_native_library_paths_and_hashes_are_revalidated(self) -> None:
        record = valid_record("machine-a", "install-a")
        record["native_libraries"]["lib/x86_64/libfixture.so"] = "2" * 64
        with self.assertRaises(ValueError):
            evidence.validate_record(record)

        record = valid_record("machine-a", "install-a")
        required = next(iter(evidence.REQUIRED))
        record["native_libraries"][required] = "not-a-digest"
        with self.assertRaises(ValueError):
            evidence.validate_record(record)

    def test_different_native_library_inventory_is_rejected(self) -> None:
        first = valid_record("machine-a", "install-a")
        second = copy.deepcopy(valid_record("machine-b", "install-b"))
        second["native_libraries"]["lib/arm64-v8a/libextra.so"] = "3" * 64
        with self.assertRaises(ValueError):
            evidence.compare(first, second)


if __name__ == "__main__":
    unittest.main()
