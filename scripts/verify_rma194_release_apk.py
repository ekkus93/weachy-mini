#!/usr/bin/env python3
"""Bounded APK audit for deferred RMA-194 AR/XR payloads."""

from __future__ import annotations

import argparse
import hashlib
import json
import stat
import sys
import zipfile
from pathlib import Path

FORBIDDEN_FILES = (
    "libarcore_sdk_c.so",
    "libopenxr_loader.so",
    "unity.xr.arfoundation",
    "unity.xr.arsubsystems",
    "unity.xr.openxr",
    "unity.xr.interaction",
    "com/google/ar/core/",
    "/arcore/",
)
FORBIDDEN_MANIFEST = (
    "com.google.ar.core",
    "android.hardware.camera.ar",
    "com.unity.xr",
)


def inspect_apk(path: Path) -> dict[str, object]:
    if not path.is_file() or path.is_symlink():
        raise ValueError("APK is missing or is not a regular file")
    with zipfile.ZipFile(path) as apk:
        entries = apk.infolist()
        if not entries or len(entries) > 50000:
            raise ValueError("APK entry count exceeds audit limit")
        names: set[str] = set()
        for entry in entries:
            name = entry.filename
            folded = name.casefold()
            parts = name.split("/")
            if entry.is_dir():
                parts = parts[:-1]
            if not parts or any(part in ("", ".", "..") for part in parts):
                raise ValueError("APK contains noncanonical entry names")
            if name.startswith("/") or "\\" in name or folded in names:
                raise ValueError("APK contains unsafe or duplicate entry names")
            # APK contents are untrusted ZIP entries; never accept a link or
            # special file masquerading as a required library or Unity asset.
            kind = stat.S_IFMT(entry.external_attr >> 16)
            if kind not in (0, stat.S_IFREG, stat.S_IFDIR):
                raise ValueError(f"APK contains nonregular entry: {name}")
            if kind and (kind == stat.S_IFDIR) != entry.is_dir():
                raise ValueError(f"APK entry type disagrees with its path: {name}")
            names.add(folded)
            if any(token in folded for token in FORBIDDEN_FILES):
                raise ValueError(f"Deferred AR/XR payload present: {name}")
        # ZIP lookups are case-sensitive even though duplicate detection is
        # case-insensitive. Require the actual Android package entry spelling.
        if "AndroidManifest.xml" not in {entry.filename for entry in entries}:
            raise ValueError("APK AndroidManifest.xml is missing")
        info = apk.getinfo("AndroidManifest.xml")
        if info.is_dir() or info.file_size == 0:
            raise ValueError("APK AndroidManifest.xml is not a nonempty file")
        if info.file_size > 2 * 1024 * 1024:
            raise ValueError("APK manifest exceeds audit limit")
        manifest = apk.read("AndroidManifest.xml")
        for token in FORBIDDEN_MANIFEST:
            if token.encode("utf-8") in manifest or token.encode("utf-16le") in manifest:
                raise ValueError(f"Deferred AR/XR manifest declaration: {token}")
        native = [
            entry.filename
            for entry in entries
            if entry.filename.startswith("lib/arm64-v8a/") and entry.filename.endswith(".so")
        ]
        required_native = {
            "libil2cpp.so", "libmujoco.so", "libreachy_sim.so", "libreachy_llama.so"
        }
        observed_native = {Path(name).name for name in native}
        missing_native = required_native - observed_native
        for name in native:
            if Path(name).name in required_native and apk.getinfo(name).file_size == 0:
                raise ValueError(f"APK has empty required ARM64 runtime library: {name}")
        if missing_native:
            raise ValueError(
                f"APK lacks required ARM64 runtime libraries: {sorted(missing_native)}"
            )
        if not any(
            entry.filename.casefold().startswith("assets/bin/data/")
            and not entry.is_dir()
            and entry.file_size > 0
            for entry in entries
        ):
            raise ValueError("APK lacks nonempty Unity data payload")
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return {
        "schema_version": 1,
        "status": "passed",
        "apk_sha256": digest.hexdigest(),
        "zip_entry_count": len(entries),
        "arm64_native_library_count": len(native),
        "deferred_payloads_detected": 0,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    args = parser.parse_args()
    try:
        result = inspect_apk(args.apk)
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError) as exc:
        print(f"RMA-194 APK audit failed: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
