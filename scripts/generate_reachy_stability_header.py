#!/usr/bin/env python3
"""Generate the native RMA-060 stability header from its JSON profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any


class StabilityProfileError(RuntimeError):
    """Raised when the stability profile is malformed or the header is stale."""


def require_object(value: object, label: str) -> dict[str, Any]:
    """Return one required JSON object."""
    if not isinstance(value, dict):
        raise StabilityProfileError(f"{label} must be an object")
    return value


def require_array(value: object, label: str) -> list[Any]:
    """Return one required JSON array."""
    if not isinstance(value, list):
        raise StabilityProfileError(f"{label} must be an array")
    return value


def require_names(value: object, label: str) -> list[str]:
    """Return one nonempty array of unique names."""
    values = require_array(value, label)
    if not values or not all(isinstance(item, str) and item for item in values):
        raise StabilityProfileError(f"{label} must contain nonempty strings")
    names = list(values)
    if len(names) != len(set(names)):
        raise StabilityProfileError(f"{label} contains duplicate names")
    return names


def require_number(value: object, label: str) -> float:
    """Return one finite JSON number."""
    if not isinstance(value, int | float) or isinstance(value, bool):
        raise StabilityProfileError(f"{label} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise StabilityProfileError(f"{label} must be finite")
    return number


def require_positive_integer(value: object, label: str) -> int:
    """Return one positive integer."""
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise StabilityProfileError(f"{label} must be a positive integer")
    return value


def read_profile(path: Path) -> tuple[dict[str, Any], str]:
    """Read, validate, and hash the profile's exact bytes."""
    try:
        raw = path.read_bytes()
        profile = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise StabilityProfileError(f"Cannot read stability profile {path}: {exc}") from exc
    profile = require_object(profile, "profile root")
    validate_profile(profile)
    return profile, hashlib.sha256(raw).hexdigest()


def validate_profile(profile: dict[str, Any]) -> None:
    """Validate every field consumed by the native Android runner."""
    if profile.get("schema_version") != 1:
        raise StabilityProfileError("Unsupported stability profile schema")
    if profile.get("profile_id") != "upstream_baseline":
        raise StabilityProfileError("profile_id must be upstream_baseline")

    source = require_object(profile.get("source"), "source")
    for field in ("model_sha256", "mujoco_version", "upstream_commit"):
        value = source.get(field)
        if not isinstance(value, str) or not value:
            raise StabilityProfileError(f"source.{field} must be a nonempty string")
    if len(source["model_sha256"]) != 64:
        raise StabilityProfileError("source.model_sha256 must be a SHA-256 string")

    timestep = require_number(profile.get("timestep_seconds"), "timestep_seconds")
    if timestep != 0.002:
        raise StabilityProfileError("upstream_baseline must run at exactly 0.002 seconds")

    actuators = require_names(profile.get("actuator_names"), "actuator_names")
    if len(actuators) != 9:
        raise StabilityProfileError("upstream_baseline must contain exactly nine actuators")

    counts = require_object(profile.get("expected_counts"), "expected_counts")
    required_count_keys = {
        "actuators",
        "bodies_including_world",
        "equalities",
        "joints",
        "nq",
        "nv",
    }
    if set(counts) != required_count_keys:
        raise StabilityProfileError("expected_counts has an unexpected key set")
    if any(
        not isinstance(value, int) or isinstance(value, bool) or value <= 0
        for value in counts.values()
    ):
        raise StabilityProfileError("expected_counts values must be positive integers")
    if counts["actuators"] != len(actuators):
        raise StabilityProfileError("expected actuator count differs from actuator_names")

    monitoring = require_object(profile.get("monitoring"), "monitoring")
    monitoring_fields = (
        "maximum_equality_residual",
        "maximum_scalar_joint_limit_violation_radians",
        "maximum_contact_penetration_metres",
        "maximum_absolute_total_energy_joules",
    )
    for field in monitoring_fields:
        if require_number(monitoring.get(field), f"monitoring.{field}") < 0.0:
            raise StabilityProfileError(f"monitoring.{field} must be nonnegative")
    if monitoring.get("warnings_must_be_zero") is not True:
        raise StabilityProfileError("monitoring.warnings_must_be_zero must be true")
    if monitoring.get("finite_state_required") is not True:
        raise StabilityProfileError("monitoring.finite_state_required must be true")

    defaults = require_object(profile.get("phase_defaults"), "phase_defaults")
    transition_steps = require_positive_integer(
        defaults.get("transition_steps"), "phase_defaults.transition_steps"
    )
    hold_steps = require_positive_integer(defaults.get("hold_steps"), "phase_defaults.hold_steps")
    if defaults.get("interpolation") != "minimum_jerk":
        raise StabilityProfileError("phase interpolation must be minimum_jerk")

    gate = require_object(profile.get("long_duration_gate"), "long_duration_gate")
    required_cycles = require_positive_integer(
        gate.get("required_android_cycles"),
        "long_duration_gate.required_android_cycles",
    )
    required_seconds = require_number(
        gate.get("required_simulated_seconds"),
        "long_duration_gate.required_simulated_seconds",
    )
    minimum_realtime_factor = require_number(
        gate.get("minimum_solver_realtime_factor"),
        "long_duration_gate.minimum_solver_realtime_factor",
    )
    if minimum_realtime_factor < 1.0:
        raise StabilityProfileError(
            "long_duration_gate.minimum_solver_realtime_factor must be at least 1"
        )
    if gate.get("representative_hardware_required") is not True:
        raise StabilityProfileError(
            "long_duration_gate.representative_hardware_required must be true"
        )
    if gate.get("timestep_deviation_decision") != "not_required":
        raise StabilityProfileError(
            "500 Hz baseline requires timestep_deviation_decision=not_required"
        )

    phases = require_array(profile.get("phases"), "phases")
    if not phases:
        raise StabilityProfileError("phases must not be empty")
    phase_names: set[str] = set()
    categories: set[str] = set()
    for index, raw_phase in enumerate(phases):
        phase = require_object(raw_phase, f"phases[{index}]")
        name = phase.get("name")
        category = phase.get("category")
        if not isinstance(name, str) or not name or name in phase_names:
            raise StabilityProfileError(f"phases[{index}].name is missing or duplicate")
        if not isinstance(category, str) or not category:
            raise StabilityProfileError(f"phases[{index}].category must be nonempty")
        phase_names.add(name)
        categories.add(category)
        targets = require_array(phase.get("targets_radians"), f"phases[{index}].targets_radians")
        if len(targets) != len(actuators):
            raise StabilityProfileError(
                f"phases[{index}].targets_radians must contain {len(actuators)} values"
            )
        for target_index, target in enumerate(targets):
            require_number(target, f"phases[{index}].targets_radians[{target_index}]")
        allowed = require_array(
            phase.get("allowed_out_of_range_actuators"),
            f"phases[{index}].allowed_out_of_range_actuators",
        )
        if not all(isinstance(item, str) and item in actuators for item in allowed):
            raise StabilityProfileError(
                f"phases[{index}].allowed_out_of_range_actuators is invalid"
            )
        if len(allowed) != len(set(allowed)):
            raise StabilityProfileError(
                f"phases[{index}].allowed_out_of_range_actuators contains duplicates"
            )

    required_categories = {
        "neutral",
        "sleep",
        "body_yaw_limit",
        "head_actuator_limit",
        "antenna_extreme",
    }
    if not required_categories.issubset(categories):
        missing = sorted(required_categories - categories)
        raise StabilityProfileError(f"Stability profile is missing categories: {missing}")
    if phases[0]["category"] != "neutral" or phases[-1]["category"] != "neutral":
        raise StabilityProfileError("Stability profile must start and end in neutral")

    steps_per_cycle = len(phases) * (transition_steps + hold_steps)
    expected_seconds = required_cycles * steps_per_cycle * timestep
    if not math.isclose(expected_seconds, required_seconds, rel_tol=0.0, abs_tol=1e-9):
        raise StabilityProfileError(
            "long_duration_gate.required_simulated_seconds does not match the schedule"
        )


def c_string(value: str) -> str:
    """Render one ASCII-safe C string literal."""
    return json.dumps(value, ensure_ascii=True)


def c_number(value: int | float) -> str:
    """Render one deterministic C number."""
    if isinstance(value, int):
        return str(value)
    return format(float(value), ".17g")


def render_header(profile: dict[str, Any], profile_sha256: str) -> str:
    """Render the canonical generated C header."""
    source = profile["source"]
    counts = profile["expected_counts"]
    monitoring = profile["monitoring"]
    defaults = profile["phase_defaults"]
    gate = profile["long_duration_gate"]
    actuators = profile["actuator_names"]
    phases = profile["phases"]
    steps_per_phase = defaults["transition_steps"] + defaults["hold_steps"]
    steps_per_cycle = len(phases) * steps_per_phase

    lines = [
        "#ifndef REACHY_STABILITY_PROFILE_GENERATED_H",
        "#define REACHY_STABILITY_PROFILE_GENERATED_H",
        "",
        "#include <stdint.h>",
        "",
        "/* Generated by scripts/generate_reachy_stability_header.py. */",
        f"#define REACHY_STABILITY_PROFILE_SCHEMA_VERSION {profile['schema_version']}U",
        f"#define REACHY_STABILITY_ACTUATOR_COUNT {len(actuators)}U",
        f"#define REACHY_STABILITY_PHASE_COUNT {len(phases)}U",
        f"#define REACHY_STABILITY_TRANSITION_STEPS {defaults['transition_steps']}U",
        f"#define REACHY_STABILITY_HOLD_STEPS {defaults['hold_steps']}U",
        f"#define REACHY_STABILITY_STEPS_PER_PHASE UINT64_C({steps_per_phase})",
        f"#define REACHY_STABILITY_STEPS_PER_CYCLE UINT64_C({steps_per_cycle})",
        f"#define REACHY_STABILITY_REQUIRED_ANDROID_CYCLES {gate['required_android_cycles']}U",
        "",
        "typedef struct ReachyStabilityPhase {",
        "    const char* name;",
        "    const char* category;",
        "    double targets[REACHY_STABILITY_ACTUATOR_COUNT];",
        "    uint32_t allowed_out_of_range_mask;",
        "} ReachyStabilityPhase;",
        "",
        (f"static const char REACHY_STABILITY_PROFILE_ID[] = {c_string(profile['profile_id'])};"),
        (f"static const char REACHY_STABILITY_PROFILE_SHA256[] = {c_string(profile_sha256)};"),
        (
            "static const char REACHY_STABILITY_MODEL_SHA256[] = "
            f"{c_string(source['model_sha256'])};"
        ),
        (
            "static const char REACHY_STABILITY_MUJOCO_VERSION[] = "
            f"{c_string(source['mujoco_version'])};"
        ),
        (
            "static const char REACHY_STABILITY_UPSTREAM_COMMIT[] = "
            f"{c_string(source['upstream_commit'])};"
        ),
        (
            "static const double REACHY_STABILITY_TIMESTEP_SECONDS = "
            f"{c_number(profile['timestep_seconds'])};"
        ),
        (
            "static const double REACHY_STABILITY_REQUIRED_SIMULATED_SECONDS = "
            f"{c_number(gate['required_simulated_seconds'])};"
        ),
        (
            "static const double REACHY_STABILITY_MINIMUM_SOLVER_REALTIME_FACTOR = "
            f"{c_number(gate['minimum_solver_realtime_factor'])};"
        ),
        (
            "static const double REACHY_STABILITY_MAXIMUM_EQUALITY_RESIDUAL = "
            f"{c_number(monitoring['maximum_equality_residual'])};"
        ),
        (
            "static const double REACHY_STABILITY_MAXIMUM_JOINT_LIMIT_VIOLATION = "
            f"{c_number(monitoring['maximum_scalar_joint_limit_violation_radians'])};"
        ),
        (
            "static const double REACHY_STABILITY_MAXIMUM_CONTACT_PENETRATION = "
            f"{c_number(monitoring['maximum_contact_penetration_metres'])};"
        ),
        (
            "static const double REACHY_STABILITY_MAXIMUM_ABSOLUTE_TOTAL_ENERGY = "
            f"{c_number(monitoring['maximum_absolute_total_energy_joules'])};"
        ),
        "",
        (
            "static const char* const REACHY_STABILITY_ACTUATOR_NAMES"
            "[REACHY_STABILITY_ACTUATOR_COUNT] = {"
        ),
    ]
    lines.extend(f"    {c_string(name)}," for name in actuators)
    lines.extend(
        [
            "};",
            "",
            (
                "static const ReachyStabilityPhase REACHY_STABILITY_PHASES"
                "[REACHY_STABILITY_PHASE_COUNT] = {"
            ),
        ]
    )
    actuator_index = {name: index for index, name in enumerate(actuators)}
    for phase in phases:
        targets = ", ".join(c_number(value) for value in phase["targets_radians"])
        mask = 0
        for name in phase["allowed_out_of_range_actuators"]:
            mask |= 1 << actuator_index[name]
        lines.append(
            "    {"
            + c_string(phase["name"])
            + ", "
            + c_string(phase["category"])
            + ", {"
            + targets
            + "}, UINT32_C("
            + str(mask)
            + ")},"
        )
    lines.extend(
        [
            "};",
            "",
            "static const uint32_t REACHY_STABILITY_EXPECTED_BODY_COUNT = "
            f"{counts['bodies_including_world']}U;",
            f"static const uint32_t REACHY_STABILITY_EXPECTED_JOINT_COUNT = {counts['joints']}U;",
            "static const uint32_t REACHY_STABILITY_EXPECTED_ACTUATOR_COUNT = "
            f"{counts['actuators']}U;",
            "static const uint32_t REACHY_STABILITY_EXPECTED_EQUALITY_COUNT = "
            f"{counts['equalities']}U;",
            f"static const uint32_t REACHY_STABILITY_EXPECTED_NQ = {counts['nq']}U;",
            f"static const uint32_t REACHY_STABILITY_EXPECTED_NV = {counts['nv']}U;",
            "",
            "#endif",
            "",
        ]
    )
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    """Generate or verify the committed native profile header."""
    args = parse_args()
    try:
        profile, profile_sha256 = read_profile(args.profile.resolve())
        rendered = render_header(profile, profile_sha256)
        output = args.output.resolve()
        if args.check:
            try:
                existing = output.read_text(encoding="utf-8")
            except OSError as exc:
                raise StabilityProfileError(
                    f"Cannot read generated stability header {output}: {exc}"
                ) from exc
            if existing != rendered:
                raise StabilityProfileError(
                    "Generated stability header is stale; rerun "
                    "scripts/generate_reachy_stability_header.py"
                )
        else:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(rendered, encoding="utf-8", newline="\n")
    except StabilityProfileError as exc:
        print(f"Stability profile generation failed: {exc}", file=sys.stderr)
        return 1
    print(
        "Stability profile header is current: "
        f"profile={profile['profile_id']} sha256={profile_sha256}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
