#!/usr/bin/env python3
"""Verify a checkout against a JSON lock containing an exact commit."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from source_checkout import SourceCheckoutError, validate_clean_checkout


class SourceLockError(RuntimeError):
    """Raised when a source lock cannot be read or validated."""


def load_lock(path: Path) -> dict[str, object]:
    """Load a version-one source lock."""
    try:
        lock = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SourceLockError(f"Cannot read source lock {path}: {exc}") from exc
    if not isinstance(lock, dict):
        raise SourceLockError("Source-lock JSON root must be an object.")
    if lock.get("schema_version") != 1:
        raise SourceLockError(f"Unsupported source-lock schema: {lock.get('schema_version')}")
    if "commit" not in lock:
        raise SourceLockError("Source lock is missing 'commit'.")
    return lock


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--lock", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Validate a source checkout and report the pinned revision."""
    args = parse_args()
    try:
        lock = load_lock(args.lock.resolve())
        commit = validate_clean_checkout(args.source.resolve(), lock["commit"])
    except (SourceCheckoutError, SourceLockError) as exc:
        print(f"source checkout validation failed: {exc}", file=sys.stderr)
        return 1
    print(f"source checkout validation passed: {commit}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
