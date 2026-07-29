#!/usr/bin/env python3
"""Validate the machine-readable third-party inventory."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
INVENTORY_PATH = ROOT / "third_party" / "inventory.json"
REQUIRED_FIELDS = {
    "id",
    "name",
    "owner",
    "license",
    "source_url",
    "status",
    "modification_status",
    "redistribution_status",
    "required_notice",
}
PACKAGED_STATUSES = {"vendored", "downloaded", "packaged"}


def load_inventory() -> dict[str, Any]:
    """Load the inventory or raise a descriptive exception."""
    return json.loads(INVENTORY_PATH.read_text(encoding="utf-8"))


def main() -> int:
    """Validate inventory structure and packaged-entry provenance."""
    try:
        inventory = load_inventory()
    except (OSError, json.JSONDecodeError) as exc:
        print(f"inventory validation failed: {exc}", file=sys.stderr)
        return 1

    errors: list[str] = []
    if inventory.get("schema_version") != 1:
        errors.append("schema_version must equal 1")

    entries = inventory.get("entries")
    if not isinstance(entries, list):
        errors.append("entries must be a list")
        entries = []

    seen_ids: set[str] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            errors.append(f"entry {index} is not an object")
            continue
        missing = sorted(REQUIRED_FIELDS.difference(entry))
        if missing:
            errors.append(f"entry {index} is missing: {', '.join(missing)}")
        entry_id = entry.get("id")
        if isinstance(entry_id, str):
            if entry_id in seen_ids:
                errors.append(f"duplicate entry id: {entry_id}")
            seen_ids.add(entry_id)
        status = entry.get("status")
        if status in PACKAGED_STATUSES and not entry.get("source_revision"):
            errors.append(f"packaged entry {entry_id!r} must have source_revision")
        if status in PACKAGED_STATUSES and entry.get("redistribution_status") == "unknown":
            errors.append(f"packaged entry {entry_id!r} has unknown redistribution status")

    if errors:
        print("inventory validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(f"inventory validation passed ({len(entries)} entries)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
