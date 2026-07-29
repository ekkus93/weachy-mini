#!/usr/bin/env python3
"""Generate the native RMA-042 scenario header from its JSON source."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any


class ScenarioError(RuntimeError):
    """Raised when the reference scenario is malformed."""


def read_scenario(path: Path) -> tuple[dict[str, Any], str]:
    """Read, validate, and hash a reference scenario."""
    try:
        raw = path.read_bytes()
        value = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise ScenarioError(f"Cannot read scenario {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ScenarioError("Scenario root must be an object")
    validate_scenario(value)
    return value, hashlib.sha256(raw).hexdigest()


def require_list(value: object, label: str) -> list[Any]:
    """Return a required JSON array."""
    if not isinstance(value, list):
        raise ScenarioError(f"{label} must be an array")
    return value


def require_string(value: object, label: str) -> str:
    """Return a required nonempty string."""
    if not isinstance(value, str) or not value:
        raise ScenarioError(f"{label} must be a nonempty string")
    return value


def validate_names(value: object, label: str) -> list[str]:
    """Validate a nonempty unique name array."""
    names = require_list(value, label)
    if not names or not all(isinstance(name, str) and name for name in names):
        raise ScenarioError(f"{label} must contain nonempty strings")
    typed_names = list(names)
    if len(set(typed_names)) != len(typed_names):
        raise ScenarioError(f"{label} contains duplicate names")
    return typed_names


def validate_numbers(
    value: object,
    label: str,
    expected_length: int,
) -> list[float]:
    """Validate a fixed-length numeric array."""
    values = require_list(value, label)
    if len(values) != expected_length:
        raise ScenarioError(
            f"{label} must contain {expected_length} values, found {len(values)}"
        )
    result: list[float] = []
    for index, item in enumerate(values):
        if not isinstance(item, int | float) or isinstance(item, bool):
            raise ScenarioError(f"{label}[{index}] must be numeric")
        result.append(float(item))
    return result


def validate_scenario(scenario: dict[str, Any]) -> None:
    """Validate the complete scenario contract before code generation."""
    if scenario.get("schema_version") != 1:
        raise ScenarioError("Unsupported scenario schema version")
    require_string(scenario.get("scenario_id"), "scenario_id")
    source = scenario.get("source")
    if not isinstance(source, dict):
        raise ScenarioError("source must be an object")
    source_keys = (
        "repository",
        "commit",
        "model_path",
        "model_sha256",
        "mujoco_version",
    )
    for key in source_keys:
        require_string(source.get(key), f"source.{key}")
    if len(source["model_sha256"]) != 64:
        raise ScenarioError("source.model_sha256 must contain 64 hexadecimal characters")

    timestep = scenario.get("timestep_seconds")
    if (
        not isinstance(timestep, int | float)
        or isinstance(timestep, bool)
        or timestep <= 0
    ):
        raise ScenarioError("timestep_seconds must be positive")
    total_steps = scenario.get("total_steps")
    if (
        not isinstance(total_steps, int)
        or isinstance(total_steps, bool)
        or total_steps <= 0
    ):
        raise ScenarioError("total_steps must be a positive integer")

    actuator_names = validate_names(scenario.get("actuator_names"), "actuator_names")
    validate_names(scenario.get("body_names"), "body_names")
    validate_phases(scenario, actuator_names, total_steps)
    validate_checkpoints(scenario, total_steps)
    validate_counts(scenario, len(actuator_names))
    validate_tolerances(scenario)


def validate_phases(
    scenario: dict[str, Any],
    actuator_names: list[str],
    total_steps: int,
) -> None:
    """Validate ordered command phases."""
    raw_phases = require_list(scenario.get("phases"), "phases")
    if not raw_phases:
        raise ScenarioError("phases must not be empty")
    previous_start = -1
    phase_names: set[str] = set()
    for index, raw_phase in enumerate(raw_phases):
        if not isinstance(raw_phase, dict):
            raise ScenarioError(f"phases[{index}] must be an object")
        phase_name = require_string(raw_phase.get("name"), f"phases[{index}].name")
        if phase_name in phase_names:
            raise ScenarioError(f"Duplicate phase name: {phase_name}")
        phase_names.add(phase_name)
        start = raw_phase.get("start_step")
        if not isinstance(start, int) or isinstance(start, bool):
            raise ScenarioError(f"phases[{index}].start_step must be an integer")
        if start <= previous_start or start < 0 or start >= total_steps:
            raise ScenarioError("Phase start steps must be increasing and within total_steps")
        previous_start = start
        validate_numbers(
            raw_phase.get("targets_radians"),
            f"phases[{index}].targets_radians",
            len(actuator_names),
        )
    if raw_phases[0]["start_step"] != 0:
        raise ScenarioError("The first phase must start at step zero")


def validate_checkpoints(scenario: dict[str, Any], total_steps: int) -> None:
    """Validate the complete ordered checkpoint set."""
    checkpoints = require_list(scenario.get("checkpoint_steps"), "checkpoint_steps")
    if not checkpoints or checkpoints[0] != 0 or checkpoints[-1] != total_steps:
        raise ScenarioError("Checkpoints must start at zero and end at total_steps")
    if any(not isinstance(step, int) or isinstance(step, bool) for step in checkpoints):
        raise ScenarioError("Checkpoint steps must be integers")
    if checkpoints != sorted(set(checkpoints)):
        raise ScenarioError("Checkpoint steps must be unique and increasing")
    if any(step < 0 or step > total_steps for step in checkpoints):
        raise ScenarioError("Checkpoint step is outside the scenario")


def validate_counts(scenario: dict[str, Any], actuator_count: int) -> None:
    """Validate pinned compiled-model dimensions."""
    counts = scenario.get("expected_counts")
    if not isinstance(counts, dict):
        raise ScenarioError("expected_counts must be an object")
    required_counts = {
        "bodies_including_world",
        "joints",
        "actuators",
        "equalities",
        "sites",
        "cameras",
        "nq",
        "nv",
    }
    if set(counts) != required_counts:
        raise ScenarioError("expected_counts has an unexpected key set")
    if any(
        not isinstance(value, int) or isinstance(value, bool) or value < 0
        for value in counts.values()
    ):
        raise ScenarioError("expected_counts values must be nonnegative integers")
    if counts["actuators"] != actuator_count:
        raise ScenarioError("Actuator-name count does not match expected_counts")


def validate_tolerances(scenario: dict[str, Any]) -> None:
    """Validate cross-platform comparison tolerances."""
    tolerances = scenario.get("tolerances")
    if not isinstance(tolerances, dict):
        raise ScenarioError("tolerances must be an object")
    required_tolerances = {
        "simulation_time_seconds",
        "qpos_absolute",
        "qvel_absolute",
        "body_position_metres_absolute",
        "body_quaternion_component_absolute",
        "equality_residual_absolute",
        "maximum_equality_residual",
    }
    if set(tolerances) != required_tolerances:
        raise ScenarioError("tolerances has an unexpected key set")
    if any(
        not isinstance(value, int | float) or isinstance(value, bool) or value < 0
        for value in tolerances.values()
    ):
        raise ScenarioError("Tolerance values must be nonnegative numbers")


def c_string(value: str) -> str:
    """Encode a UTF-8 JSON string as a C string literal."""
    return json.dumps(value, ensure_ascii=True)


def c_number(value: int | float) -> str:
    """Emit stable C numeric source."""
    if isinstance(value, int):
        return str(value)
    return format(float(value), ".17g")


def c_declaration(type_name: str, name: str, value: str) -> str:
    """Render one generated C constant declaration."""
    return f"static const {type_name} {name} = {value};"


def render_header(scenario: dict[str, Any], scenario_sha256: str) -> str:
    """Render the validated native scenario header."""
    source = scenario["source"]
    actuator_names = scenario["actuator_names"]
    body_names = scenario["body_names"]
    phases = scenario["phases"]
    checkpoints = scenario["checkpoint_steps"]
    counts = scenario["expected_counts"]
    tolerances = scenario["tolerances"]

    lines = [
        "#ifndef REACHY_REFERENCE_SCENARIO_GENERATED_H",
        "#define REACHY_REFERENCE_SCENARIO_GENERATED_H",
        "",
        "#include <stdint.h>",
        "",
        "/* Generated by scripts/generate_reachy_reference_header.py. */",
        f"#define REACHY_REFERENCE_SCENARIO_SCHEMA_VERSION {scenario['schema_version']}U",
        f"#define REACHY_REFERENCE_TOTAL_STEPS UINT64_C({scenario['total_steps']})",
        f"#define REACHY_REFERENCE_ACTUATOR_COUNT {len(actuator_names)}U",
        f"#define REACHY_REFERENCE_BODY_COUNT {len(body_names)}U",
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
        c_declaration(
            "char REACHY_REFERENCE_SCENARIO_ID[]",
            "",
            c_string(scenario["scenario_id"]),
        ).replace("[]  =", "[] ="),
        c_declaration(
            "char REACHY_REFERENCE_SCENARIO_SHA256[]",
            "",
            c_string(scenario_sha256),
        ).replace("[]  =", "[] ="),
        c_declaration(
            "char REACHY_REFERENCE_MODEL_SHA256[]",
            "",
            c_string(source["model_sha256"]),
        ).replace("[]  =", "[] ="),
        c_declaration(
            "char REACHY_REFERENCE_MUJOCO_VERSION[]",
            "",
            c_string(source["mujoco_version"]),
        ).replace("[]  =", "[] ="),
        c_declaration(
            "double",
            "REACHY_REFERENCE_TIMESTEP_SECONDS",
            c_number(scenario["timestep_seconds"]),
        ),
        c_declaration(
            "double",
            "REACHY_REFERENCE_MAXIMUM_EQUALITY_RESIDUAL",
            c_number(tolerances["maximum_equality_residual"]),
        ),
        "",
        (
            "static const char* const REACHY_REFERENCE_ACTUATOR_NAMES"
            "[REACHY_REFERENCE_ACTUATOR_COUNT] = {"
        ),
    ]
    lines.extend(f"    {c_string(name)}," for name in actuator_names)
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
    lines.extend(f"    {c_string(name)}," for name in body_names)
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
            "    {UINT64_C(" + str(phase["start_step"]) + "), "
            + c_string(phase["name"])
            + ", {"
            + target_text
            + "}},"
        )
    count_declarations = (
        ("BODY", "bodies_including_world"),
        ("JOINT", "joints"),
        ("ACTUATOR", "actuators"),
        ("EQUALITY", "equalities"),
        ("SITE", "sites"),
        ("CAMERA", "cameras"),
    )
    lines.extend(["};", ""])
    for constant_name, count_key in count_declarations:
        lines.append(
            c_declaration(
                "uint32_t",
                f"REACHY_REFERENCE_EXPECTED_{constant_name}_COUNT",
                f"{counts[count_key]}U",
            )
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
