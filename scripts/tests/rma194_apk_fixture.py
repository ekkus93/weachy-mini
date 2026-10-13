"""Small valid ELF fixture helpers for APK archive audit tests."""

from __future__ import annotations

import struct


def elf64_aarch64_shared(
    *,
    elf_class: int = 2,
    byte_order: int = 1,
    elf_type: int = 3,
    machine: int = 183,
    version: int = 1,
) -> bytes:
    """Return a synthetic ELF with bounded load and dynamic segments."""
    header = bytearray(176)
    header[:7] = bytes((0x7F, 0x45, 0x4C, 0x46, elf_class, byte_order, 1))
    struct.pack_into("<HHI", header, 16, elf_type, machine, version)
    struct.pack_into("<Q", header, 32, 64)
    struct.pack_into("<HHH", header, 52, 64, 56, 2)
    struct.pack_into("<IIQQQQQQ", header, 64, 1, 5, 0, 0, 0, 176, 176, 4096)
    struct.pack_into("<IIQQQQQQ", header, 120, 2, 6, 0, 0, 0, 176, 176, 8)
    return bytes(header)
