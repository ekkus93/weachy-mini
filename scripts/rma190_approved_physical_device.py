#!/usr/bin/env python3
"""Check RMA-190 physical-device identity against the approved RMA-184 matrix."""

from __future__ import annotations

import json
import sys
from pathlib import Path

MATRIX = Path(__file__).resolve().parents[1] / "models/reachy-mini/android-device-matrix.json"


def is_approved(manufacturer: str, model: str) -> bool:
    if not manufacturer or not model:
        return False
    entries = json.loads(MATRIX.read_text(encoding="utf-8"))["representative_devices"]
    if not isinstance(entries, list):
        raise ValueError("RMA-184 representative_devices must be a list")
    matches = []
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("RMA-184 contains a non-object device entry")
        if (
            isinstance(entry.get("manufacturer"), str)
            and entry["manufacturer"].casefold() == manufacturer.casefold()
            and entry.get("model") == model
        ):
            matches.append(entry)
    return len(matches) == 1 and matches[0].get("support_status") in (
        "supported",
        "supported_with_limitations",
    )


def main() -> int:
    if len(sys.argv) != 3:
        print("Expected manufacturer and model", file=sys.stderr)
        return 2
    try:
        if not is_approved(sys.argv[1], sys.argv[2]):
            print("Device is not an approved RMA-184 physical model", file=sys.stderr)
            return 2
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"RMA-184 matrix approval failed: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
