#!/usr/bin/env python3
"""Validate the compact RMA-042 desktop reference-trace lock."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")


class TraceLockError(RuntimeError):
    """Raised when reference trace identity or bytes differ from the lock."""


def read_json(path: Path) -> tuple[dict[str, Any], bytes]:
    """Read a JSON object and preserve its exact bytes."""
    try:
        raw = path.read_bytes()
        value = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise TraceLockError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise TraceLockError(f"JSON root must be an object: {path}")
    return value, raw


def require_string(value: object, label: str) -> str:
    """Return one required nonempty string."""
    if not isinstance(value, str) or not value:
        raise TraceLockError(f"{label} must be a nonempty string")
    return value


def require_sha256(value: object, label: str) -> str:
    """Return one lowercase hexadecimal SHA-256 digest."""
    digest = require_string(value, label)
    if SHA256_PATTERN.fullmatch(digest) is None:
        raise TraceLockError(f"{label} must contain 64 lowercase hexadecimal characters")
    return digest


def validate_lock(
    scenario: dict[str, Any],
    scenario_raw: bytes,
    lock: dict[str, Any],
) -> None:
    """Require the compact lock to identify the exact scenario and source."""
    if lock.get("schema_version") != 1:
        raise TraceLockError("Unsupported reference trace lock schema")
    source = scenario.get("source")
    if not isinstance(source, dict):
        raise TraceLockError("Scenario source must be an object")
    expected = {
        "scenario_id": scenario.get("scenario_id"),
        "scenario_sha256": hashlib.sha256(scenario_raw).hexdigest(),
        "source_model_sha256": source.get("model_sha256"),
        "mujoco_version": source.get("mujoco_version"),
    }
    for field, required in expected.items():
        actual = lock.get(field)
        if actual != required:
            raise TraceLockError(
                f"Trace lock {field} mismatch: expected {required!r}, found {actual!r}"
            )
    require_sha256(lock.get("scenario_sha256"), "lock.scenario_sha256")
    require_sha256(lock.get("source_model_sha256"), "lock.source_model_sha256")
    require_sha256(lock.get("trace_sha256"), "lock.trace_sha256")

    generation = lock.get("trace_generation")
    if not isinstance(generation, dict):
        raise TraceLockError("lock.trace_generation must be an object")
    if generation.get("script") != "scripts/generate_reachy_reference_trace.py":
        raise TraceLockError("Trace lock identifies an unexpected generator")
    if generation.get("format") != "UTF-8 JSON, sorted keys, two-space indentation, trailing newline":
        raise TraceLockError("Trace lock identifies an unexpected serialization format")
    if generation.get("checkpoints") != len(scenario.get("checkpoint_steps", [])):
        raise TraceLockError("Trace lock checkpoint count differs from scenario")
    if generation.get("total_steps") != scenario.get("total_steps"):
        raise TraceLockError("Trace lock total step count differs from scenario")


def validate_trace(
    scenario: dict[str, Any],
    scenario_raw: bytes,
    lock: dict[str, Any],
    trace: dict[str, Any],
    trace_raw: bytes,
) -> None:
    """Require a generated trace to match the compact lock exactly."""
    source = scenario["source"]
    expected = {
        "schema_version": 1,
        "status": "ok",
        "platform": "desktop_reference",
        "scenario_id": scenario["scenario_id"],
        "scenario_sha256": hashlib.sha256(scenario_raw).hexdigest(),
        "source_model_sha256": source["model_sha256"],
        "mujoco_version": source["mujoco_version"],
        "compiled_counts": scenario["expected_counts"],
    }
    for field, required in expected.items():
        actual = trace.get(field)
        if actual != required:
            raise TraceLockError(
                f"Desktop trace {field} mismatch: expected {required!r}, found {actual!r}"
            )
    checkpoints = trace.get("checkpoints")
    if not isinstance(checkpoints, list):
        raise TraceLockError("Desktop trace checkpoints must be an array")
    actual_steps = [
        checkpoint.get("step") if isinstance(checkpoint, dict) else None
        for checkpoint in checkpoints
    ]
    if actual_steps != scenario["checkpoint_steps"]:
        raise TraceLockError("Desktop trace checkpoint steps differ from scenario")
    actual_sha256 = hashlib.sha256(trace_raw).hexdigest()
    if actual_sha256 != lock["trace_sha256"]:
        raise TraceLockError(
            "Desktop trace SHA-256 mismatch: "
            f"expected {lock['trace_sha256']}, found {actual_sha256}"
        )


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True, type=Path)
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--trace", type=Path)
    return parser.parse_args()


def main() -> int:
    """Validate lock metadata and optional generated trace bytes."""
    args = parse_args()
    try:
        scenario, scenario_raw = read_json(args.scenario.resolve())
        lock, _ = read_json(args.lock.resolve())
        validate_lock(scenario, scenario_raw, lock)
        if args.trace is not None:
            trace, trace_raw = read_json(args.trace.resolve())
            validate_trace(scenario, scenario_raw, lock, trace, trace_raw)
    except (TraceLockError, KeyError) as exc:
        print(f"Reference trace lock validation failed: {exc}", file=sys.stderr)
        return 1
    mode = "lock-and-trace" if args.trace is not None else "lock"
    print(f"Reference trace lock validation passed: mode={mode} scenario={scenario['scenario_id']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
