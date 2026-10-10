#!/usr/bin/env python3
"""RMA-002: collect and compare two independent Android APK build records.

A matching pair is not itself proof that two machines were independently
provisioned, and never closes the physical or release acceptance gates.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = "rma002-independent-android-build-v1"
REQUIRED = {
    "lib/arm64-v8a/libil2cpp.so",
    "lib/arm64-v8a/libmujoco.so",
    "lib/arm64-v8a/libreachy_sim.so",
}


def digest(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def checked(command: list[str]) -> str:
    result = subprocess.run(
        command, cwd=ROOT, capture_output=True, text=True, timeout=180, check=False
    )
    if result.returncode:
        raise ValueError(f"Build evidence command failed: {command[0]}")
    return result.stdout + result.stderr


def apk_libraries(apk: Path) -> dict[str, str]:
    with zipfile.ZipFile(apk) as archive:
        names = set(archive.namelist())
        if not REQUIRED.issubset(names):
            raise ValueError("APK omits required production ARM64 libraries.")
        libs = sorted(name for name in names if name.startswith("lib/") and name.endswith(".so"))
        if any(not name.startswith("lib/arm64-v8a/") for name in libs):
            raise ValueError("APK includes an unexpected non-ARM64 native library.")
        result = {}
        for name in libs:
            hasher = hashlib.sha256()
            with archive.open(name) as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    hasher.update(chunk)
            result[name] = hasher.hexdigest()
        return result


def parse_badging(text: str) -> dict[str, object]:
    patterns = {
        "package": r"(?m)^package: name='([^']+)'",
        "min_sdk": r"(?m)^sdkVersion:'(\d+)'",
        "target_sdk": r"(?m)^targetSdkVersion:'(\d+)'",
    }
    result: dict[str, object] = {}
    for name, pattern in patterns.items():
        match = re.search(pattern, text)
        if match is None:
            raise ValueError(f"APK badging omitted {name}.")
        result[name] = int(match.group(1)) if name.endswith("_sdk") else match.group(1)
    if result["package"] != "com.ekkus.weachymini":
        raise ValueError("APK package does not match the shipping application.")
    return result


def capture(apk: Path, machine: str, provisioning: str) -> dict:
    for label in (machine, provisioning):
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", label):
            raise ValueError("Use bounded non-secret machine/provisioning labels.")
    if checked(["git", "status", "--porcelain"]).strip():
        raise ValueError("The checkout is not clean.")
    if checked(["git", "branch", "--show-current"]).strip() != "master":
        raise ValueError("The checkout is not on master.")
    sha = checked(["git", "rev-parse", "HEAD"]).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Could not determine an exact source SHA.")
    checked([sys.executable, str(ROOT / "scripts/verify_toolchain.py")])
    lock_path = ROOT / "toolchain.lock.json"
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    sdk = os.environ.get("ANDROID_SDK_ROOT") or os.environ.get("ANDROID_HOME")
    if not sdk:
        raise ValueError("ANDROID_SDK_ROOT or ANDROID_HOME is required.")
    aapt2 = Path(sdk) / "build-tools" / lock["android"]["build_tools"] / "aapt2"
    if not aapt2.is_file():
        raise ValueError("Pinned aapt2 is unavailable.")
    badging = parse_badging(checked([str(aapt2), "dump", "badging", str(apk.resolve())]))
    if badging["min_sdk"] != lock["android"]["unity_device_feasibility_min_sdk"]:
        raise ValueError("APK minSdkVersion does not match the device-feasibility pin.")
    if badging["target_sdk"] != lock["android"]["target_sdk"]:
        raise ValueError("APK targetSdkVersion does not match the toolchain pin.")
    return {
        "schema": SCHEMA,
        "machine": machine,
        "provisioning": provisioning,
        "source_sha": sha,
        "clean_checkout": True,
        "installed_toolchain_verified": True,
        "toolchain_sha256": digest(lock_path),
        "unity_version": lock["unity"]["editor_version"],
        "apk_sha256": digest(apk),
        "badging": badging,
        "native_libraries": apk_libraries(apk),
    }


def _bounded_label(value: object, field: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(
        r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", value
    ):
        raise ValueError(f"{field} must be a bounded non-secret label.")
    return value


def _sha256(value: object, field: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value):
        raise ValueError(f"{field} must be a lowercase SHA-256 digest.")
    return value


def validate_record(record: object) -> dict:
    if not isinstance(record, dict):
        raise ValueError("Build evidence record must be a JSON object.")
    if record.get("schema") != SCHEMA:
        raise ValueError("Unrecognized evidence schema.")
    _bounded_label(record.get("machine"), "machine")
    _bounded_label(record.get("provisioning"), "provisioning")

    source_sha = record.get("source_sha")
    if not isinstance(source_sha, str) or not re.fullmatch(r"[0-9a-f]{40}", source_sha):
        raise ValueError("source_sha must be an exact lowercase Git SHA.")
    if record.get("clean_checkout") is not True:
        raise ValueError("A build was not captured from a clean checkout.")
    if record.get("installed_toolchain_verified") is not True:
        raise ValueError("A build lacks installed-toolchain verification.")

    lock_path = ROOT / "toolchain.lock.json"
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    if _sha256(record.get("toolchain_sha256"), "toolchain_sha256") != digest(lock_path):
        raise ValueError("Build evidence does not match the current toolchain manifest.")
    if record.get("unity_version") != lock["unity"]["editor_version"]:
        raise ValueError("Build evidence Unity version does not match the current pin.")
    _sha256(record.get("apk_sha256"), "apk_sha256")

    badging = record.get("badging")
    if not isinstance(badging, dict):
        raise ValueError("Build evidence badging must be an object.")
    if badging.get("package") != "com.ekkus.weachymini":
        raise ValueError("Build evidence package does not match the shipping application.")
    if badging.get("min_sdk") != lock["android"]["unity_device_feasibility_min_sdk"]:
        raise ValueError("Build evidence minSdkVersion does not match the toolchain pin.")
    if badging.get("target_sdk") != lock["android"]["target_sdk"]:
        raise ValueError("Build evidence targetSdkVersion does not match the toolchain pin.")

    libs = record.get("native_libraries")
    if not isinstance(libs, dict) or not REQUIRED.issubset(libs):
        raise ValueError("A build omits required production libraries.")
    for name, library_hash in libs.items():
        if not isinstance(name, str) or not re.fullmatch(
            r"lib/arm64-v8a/[^/]+\.so", name
        ):
            raise ValueError(
                "Build evidence contains a non-ARM64 or malformed native library path."
            )
        _sha256(library_hash, f"native_libraries[{name}]")
    return record


def compare(first: dict, second: dict) -> dict:
    first = validate_record(first)
    second = validate_record(second)
    if first["machine"] == second["machine"]:
        raise ValueError("Two builds cannot use the same machine label.")
    if first["provisioning"] == second["provisioning"]:
        raise ValueError("Two builds cannot use the same provisioning label.")
    for field in ("source_sha", "toolchain_sha256", "unity_version", "badging"):
        if first[field] != second[field]:
            raise ValueError(f"Independent builds disagree on {field}.")
    if set(first["native_libraries"]) != set(second["native_libraries"]):
        raise ValueError("Independent APKs have different native-library inventories.")
    return {
        "schema": "rma002-independent-comparison-v1",
        "status": "structurally_compatible_not_release_accepted",
        "source_sha": first["source_sha"],
        "machines": [first["machine"], second["machine"]],
        "apk_byte_identical": first["apk_sha256"] == second["apk_sha256"],
        "native_libraries_byte_identical": (
            first["native_libraries"] == second["native_libraries"]
        ),
        "operator_must_verify_independent_provisioning": True,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    collect = commands.add_parser("capture")
    collect.add_argument("--apk", type=Path, required=True)
    collect.add_argument("--machine", required=True)
    collect.add_argument("--provisioning", required=True)
    collect.add_argument("--output", type=Path, required=True)
    comparison = commands.add_parser("compare")
    comparison.add_argument("first", type=Path)
    comparison.add_argument("second", type=Path)
    comparison.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "capture":
            result = capture(args.apk, args.machine, args.provisioning)
        else:
            result = compare(
                json.loads(args.first.read_text(encoding="utf-8")),
                json.loads(args.second.read_text(encoding="utf-8")),
            )
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8"
        )
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.TimeoutExpired) as exc:
        print(f"RMA-002 evidence rejected: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
