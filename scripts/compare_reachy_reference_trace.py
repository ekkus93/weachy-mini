#!/usr/bin/env python3
"""Compare an Android Reachy trace with the pinned desktop reference trace."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any


class TraceComparisonError(RuntimeError):
    """Raised when trace metadata or numerical results differ beyond policy."""


def read_json(path: Path) -> tuple[dict[str, Any], str]:
    """Read a JSON object and return its exact SHA-256."""
    try:
        raw = path.read_bytes()
        value = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise TraceComparisonError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise TraceComparisonError(f"JSON root must be an object: {path}")
    return value, hashlib.sha256(raw).hexdigest()


def require_metadata(
    scenario: dict[str, Any],
    expected: dict[str, Any],
    actual: dict[str, Any],
    scenario_sha256: str,
) -> None:
    """Require both traces to identify the exact scenario, model, and runtime."""
    source = scenario["source"]
    for label, trace in (("desktop", expected), ("Android", actual)):
        if trace.get("schema_version") != 1 or trace.get("status") != "ok":
            raise TraceComparisonError(f"{label} trace is not a successful schema-1 trace")
        comparisons = {
            "scenario ID": (trace.get("scenario_id"), scenario["scenario_id"]),
            "scenario SHA-256": (trace.get("scenario_sha256"), scenario_sha256),
            "model SHA-256": (trace.get("source_model_sha256"), source["model_sha256"]),
            "MuJoCo version": (trace.get("mujoco_version"), source["mujoco_version"]),
            "compiled counts": (trace.get("compiled_counts"), scenario["expected_counts"]),
        }
        for field, (observed, required) in comparisons.items():
            if observed != required:
                raise TraceComparisonError(
                    f"{label} {field} mismatch: expected {required!r}, found {observed!r}"
                )


def numeric_vector(value: object, label: str, length: int) -> list[float]:
    """Return a finite fixed-length numeric array."""
    if not isinstance(value, list) or len(value) != length:
        raise TraceComparisonError(f"{label} must contain {length} values")
    result: list[float] = []
    for index, item in enumerate(value):
        if not isinstance(item, int | float) or isinstance(item, bool):
            raise TraceComparisonError(f"{label}[{index}] must be numeric")
        number = float(item)
        if not math.isfinite(number):
            raise TraceComparisonError(f"{label}[{index}] is not finite")
        result.append(number)
    return result


def maximum_absolute_error(left: list[float], right: list[float]) -> float:
    """Return the maximum componentwise absolute error."""
    return max((abs(a - b) for a, b in zip(left, right, strict=True)), default=0.0)


def quaternion_error(left: list[float], right: list[float]) -> float:
    """Compare equivalent q and -q quaternion representations."""
    direct = max(abs(a - b) for a, b in zip(left, right, strict=True))
    negated = max(abs(a + b) for a, b in zip(left, right, strict=True))
    return min(direct, negated)


def compare_traces(
    scenario: dict[str, Any],
    expected: dict[str, Any],
    actual: dict[str, Any],
    scenario_sha256: str,
) -> dict[str, Any]:
    """Compare all checkpoints and return measured maximum errors."""
    require_metadata(scenario, expected, actual, scenario_sha256)
    expected_checkpoints = expected.get("checkpoints")
    actual_checkpoints = actual.get("checkpoints")
    if not isinstance(expected_checkpoints, list) or not isinstance(actual_checkpoints, list):
        raise TraceComparisonError("Trace checkpoints must be arrays")
    checkpoint_steps = scenario["checkpoint_steps"]
    if len(expected_checkpoints) != len(checkpoint_steps) or len(actual_checkpoints) != len(
        checkpoint_steps
    ):
        raise TraceComparisonError("Trace checkpoint count differs from scenario")

    counts = scenario["expected_counts"]
    body_names = scenario["body_names"]
    tolerances = scenario["tolerances"]
    maxima = {
        "simulation_time_seconds": 0.0,
        "qpos_absolute": 0.0,
        "qvel_absolute": 0.0,
        "body_position_metres_absolute": 0.0,
        "body_quaternion_component_absolute": 0.0,
        "equality_residual_absolute": 0.0,
        "maximum_observed_equality_residual": 0.0,
    }

    for index, step in enumerate(checkpoint_steps):
        desktop = expected_checkpoints[index]
        android = actual_checkpoints[index]
        if not isinstance(desktop, dict) or not isinstance(android, dict):
            raise TraceComparisonError(f"Checkpoint {index} must be an object")
        if desktop.get("step") != step or android.get("step") != step:
            raise TraceComparisonError(f"Checkpoint step mismatch at index {index}")
        if desktop.get("warning_count") != 0 or android.get("warning_count") != 0:
            raise TraceComparisonError(f"MuJoCo warning present at checkpoint {step}")

        desktop_time = float(desktop["simulation_time"])
        android_time = float(android["simulation_time"])
        maxima["simulation_time_seconds"] = max(
            maxima["simulation_time_seconds"],
            abs(desktop_time - android_time),
        )
        desktop_residual = float(desktop["maximum_equality_residual"])
        android_residual = float(android["maximum_equality_residual"])
        if not math.isfinite(desktop_residual) or not math.isfinite(android_residual):
            raise TraceComparisonError(f"Non-finite equality residual at step {step}")
        maxima["equality_residual_absolute"] = max(
            maxima["equality_residual_absolute"],
            abs(desktop_residual - android_residual),
        )
        maxima["maximum_observed_equality_residual"] = max(
            maxima["maximum_observed_equality_residual"],
            desktop_residual,
            android_residual,
        )

        desktop_qpos = numeric_vector(desktop.get("qpos"), "desktop qpos", counts["nq"])
        android_qpos = numeric_vector(android.get("qpos"), "Android qpos", counts["nq"])
        maxima["qpos_absolute"] = max(
            maxima["qpos_absolute"],
            maximum_absolute_error(desktop_qpos, android_qpos),
        )
        desktop_qvel = numeric_vector(desktop.get("qvel"), "desktop qvel", counts["nv"])
        android_qvel = numeric_vector(android.get("qvel"), "Android qvel", counts["nv"])
        maxima["qvel_absolute"] = max(
            maxima["qvel_absolute"],
            maximum_absolute_error(desktop_qvel, android_qvel),
        )

        desktop_bodies = desktop.get("bodies")
        android_bodies = android.get("bodies")
        if not isinstance(desktop_bodies, list) or not isinstance(android_bodies, list):
            raise TraceComparisonError(f"Body transforms missing at step {step}")
        if len(desktop_bodies) != len(body_names) or len(android_bodies) != len(body_names):
            raise TraceComparisonError(f"Body transform count mismatch at step {step}")
        for body_index, body_name in enumerate(body_names):
            desktop_body = desktop_bodies[body_index]
            android_body = android_bodies[body_index]
            if not isinstance(desktop_body, dict) or not isinstance(android_body, dict):
                raise TraceComparisonError(f"Invalid body transform at step {step}")
            if desktop_body.get("name") != body_name or android_body.get("name") != body_name:
                raise TraceComparisonError(
                    f"Body order/name mismatch at step {step}, index {body_index}"
                )
            desktop_position = numeric_vector(
                desktop_body.get("position_metres"),
                f"desktop {body_name} position",
                3,
            )
            android_position = numeric_vector(
                android_body.get("position_metres"),
                f"Android {body_name} position",
                3,
            )
            maxima["body_position_metres_absolute"] = max(
                maxima["body_position_metres_absolute"],
                maximum_absolute_error(desktop_position, android_position),
            )
            desktop_quaternion = numeric_vector(
                desktop_body.get("quaternion_wxyz"),
                f"desktop {body_name} quaternion",
                4,
            )
            android_quaternion = numeric_vector(
                android_body.get("quaternion_wxyz"),
                f"Android {body_name} quaternion",
                4,
            )
            maxima["body_quaternion_component_absolute"] = max(
                maxima["body_quaternion_component_absolute"],
                quaternion_error(desktop_quaternion, android_quaternion),
            )

    comparison_keys = (
        "simulation_time_seconds",
        "qpos_absolute",
        "qvel_absolute",
        "body_position_metres_absolute",
        "body_quaternion_component_absolute",
        "equality_residual_absolute",
    )
    for key in comparison_keys:
        if maxima[key] > float(tolerances[key]):
            raise TraceComparisonError(
                f"{key} error {maxima[key]:.17g} exceeds tolerance "
                f"{float(tolerances[key]):.17g}"
            )
    if maxima["maximum_observed_equality_residual"] > float(
        tolerances["maximum_equality_residual"]
    ):
        raise TraceComparisonError(
            "Observed equality residual exceeds the bounded-residual policy"
        )
    return maxima


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True, type=Path)
    parser.add_argument("--desktop", required=True, type=Path)
    parser.add_argument("--android", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Compare traces and write a compact evidence summary."""
    args = parse_args()
    try:
        scenario, scenario_sha256 = read_json(args.scenario.resolve())
        desktop, desktop_sha256 = read_json(args.desktop.resolve())
        android, android_sha256 = read_json(args.android.resolve())
        maxima = compare_traces(scenario, desktop, android, scenario_sha256)
        summary = {
            "schema_version": 1,
            "status": "ok",
            "scenario_id": scenario["scenario_id"],
            "scenario_sha256": scenario_sha256,
            "desktop_trace_sha256": desktop_sha256,
            "android_trace_sha256": android_sha256,
            "maximum_errors": maxima,
            "tolerances": scenario["tolerances"],
        }
        output = args.output.resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(
            json.dumps(summary, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    except (TraceComparisonError, KeyError, TypeError, ValueError) as exc:
        print(f"Reference trace comparison failed: {exc}", file=sys.stderr)
        return 1
    print(
        "Reference trace comparison passed: "
        f"scenario={scenario['scenario_id']} output={output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
