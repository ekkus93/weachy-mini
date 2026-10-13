#!/usr/bin/env python3
"""Resolve the physical RMA-184 qualification class from the authoritative matrix."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

CLASS_INDEX = {"low": 0, "mid": 1, "high": 2}


def resolve(matrix: dict, model: str) -> int:
    if not isinstance(matrix, dict):
        raise ValueError("RMA-184 matrix must be a JSON object")
    if not isinstance(model, str) or not model.strip():
        raise ValueError("RMA-184 physical model must be a nonempty string")
    entries = matrix.get("representative_devices")
    if not isinstance(entries, list):
        raise ValueError("representative_devices must be a JSON array")
    if any(not isinstance(entry, dict) for entry in entries):
        raise ValueError("representative_devices entries must be JSON objects")
    matches = [entry for entry in entries if entry.get("model") == model]
    if len(matches) != 1:
        raise ValueError(
            f"expected exactly one registered representative model {model!r}; found {len(matches)}"
        )
    performance_class = matches[0].get("performance_class")
    if not isinstance(performance_class, str) or performance_class not in CLASS_INDEX:
        raise ValueError(f"unknown registered RMA-184 class {performance_class!r}")
    return CLASS_INDEX[performance_class]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--model", required=True)
    args = parser.parse_args()
    try:
        matrix = json.loads(args.matrix.read_text(encoding="utf-8"))
        selected = resolve(matrix, args.model)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"RMA-184 device-class selection failed: {error}", file=sys.stderr)
        return 2
    print(selected)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
