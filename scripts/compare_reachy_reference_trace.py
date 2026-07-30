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


QUATERNION_NORM_TOLERANCE = 1e-6


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


def finite_number(value: object, label: str) -> float:
    """Return one finite JSON number while rejecting booleans."""
    if not isinstance(value, int | float) or isinstance(value, bool):
        raise TraceComparisonError(f"{label} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise TraceComparisonError(f"{label} is not finite")
    return number


def require_unique_names(value: object, label: str) -> list[str]:
    """Return a nonempty ordered list of unique nonempty names."""
    if not isinstance(value, list) or not value:
        raise TraceComparisonError(f"{label} must be a nonempty array")
    result: list[str] = []
    for index, item in enumerate(value):
        if not isinstance(item, str) or not item:
            raise TraceComparisonError(f"{label}[{index}] must be a nonempty string")
        result.append(item)
    if len(set(result)) != len(result):
        raise TraceComparisonError(f"{label} must not contain duplicate names")
    return result


def require_scenario_contract(scenario: dict[str, Any]) -> None:
    """Validate the shared scenario before trusting its dimensions or tolerances."""
    if scenario.get("schema_version") != 1:
        raise TraceComparisonError("Scenario must use schema version 1")
    scenario_id = scenario.get("scenario_id")
    if not isinstance(scenario_id, str) or not scenario_id:
        raise TraceComparisonError("Scenario ID must be a nonempty string")
    source = scenario.get("source")
    if not isinstance(source, dict):
        raise TraceComparisonError("Scenario source must be an object")
    for key in ("commit", "model_sha256", "mujoco_version"):
        value = source.get(key)
        if not isinstance(value, str) or not value:
            raise TraceComparisonError(f"Scenario source {key} must be a nonempty string")

    timestep = finite_number(scenario.get("timestep_seconds"), "scenario timestep")
    if timestep <= 0.0:
        raise TraceComparisonError("Scenario timestep must be positive")
    total_steps = scenario.get("total_steps")
    if not isinstance(total_steps, int) or isinstance(total_steps, bool) or total_steps <= 0:
        raise TraceComparisonError("Scenario total_steps must be a positive integer")

    actuator_names = require_unique_names(scenario.get("actuator_names"), "actuator_names")
    require_unique_names(scenario.get("body_names"), "body_names")

    phases = scenario.get("phases")
    if not isinstance(phases, list) or not phases:
        raise TraceComparisonError("Scenario phases must be a nonempty array")
    previous_start = -1
    for index, phase in enumerate(phases):
        if not isinstance(phase, dict):
            raise TraceComparisonError(f"Scenario phase {index} must be an object")
        start = phase.get("start_step")
        if not isinstance(start, int) or isinstance(start, bool):
            raise TraceComparisonError(f"Scenario phase {index} start_step must be an integer")
        if start <= previous_start or start < 0 or start >= total_steps:
            raise TraceComparisonError("Scenario phase start steps must increase within the run")
        if index == 0 and start != 0:
            raise TraceComparisonError("The first scenario phase must start at step 0")
        previous_start = start
        targets = phase.get("targets_radians")
        if not isinstance(targets, list) or len(targets) != len(actuator_names):
            raise TraceComparisonError(
                f"Scenario phase {index} must contain {len(actuator_names)} targets"
            )
        for target_index, target in enumerate(targets):
            finite_number(target, f"scenario phase {index} target {target_index}")

    checkpoints = scenario.get("checkpoint_steps")
    if not isinstance(checkpoints, list) or not checkpoints:
        raise TraceComparisonError("Scenario checkpoint_steps must be a nonempty array")
    if checkpoints[0] != 0 or checkpoints[-1] != total_steps:
        raise TraceComparisonError("Scenario checkpoints must include step 0 and total_steps")
    previous_checkpoint = -1
    for checkpoint in checkpoints:
        if not isinstance(checkpoint, int) or isinstance(checkpoint, bool):
            raise TraceComparisonError("Scenario checkpoint steps must be integers")
        if checkpoint <= previous_checkpoint:
            raise TraceComparisonError("Scenario checkpoint steps must be strictly increasing")
        previous_checkpoint = checkpoint

    counts = scenario.get("expected_counts")
    if not isinstance(counts, dict):
        raise TraceComparisonError("Scenario expected_counts must be an object")
    for key in (
        "bodies_including_world",
        "joints",
        "actuators",
        "equalities",
        "sites",
        "cameras",
        "nq",
        "nv",
    ):
        value = counts.get(key)
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            raise TraceComparisonError(f"Scenario expected count {key} must be positive")

    tolerances = scenario.get("tolerances")
    if not isinstance(tolerances, dict):
        raise TraceComparisonError("Scenario tolerances must be an object")
    for key in (
        "simulation_time_seconds",
        "qpos_absolute",
        "qvel_absolute",
        "body_position_metres_absolute",
        "body_quaternion_component_absolute",
        "equality_residual_absolute",
        "maximum_equality_residual",
    ):
        tolerance = finite_number(tolerances.get(key), f"scenario tolerance {key}")
        if tolerance < 0.0:
            raise TraceComparisonError(f"Scenario tolerance {key} must not be negative")


def require_metadata(
    scenario: dict[str, Any],
    expected: dict[str, Any],
    actual: dict[str, Any],
    scenario_sha256: str,
) -> None:
    """Require both traces to identify the exact scenario, model, runtime, and platform."""
    source = scenario["source"]
    for label, trace, required_platform in (
        ("desktop", expected, "desktop_reference"),
        ("Android", actual, "android_arm64_api26"),
    ):
        if trace.get("schema_version") != 1 or trace.get("status") != "ok":
            raise TraceComparisonError(f"{label} trace is not a successful schema-1 trace")
        comparisons = {
            "platform": (trace.get("platform"), required_platform),
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
    return [finite_number(item, f"{label}[{index}]") for index, item in enumerate(value)]


def maximum_absolute_error(left: list[float], right: list[float]) -> float:
    """Return the maximum componentwise absolute error."""
    return max((abs(a - b) for a, b in zip(left, right, strict=True)), default=0.0)


def quaternion_error(left: list[float], right: list[float]) -> float:
    """Compare equivalent q and -q quaternion representations."""
    direct = max(abs(a - b) for a, b in zip(left, right, strict=True))
    negated = max(abs(a + b) for a, b in zip(left, right, strict=True))
    return min(direct, negated)


def quaternion_norm_error(value: list[float], label: str) -> float:
    """Require a quaternion to represent a normalized MuJoCo rotation."""
    error = abs(math.sqrt(sum(component * component for component in value)) - 1.0)
    if error > QUATERNION_NORM_TOLERANCE:
        raise TraceComparisonError(
            f"{label} norm error {error:.17g} exceeds {QUATERNION_NORM_TOLERANCE:.17g}"
        )
    return error


def require_warning_free(value: object, label: str, step: int) -> None:
    """Require an explicit integer zero warning count."""
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise TraceComparisonError(f"{label} warning_count is not a nonnegative integer")
    if value != 0:
        raise TraceComparisonError(f"MuJoCo warning present in {label} trace at step {step}")


def compare_traces(
    scenario: dict[str, Any],
    expected: dict[str, Any],
    actual: dict[str, Any],
    scenario_sha256: str,
) -> dict[str, Any]:
    """Compare all checkpoints and return measured maximum errors."""
    require_scenario_contract(scenario)
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
    timestep = float(scenario["timestep_seconds"])
    time_tolerance = float(tolerances["simulation_time_seconds"])
    maxima = {
        "simulation_time_seconds": 0.0,
        "desktop_time_schedule_absolute": 0.0,
        "android_time_schedule_absolute": 0.0,
        "qpos_absolute": 0.0,
        "qvel_absolute": 0.0,
        "body_position_metres_absolute": 0.0,
        "body_quaternion_component_absolute": 0.0,
        "maximum_quaternion_norm_error": 0.0,
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
        require_warning_free(desktop.get("warning_count"), "desktop", step)
        require_warning_free(android.get("warning_count"), "Android", step)

        desktop_time = finite_number(desktop.get("simulation_time"), "desktop simulation_time")
        android_time = finite_number(android.get("simulation_time"), "Android simulation_time")
        scheduled_time = step * timestep
        desktop_schedule_error = abs(desktop_time - scheduled_time)
        android_schedule_error = abs(android_time - scheduled_time)
        maxima["desktop_time_schedule_absolute"] = max(
            maxima["desktop_time_schedule_absolute"], desktop_schedule_error
        )
        maxima["android_time_schedule_absolute"] = max(
            maxima["android_time_schedule_absolute"], android_schedule_error
        )
        if desktop_schedule_error > time_tolerance:
            raise TraceComparisonError(
                f"desktop simulation_time at step {step} differs from the scenario schedule"
            )
        if android_schedule_error > time_tolerance:
            raise TraceComparisonError(
                f"Android simulation_time at step {step} differs from the scenario schedule"
            )
        maxima["simulation_time_seconds"] = max(
            maxima["simulation_time_seconds"], abs(desktop_time - android_time)
        )

        desktop_residual = finite_number(
            desktop.get("maximum_equality_residual"),
            "desktop maximum_equality_residual",
        )
        android_residual = finite_number(
            android.get("maximum_equality_residual"),
            "Android maximum_equality_residual",
        )
        if desktop_residual < 0.0 or android_residual < 0.0:
            raise TraceComparisonError(f"Equality residual must not be negative at step {step}")
        maxima["equality_residual_absolute"] = max(
            maxima["equality_residual_absolute"], abs(desktop_residual - android_residual)
        )
        maxima["maximum_observed_equality_residual"] = max(
            maxima["maximum_observed_equality_residual"],
            desktop_residual,
            android_residual,
        )

        desktop_qpos = numeric_vector(desktop.get("qpos"), "desktop qpos", counts["nq"])
        android_qpos = numeric_vector(android.get("qpos"), "Android qpos", counts["nq"])
        maxima["qpos_absolute"] = max(
            maxima["qpos_absolute"], maximum_absolute_error(desktop_qpos, android_qpos)
        )
        desktop_qvel = numeric_vector(desktop.get("qvel"), "desktop qvel", counts["nv"])
        android_qvel = numeric_vector(android.get("qvel"), "Android qvel", counts["nv"])
        maxima["qvel_absolute"] = max(
            maxima["qvel_absolute"], maximum_absolute_error(desktop_qvel, android_qvel)
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
            maxima["maximum_quaternion_norm_error"] = max(
                maxima["maximum_quaternion_norm_error"],
                quaternion_norm_error(desktop_quaternion, f"desktop {body_name} quaternion"),
                quaternion_norm_error(android_quaternion, f"Android {body_name} quaternion"),
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
        raise TraceComparisonError("Observed equality residual exceeds the bounded-residual policy")
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
            "coordinate_convention": "MuJoCo world metres, native qpos/qvel, quaternion wxyz",
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
    print(f"Reference trace comparison passed: scenario={scenario['scenario_id']} output={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
