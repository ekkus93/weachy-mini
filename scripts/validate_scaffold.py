#!/usr/bin/env python3
"""Validate required repository structure and reject sensitive/generated files."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQUIRED_PATHS = (
    "Assets/ReachyMini/Runtime",
    "Assets/ReachyMini/Editor",
    "Assets/ReachyMini/Tests",
    "Assets/Plugins/Android",
    "Packages",
    "ProjectSettings",
    "android-plugin/src/main/java",
    "native/reachy_sim/include",
    "native/reachy_sim/src",
    "native/reachy_sim/tests",
    "native/llama_runtime",
    "native/cmake",
    "models/manifests",
    "calibration/schemas",
    "docs",
    "scripts",
    "third_party",
)
FORBIDDEN_NAMES = {"local.properties", "keystore.properties", "secrets.properties"}
FORBIDDEN_SUFFIXES = {".aab", ".apk", ".gguf", ".jks", ".keystore", ".safetensors"}
FORBIDDEN_PARTS = {"Library", "Temp", "UserSettings", ".gradle", ".cxx"}


def main() -> int:
    """Validate the repository scaffold."""
    errors: list[str] = []
    for relative_path in REQUIRED_PATHS:
        if not (ROOT / relative_path).exists():
            errors.append(f"missing required path: {relative_path}")

    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(ROOT)
        if path.name in FORBIDDEN_NAMES:
            errors.append(f"forbidden machine-specific or secret file: {relative}")
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            errors.append(f"forbidden binary or credential file: {relative}")
        if FORBIDDEN_PARTS.intersection(relative.parts):
            errors.append(f"generated directory content is tracked: {relative}")

    if errors:
        print("scaffold validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("scaffold validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
