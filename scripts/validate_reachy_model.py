#!/usr/bin/env python3
"""Validate a Reachy MJCF against the pinned topology contract and write its map."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from reachy_model_map import (
    ModelMapError,
    build_model_map,
    load_mjcf,
    validate_model_requirements,
    write_model_map,
)

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOCK_PATH = ROOT / "third_party" / "reachy-mini-source.lock.json"


class ModelValidationError(RuntimeError):
    """Raised when the source lock cannot be loaded or used."""


def read_lock(path: Path) -> dict[str, Any]:
    """Read the source lock as a JSON object."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ModelValidationError(f"Cannot read source lock {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ModelValidationError(f"Source lock root must be an object: {path}")
    for key in ("model_file", "model_requirements"):
        if key not in value:
            raise ModelValidationError(f"Source lock is missing {key!r}: {path}")
    return value


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--lock", type=Path, default=DEFAULT_LOCK_PATH)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Validate topology, write the map, and return an explicit process status."""
    args = parse_args()
    try:
        lock = read_lock(args.lock.resolve())
        model_path = args.model.resolve()
        if not model_path.is_file():
            raise ModelValidationError(f"Reachy MJCF is not a file: {model_path}")
        root = load_mjcf(model_path, lock["model_file"])
        model_map = build_model_map(root, model_path, lock["model_file"])
        validate_model_requirements(model_map, lock["model_requirements"])
        output_path = args.output.resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        write_model_map(output_path, model_map)
    except (ModelMapError, ModelValidationError) as exc:
        print(f"Reachy model validation failed: {exc}", file=sys.stderr)
        return 1

    counts = model_map["counts"]
    print(
        "Reachy model validation passed: "
        f"model={model_map['model']} "
        f"bodies={counts['bodies']} "
        f"joints={counts['joints']} "
        f"actuators={counts['actuators']} "
        f"equalities={counts['equalities']} "
        f"sites={counts['sites']} "
        f"cameras={counts['cameras']} "
        f"output={output_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
