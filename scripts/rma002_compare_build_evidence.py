#!/usr/bin/env python3
"""Compare two observed clean Android build records without declaring acceptance."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any

SHA256 = re.compile(r"[0-9a-f]{64}\Z")
COMMIT = re.compile(r"[0-9a-f]{40}\Z")
COMMAND = "./scripts/build_unity_android.sh device-feasibility"
ROOT = Path(__file__).resolve().parents[1]
LOCKFILE = ROOT / "toolchain.lock.json"
VERSIONS = (
    "unity_editor",
    "jdk",
    "android_sdk",
    "android_ndk",
    "android_cmake",
    "gradle",
    "android_gradle_plugin",
)


class EvidenceError(ValueError):
    """An input record lacks required, consistent evidence."""


def required_text(obj: dict[str, Any], key: str, label: str) -> str:
    value = obj.get(key)
    if not isinstance(value, str) or not value.strip():
        raise EvidenceError(f"{label}.{key} must be nonempty text")
    if any(ord(char) < 32 for char in value):
        raise EvidenceError(f"{label}.{key} contains a control character")
    return value


def required_hash(obj: dict[str, Any], key: str, label: str, pattern: re.Pattern[str]) -> str:
    value = required_text(obj, key, label)
    if pattern.fullmatch(value) is None:
        raise EvidenceError(f"{label}.{key} must be lowercase hexadecimal")
    return value


def required_object(obj: dict[str, Any], key: str, label: str) -> dict[str, Any]:
    value = obj.get(key)
    if not isinstance(value, dict):
        raise EvidenceError(f"{label}.{key} must be a JSON object")
    return value


def validate_repository_pins(record: dict[str, Any]) -> None:
    """Fail closed when two agreeing build records contradict the checked-out lock."""
    try:
        lock_bytes = LOCKFILE.read_bytes()
        manifest = json.loads(lock_bytes)
        android = manifest["android"]
        expected_versions = {
            "unity_editor": manifest["unity"]["editor_version"],
            "jdk": str(android["jdk_major"]),
            "android_sdk": android["compile_sdk_package"],
            "android_ndk": android["ndk"],
            "android_cmake": android["cmake"],
            "gradle": android["gradle"],
            "android_gradle_plugin": android["android_gradle_plugin"],
        }
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise EvidenceError(f"cannot validate checked-out toolchain lock: {exc}") from exc

    actual_lock_hash = hashlib.sha256(lock_bytes).hexdigest()
    if record["toolchain_lock_sha256"] != actual_lock_hash:
        raise EvidenceError("toolchain_lock_sha256 does not match checked-out lock")
    for key, expected in expected_versions.items():
        if record["toolchain_versions"][key] != expected:
            raise EvidenceError(f"toolchain_versions.{key} disagrees with pinned lock")
    for key, expected in (
        ("min_sdk", android["unity_device_feasibility_min_sdk"]),
        ("target_sdk", android["target_sdk"]),
        ("abi", android["abi"]),
    ):
        if record["build"][key] != expected:
            raise EvidenceError(f"build.{key} disagrees with pinned lock")


def read_record(path: Path) -> dict[str, Any]:
    try:
        record = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise EvidenceError(f"cannot read {path}: {exc}") from exc
    return validate_record(record, str(path))


def validate_record(record: Any, label: str = "record") -> dict[str, Any]:
    """Validate every declared field even for direct in-memory API callers."""
    if not isinstance(record, dict) or type(record.get("schema_version")) is not int:
        raise EvidenceError(f"{label}: schema_version must be integer 1")
    if record["schema_version"] != 1:
        raise EvidenceError(f"{label}: unsupported schema version")
    required_hash(record, "source_sha", label, COMMIT)
    required_hash(record, "toolchain_lock_sha256", label, SHA256)
    environment = required_object(record, "environment", label)
    for key in (
        "environment_id",
        "provisioning_id",
        "provisioning_evidence",
        "os",
        "architecture",
    ):
        required_text(environment, key, "environment")
    for key in ("clean_checkout", "fresh_provisioning", "toolchain_verified"):
        if environment.get(key) is not True:
            raise EvidenceError(f"environment.{key} must be explicitly true")
    versions = required_object(record, "toolchain_versions", label)
    for key in VERSIONS:
        required_text(versions, key, "toolchain_versions")
    build = required_object(record, "build", label)
    if required_text(build, "command", "build") != COMMAND:
        raise EvidenceError("build.command differs from pinned device-feasibility build")
    if type(build.get("exit_code")) is not int or build["exit_code"] != 0:
        raise EvidenceError("build.exit_code must be integer zero")
    if required_text(build, "application_id", "build") != "com.ekkus.weachymini":
        raise EvidenceError("build.application_id is incorrect")
    if required_text(build, "abi", "build") != "arm64-v8a":
        raise EvidenceError("build.abi must be arm64-v8a")
    if type(build.get("min_sdk")) is not int or build["min_sdk"] != 26:
        raise EvidenceError("build.min_sdk must be integer 26")
    if type(build.get("target_sdk")) is not int or build["target_sdk"] != 37:
        raise EvidenceError("build.target_sdk must be integer 37")
    required_hash(build, "apk_sha256", "build", SHA256)
    required_text(build, "build_log_evidence", "build")
    validate_repository_pins(record)
    return record


def compare(a: dict[str, Any], b: dict[str, Any], expected_sha: str) -> dict[str, Any]:
    validate_record(a, "record_a")
    validate_record(b, "record_b")
    if not isinstance(expected_sha, str) or COMMIT.fullmatch(expected_sha) is None:
        raise EvidenceError("expected SHA must be exactly 40 lowercase hex characters")
    if a["source_sha"] != expected_sha or b["source_sha"] != expected_sha:
        raise EvidenceError("source SHA differs from expected exact revision")
    for key in ("toolchain_lock_sha256", "toolchain_versions"):
        if a[key] != b[key]:
            raise EvidenceError(f"the records disagree on {key}")
    for key in ("environment_id", "provisioning_id", "provisioning_evidence"):
        if a["environment"][key] == b["environment"][key]:
            raise EvidenceError(f"the records reuse environment.{key}")
    for key in ("command", "application_id", "abi", "min_sdk", "target_sdk"):
        if a["build"][key] != b["build"][key]:
            raise EvidenceError(f"the records disagree on build.{key}")
    return {
        "source_sha": expected_sha,
        "comparable_records": True,
        "apk_hashes_equal": a["build"]["apk_sha256"] == b["build"]["apk_sha256"],
        "evidence_status": "requires_independent_human_review",
        "todo_gate_closed": False,
        "note": "JSON declarations cannot attest independent clean environments, actual "
        "build execution, or artifact integrity; review the original evidence.",
    }


def verify_apk_file(path: Path, expected_sha256: str) -> None:
    digest = hashlib.sha256()
    size = 0
    try:
        with path.open("rb") as file:
            for chunk in iter(lambda: file.read(1024 * 1024), b""):
                size += len(chunk)
                digest.update(chunk)
    except OSError as exc:
        raise EvidenceError(f"cannot read APK {path}: {exc}") from exc
    if not size or digest.hexdigest() != expected_sha256:
        raise EvidenceError(f"APK {path} is empty or its SHA-256 disagrees with record")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record_a", type=Path)
    parser.add_argument("record_b", type=Path)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--apk-a", type=Path)
    parser.add_argument("--apk-b", type=Path)
    args = parser.parse_args(argv)
    try:
        if (args.apk_a is None) != (args.apk_b is None):
            raise EvidenceError("both APK paths are required")
        first, second = read_record(args.record_a), read_record(args.record_b)
        result = compare(first, second, args.expected_sha)
        result["apk_files_verified"] = False
        if args.apk_a is not None:
            if args.apk_a.resolve() == args.apk_b.resolve():
                raise EvidenceError("APK paths must be independent")
            verify_apk_file(args.apk_a, first["build"]["apk_sha256"])
            verify_apk_file(args.apk_b, second["build"]["apk_sha256"])
            result["apk_files_verified"] = True
    except EvidenceError as exc:
        print(f"RMA-002 evidence comparison rejected: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
