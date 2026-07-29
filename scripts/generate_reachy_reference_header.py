#!/usr/bin/env python3
"""Generate the native RMA-042 scenario header from its JSON source."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

COUNT_KEYS = (
    ("BODY", "bodies_including_world"),
    ("JOINT", "joints"),
    ("ACTUATOR", "actuators"),
    ("EQUALITY", "equalities"),
    ("SITE", "sites"),
    ("CAMERA", "cameras"),
)


class ScenarioError(RuntimeError):
    """Raised when the reference scenario is malformed or stale."""


def require_object(value: object, label: str) -> dict[str, Any]:
    """Return a required JSON object."""
    if not isinstance(value, dict):
        raise ScenarioError(f"{label} must be an object")
    return value


def require_array(value: object, label: str) -> list[Any]:
    """Return a required JSON array."""
    if not isinstance(value, list):
        raise ScenarioError(f"{label} must be an array")
    return value


def require_names(value: object, label: str) -> list[str]:
    """Return a nonempty array of unique names."""
    values = require_array(value, label)
    if not values or not all(isinstance(item, str) and item for item in values):
        raise ScenarioError(f"{label} must contain nonempty strings")
    names = list(values)
    if len(names) != len(set(names)):
        raise ScenarioError(f"{label} contains duplicate names")
    return names


def require_number(value: object, label: str) -> float:
    """Return a finite JSON number represented as float."""
    if not isinstance(value, int | float) or isinstance(value, bool):
        raise ScenarioError(f"{label} must be numeric")
    number = float(value)
    if not (-float("inf") < number < float("inf")):
        raise ScenarioError(f"{label} must be finite")
    return number


def read_scenario(path: Path) -> tuple[dict[str, Any], str]:
    """Read, validate, and hash the scenario's exact bytes."""
    try:
        raw = path.read_bytes()
        scenario = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise ScenarioError(f"Cannot read scenario {path}: {exc}") from exc
    scenario = require_object(scenario, "scenario root")
    validate_scenario(scenario)
    return scenario, hashlib.sha256(raw).hexdigest()


def validate_scenario(scenario: dict[str, Any]) -> None:
    """Validate fields consumed by the generated native runner."""
    if scenario.get("schema_version") != 1:
        raise ScenarioError("Unsupported scenario schema version")
    scenario_id = scenario.get("scenario_id")
    if not isinstance(scenario_id, str) or not scenario_id:
        raise ScenarioError("scenario_id must be a nonempty string")
    source = require_object(scenario.get("source"), "source")
    for field in ("model_sha256", "mujoco_version"):
        if not isinstance(source.get(field), str) or not source[field]:
            raise ScenarioError(f"source.{field} must be a nonempty string")
    if len(source["model_sha256"]) != 64:
        raise ScenarioError("source.model_sha256 must be a SHA-256 string")

    timestep = require_number(scenario.get("timestep_seconds"), "timestep_seconds")
    if timestep <= 0.0:
        raise ScenarioError("timestep_seconds must be positive")
    total_steps = scenario.get("total_steps")
    if not isinstance(total_steps, int) or isinstance(total_steps, bool) or total_steps <= 0:
        raise ScenarioError("total_steps must be a positive integer")

    actuators = require_names(scenario.get("actuator_names"), "actuator_names")
    require_names(scenario.get("body_names"), "body_names")
    counts = require_object(scenario.get("expected_counts"), "expected_counts")
    required_count_keys = {key for _, key in COUNT_KEYS} | {"nq", "nv"}
    if set(counts) != required_count_keys:
        raise ScenarioError("expected_counts has an unexpected key set")
    if any(
        not isinstance(value, int) or isinstance(value, bool) or value < 0
        for value in counts.values()
    ):
        raise ScenarioError("expected_counts values must be nonnegative integers")
    if counts["actuators"] != len(actuators):
        raise ScenarioError("Actuator count differs from actuator_names")

    phases = require_array(scenario.get("phases"), "phases")
    if not phases:
        raise ScenarioError("phases must not be empty")
    previous_start = -1
    for index, raw_phase in enumerate(phases):
        phase = require_object(raw_phase, f"phases[{index}]")
        start = phase.get("start_step")
        name = phase.get("name")
        targets = require_array(phase.get("targets_radians"), f"phases[{index}].targets")
        if not isinstance(start, int) or isinstance(start, bool):
            raise ScenarioError(f"phases[{index}].start_step must be an integer")
        if start <= previous_start or start < 0 or start >= total_steps:
            raise ScenarioError("Phase start steps must increase within total_steps")
        if not isinstance(name, str) or not name:
            raise ScenarioError(f"phases[{index}].name must be nonempty")
        if len(targets) != len(actuators):
            raise ScenarioError(f"phases[{index}] target count differs from actuators")
        for target_index, target in enumerate(targets):
            require_number(target, f"phases[{index}].targets[{target_index}]")
        previous_start = start
    if phases[0]["start_step"] != 0:
        raise ScenarioError("The first phase must start at step zero")

    checkpoints = require_array(scenario.get("checkpoint_steps"), "checkpoint_steps")
    if checkpoints != sorted(set(checkpoints)):
        raise ScenarioError("Checkpoint steps must be unique and increasing")
    if not checkpoints or checkpoints[0] != 0 or checkpoints[-1] != total_steps:
        raise ScenarioError("Checkpoints must span zero through total_steps")
    if any(not isinstance(step, int) or isinstance(step, bool) for step in checkpoints):
        raise ScenarioError("Checkpoint steps must be integers")

    tolerances = require_object(scenario.get("tolerances"), "tolerances")
    maximum_residual = require_number(
        tolerances.get("maximum_equality_residual"),
        "tolerances.maximum_equality_residual",
    )
    if maximum_residual < 0.0:
        raise ScenarioError("maximum_equality_residual must be nonnegative")


def c_string(value: str) -> str:
    """Render an ASCII-safe C string literal."""
    return json.dumps(value, ensure_ascii=True)


def c_number(value: int | float) -> str:
    """Render deterministic C numeric source."""
    if isinstance(value, int):
        return str(value)
    return format(float(value), ".17g")


def render_header(scenario: dict[str, Any], scenario_sha256: str) -> str:
    """Render the canonical generated header."""
    source = scenario["source"]
    actuators = scenario["actuator_names"]
    bodies = scenario["body_names"]
    phases = scenario["phases"]
    checkpoints = scenario["checkpoint_steps"]
    counts = scenario["expected_counts"]
    maximum_residual = scenario["tolerances"]["maximum_equality_residual"]

    lines = [
        "#ifndef REACHY_REFERENCE_SCENARIO_GENERATED_H",
        "#define REACHY_REFERENCE_SCENARIO_GENERATED_H",
        "",
        "#include <stdint.h>",
        "",
        "/* Generated by scripts/generate_reachy_reference_header.py. */",
        f"#define REACHY_REFERENCE_SCENARIO_SCHEMA_VERSION {scenario['schema_version']}U",
        f"#define REACHY_REFERENCE_TOTAL_STEPS UINT64_C({scenario['total_steps']})",
        f"#define REACHY_REFERENCE_ACTUATOR_COUNT {len(actuators)}U",
        f"#define REACHY_REFERENCE_BODY_COUNT {len(bodies)}U",
        f"#define REACHY_REFERENCE_PHASE_COUNT {len(phases)}U",
        f"#define REACHY_REFERENCE_CHECKPOINT_COUNT {len(checkpoints)}U",
        f"#define REACHY_REFERENCE_EXPECTED_NQ {counts['nq']}U",
        f"#define REACHY_REFERENCE_EXPECTED_NV {counts['nv']}U",
        "",
        "typedef struct ReachyReferencePhase {",
        "    uint64_t start_step;",
        "    const char* name;",
        "    double targets[REACHY_REFERENCE_ACTUATOR_COUNT];",
        "} ReachyReferencePhase;",
        "",
        (
            "static const char REACHY_REFERENCE_SCENARIO_ID[] = "
            f"{c_string(scenario['scenario_id'])};"
        ),
        (f"static const char REACHY_REFERENCE_SCENARIO_SHA256[] = {c_string(scenario_sha256)};"),
        (
            "static const char REACHY_REFERENCE_MODEL_SHA256[] = "
            f"{c_string(source['model_sha256'])};"
        ),
        (
            "static const char REACHY_REFERENCE_MUJOCO_VERSION[] = "
            f"{c_string(source['mujoco_version'])};"
        ),
        (
            "static const double REACHY_REFERENCE_TIMESTEP_SECONDS = "
            f"{c_number(scenario['timestep_seconds'])};"
        ),
        (
            "static const double REACHY_REFERENCE_MAXIMUM_EQUALITY_RESIDUAL = "
            f"{c_number(maximum_residual)};"
        ),
        "",
        (
            "static const char* const REACHY_REFERENCE_ACTUATOR_NAMES"
            "[REACHY_REFERENCE_ACTUATOR_COUNT] = {"
        ),
    ]
    lines.extend(f"    {c_string(name)}," for name in actuators)
    lines.extend(
        [
            "};",
            "",
            (
                "static const char* const REACHY_REFERENCE_BODY_NAMES"
                "[REACHY_REFERENCE_BODY_COUNT] = {"
            ),
        ]
    )
    lines.extend(f"    {c_string(name)}," for name in bodies)
    lines.extend(
        [
            "};",
            "",
            (
                "static const uint64_t REACHY_REFERENCE_CHECKPOINT_STEPS"
                "[REACHY_REFERENCE_CHECKPOINT_COUNT] = {"
            ),
        ]
    )
    lines.extend(f"    UINT64_C({step})," for step in checkpoints)
    lines.extend(
        [
            "};",
            "",
            (
                "static const ReachyReferencePhase REACHY_REFERENCE_PHASES"
                "[REACHY_REFERENCE_PHASE_COUNT] = {"
            ),
        ]
    )
    for phase in phases:
        target_text = ", ".join(c_number(value) for value in phase["targets_radians"])
        lines.append(
            "    {UINT64_C("
            + str(phase["start_step"])
            + "), "
            + c_string(phase["name"])
            + ", {"
            + target_text
            + "}},"
        )
    lines.extend(["};", ""])
    for constant_name, count_key in COUNT_KEYS:
        lines.append(
            "static const uint32_t "
            f"REACHY_REFERENCE_EXPECTED_{constant_name}_COUNT = "
            f"{counts[count_key]}U;"
        )
    lines.extend(["", "#endif", ""])
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    """Generate or verify the committed C header."""
    args = parse_args()
    try:
        scenario, scenario_sha256 = read_scenario(args.scenario.resolve())
        rendered = render_header(scenario, scenario_sha256)
        output = args.output.resolve()
        if args.check:
            try:
                existing = output.read_text(encoding="utf-8")
            except OSError as exc:
                raise ScenarioError(f"Cannot read generated header {output}: {exc}") from exc
            if existing != rendered:
                raise ScenarioError(
                    "Generated reference header is stale; rerun "
                    "scripts/generate_reachy_reference_header.py"
                )
        else:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(rendered, encoding="utf-8", newline="\n")
    except ScenarioError as exc:
        print(f"Reference scenario generation failed: {exc}", file=sys.stderr)
        return 1
    print(
        "Reference scenario header is current: "
        f"scenario={scenario['scenario_id']} sha256={scenario_sha256}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
