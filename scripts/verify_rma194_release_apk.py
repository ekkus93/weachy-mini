#!/usr/bin/env python3
"""Bounded APK audit for deferred RMA-194 AR/XR payloads."""

from __future__ import annotations

import argparse
import hashlib
import json
import stat
import struct
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


def _verify_required_arm64_elf(apk: zipfile.ZipFile, name: str) -> None:
    """Check the bounded ELF layout, not just an easily forged 64-byte header."""
    info = apk.getinfo(name)
    if info.file_size < 64:
        raise ValueError(f"APK has truncated ELF header in required ARM64 library: {name}")
    with apk.open(info) as library:
        header = library.read(64)
        if (
            len(header) != 64
            or header[:4] != bytes((127, 69, 76, 70))
            or header[4] != 2  # ELFCLASS64
            or header[5] != 1  # ELFDATA2LSB
            or header[6] != 1  # EV_CURRENT
        ):
            raise ValueError(
                f"APK has invalid ELF64 header in required ARM64 library: {name}"
            )
        elf_type, machine, version = struct.unpack_from("<HHI", header, 16)
        if elf_type != 3 or machine != 183 or version != 1:
            raise ValueError(
                f"APK requires ELF64 AArch64 shared objects (ET_DYN): {name}"
            )

        # A 64-byte file with a forged ELF identity is not a loadable .so.
        # Keep the table offset/count bounded before seeking in compressed ZIP
        # members; validate segment extents without decompressing the full .so.
        phoff = struct.unpack_from("<Q", header, 32)[0]
        ehsize, phentsize, phnum = struct.unpack_from("<HHH", header, 52)
        table_size = phnum * phentsize
        if (
            ehsize != 64
            or phentsize != 56  # sizeof(Elf64_Phdr)
            or not 1 <= phnum <= 128
            or not 64 <= phoff <= 65536
            or phoff + table_size > info.file_size
        ):
            raise ValueError(f"APK has invalid ELF program-header table: {name}")
        library.seek(phoff)
        table = library.read(table_size)
        if len(table) != table_size:
            raise ValueError(f"APK has truncated ELF program-header table: {name}")

    has_load = False
    has_dynamic = False
    file_backed_loads: list[tuple[int, int]] = []
    dynamic_ranges: list[tuple[int, int]] = []
    for offset in range(0, len(table), 56):
        p_type, _flags, p_offset, _vaddr, _paddr, filesz, memsz, _align = (
            struct.unpack_from("<IIQQQQQQ", table, offset)
        )
        if p_type not in (1, 2):  # PT_LOAD / PT_DYNAMIC
            continue
        if filesz > memsz or p_offset + filesz > info.file_size:
            raise ValueError(f"APK has invalid ELF segment extent: {name}")
        if p_type == 1:
            has_load = True
            if filesz:
                file_backed_loads.append((p_offset, p_offset + filesz))
        else:
            has_dynamic = True
            if filesz:
                dynamic_ranges.append((p_offset, p_offset + filesz))
    if not (has_load and has_dynamic and file_backed_loads and dynamic_ranges):
        raise ValueError(
            f"APK required ARM64 library lacks nonempty load/dynamic segments: {name}"
        )
    # PT_DYNAMIC must point to bytes actually mapped by PT_LOAD, not merely
    # to an arbitrary region of the archive member or an empty segment.
    for start, end in dynamic_ranges:
        if not any(load_start <= start and end <= load_end for load_start, load_end in file_backed_loads):
            raise ValueError(
                f"APK ARM64 dynamic segment is not file-backed by PT_LOAD: {name}"
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
        # The shipping build targets ARM64 exclusively. A second ABI or a
        # nested .so is not loadable through Android's standard JNI layout;
        # validate every packaged library, not just the four required names.
        packaged_native = [
            entry.filename
            for entry in entries
            if entry.filename.startswith("lib/") and entry.filename.endswith(".so")
        ]
        native = []
        for name in packaged_native:
            if (
                not name.startswith("lib/arm64-v8a/")
                or "/" in name.removeprefix("lib/arm64-v8a/")
            ):
                raise ValueError(
                    f"APK contains unsupported native library layout: {name}"
                )
            native.append(name)
        required_native = {
            "libil2cpp.so",
            "libmujoco.so",
            "libreachy_sim.so",
            "libreachy_llama.so",
        }
        # Only direct lib/arm64-v8a/<name> entries satisfy Android's loader
        # layout. The inventory above rejects unexpected ABIs and nesting.
        observed_native = {name.removeprefix("lib/arm64-v8a/") for name in native}
        missing_native = required_native - observed_native
        if missing_native:
            raise ValueError(
                f"APK lacks required ARM64 runtime libraries: {sorted(missing_native)}"
            )
        for name in native:
            if apk.getinfo(name).file_size == 0:
                raise ValueError(f"APK has empty ARM64 native library: {name}")
            _verify_required_arm64_elf(apk, name)
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
