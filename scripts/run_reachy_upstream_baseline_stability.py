#!/usr/bin/env python3
"""Run the pinned Reachy upstream-baseline MuJoCo stability suite."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import statistics
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


class StabilityError(RuntimeError):
    """Raised when the stability profile or simulation violates its contract."""


@dataclass(frozen=True)
class StabilityConfig:
    """Validated subset of the machine-readable stability profile."""

    raw: dict[str, Any]
    actuator_names: tuple[str, ...]
    timestep_seconds: float
    transition_steps: int
    hold_steps: int


@dataclass
class Metrics:
    """Mutable per-phase or aggregate stability metrics."""

    completed_steps: int = 0
    maximum_equality_residual: float = 0.0
    maximum_joint_limit_violation: float = 0.0
    maximum_contact_penetration: float = 0.0
    maximum_contact_count: int = 0
    minimum_total_energy: float = math.inf
    maximum_total_energy: float = -math.inf
    maximum_absolute_total_energy: float = 0.0
    warning_count: int = 0

    def update_from(self, other: Metrics) -> None:
        """Merge one phase into the aggregate metrics."""
        self.completed_steps += other.completed_steps
        self.maximum_equality_residual = max(
            self.maximum_equality_residual,
            other.maximum_equality_residual,
        )
        self.maximum_joint_limit_violation = max(
            self.maximum_joint_limit_violation,
            other.maximum_joint_limit_violation,
        )
        self.maximum_contact_penetration = max(
            self.maximum_contact_penetration,
            other.maximum_contact_penetration,
        )
        self.maximum_contact_count = max(
            self.maximum_contact_count,
            other.maximum_contact_count,
        )
        self.minimum_total_energy = min(
            self.minimum_total_energy,
            other.minimum_total_energy,
        )
        self.maximum_total_energy = max(
            self.maximum_total_energy,
            other.maximum_total_energy,
        )
        self.maximum_absolute_total_energy = max(
            self.maximum_absolute_total_energy,
            other.maximum_absolute_total_energy,
        )
        self.warning_count += other.warning_count


def read_json(path: Path) -> dict[str, Any]:
    """Read one JSON object."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise StabilityError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise StabilityError(f"JSON root must be an object: {path}")
    return value


def require_number(value: object, label: str) -> float:
    """Return one finite numeric value."""
    if not isinstance(value, int | float) or isinstance(value, bool):
        raise StabilityError(f"{label} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise StabilityError(f"{label} must be finite")
    return number


def require_positive_integer(value: object, label: str) -> int:
    """Return one positive integer."""
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise StabilityError(f"{label} must be a positive integer")
    return value


def require_names(value: object, label: str) -> tuple[str, ...]:
    """Return a nonempty array of unique names."""
    if not isinstance(value, list) or not value:
        raise StabilityError(f"{label} must be a nonempty array")
    if not all(isinstance(item, str) and item for item in value):
        raise StabilityError(f"{label} must contain nonempty strings")
    names = tuple(value)
    if len(names) != len(set(names)):
        raise StabilityError(f"{label} contains duplicate names")
    return names


def validate_config(raw: dict[str, Any]) -> StabilityConfig:
    """Validate the source-independent stability profile contract."""
    if raw.get("schema_version") != 1:
        raise StabilityError("Unsupported stability profile schema")
    if raw.get("profile_id") != "upstream_baseline":
        raise StabilityError("Stability profile_id must be upstream_baseline")

    source = raw.get("source")
    if not isinstance(source, dict):
        raise StabilityError("source must be an object")
    for field in (
        "model_sha256",
        "mujoco_version",
        "upstream_commit",
        "upstream_sleep_source",
    ):
        value = source.get(field)
        if not isinstance(value, str) or not value:
            raise StabilityError(f"source.{field} must be a nonempty string")
    if len(source["model_sha256"]) != 64:
        raise StabilityError("source.model_sha256 must be a SHA-256 string")

    timestep = require_number(raw.get("timestep_seconds"), "timestep_seconds")
    if timestep != 0.002:
        raise StabilityError("upstream_baseline must run at exactly 0.002 seconds")

    actuator_names = require_names(raw.get("actuator_names"), "actuator_names")
    if len(actuator_names) != 9:
        raise StabilityError("upstream_baseline must contain exactly nine actuators")

    expected_counts = raw.get("expected_counts")
    if not isinstance(expected_counts, dict):
        raise StabilityError("expected_counts must be an object")
    required_count_keys = {
        "actuators",
        "bodies_including_world",
        "equalities",
        "joints",
        "nq",
        "nv",
    }
    if set(expected_counts) != required_count_keys:
        raise StabilityError("expected_counts has an unexpected key set")
    if any(
        not isinstance(value, int) or isinstance(value, bool) or value <= 0
        for value in expected_counts.values()
    ):
        raise StabilityError("expected_counts values must be positive integers")
    if expected_counts["actuators"] != len(actuator_names):
        raise StabilityError("Actuator count differs from actuator_names")

    monitoring = raw.get("monitoring")
    if not isinstance(monitoring, dict):
        raise StabilityError("monitoring must be an object")
    for field in (
        "maximum_equality_residual",
        "maximum_scalar_joint_limit_violation_radians",
        "maximum_contact_penetration_metres",
        "maximum_absolute_total_energy_joules",
    ):
        if require_number(monitoring.get(field), f"monitoring.{field}") < 0.0:
            raise StabilityError(f"monitoring.{field} must be nonnegative")
    for field in ("warnings_must_be_zero", "finite_state_required"):
        if monitoring.get(field) is not True:
            raise StabilityError(f"monitoring.{field} must be true")

    defaults = raw.get("phase_defaults")
    if not isinstance(defaults, dict):
        raise StabilityError("phase_defaults must be an object")
    transition_steps = require_positive_integer(
        defaults.get("transition_steps"),
        "phase_defaults.transition_steps",
    )
    hold_steps = require_positive_integer(
        defaults.get("hold_steps"),
        "phase_defaults.hold_steps",
    )
    if defaults.get("interpolation") != "minimum_jerk":
        raise StabilityError("phase interpolation must be minimum_jerk")

    phases = raw.get("phases")
    if not isinstance(phases, list) or not phases:
        raise StabilityError("phases must be a nonempty array")
    phase_names: set[str] = set()
    categories: set[str] = set()
    for index, phase in enumerate(phases):
        if not isinstance(phase, dict):
            raise StabilityError(f"phases[{index}] must be an object")
        name = phase.get("name")
        category = phase.get("category")
        if not isinstance(name, str) or not name or name in phase_names:
            raise StabilityError(f"phases[{index}].name is missing or duplicate")
        if not isinstance(category, str) or not category:
            raise StabilityError(f"phases[{index}].category must be nonempty")
        phase_names.add(name)
        categories.add(category)
        targets = phase.get("targets_radians")
        if not isinstance(targets, list) or len(targets) != len(actuator_names):
            raise StabilityError(
                f"phases[{index}].targets_radians must contain "
                f"{len(actuator_names)} values"
            )
        for target_index, target in enumerate(targets):
            require_number(target, f"phases[{index}].targets_radians[{target_index}]")
        allowed = phase.get("allowed_out_of_range_actuators")
        if not isinstance(allowed, list) or not all(
            isinstance(item, str) and item in actuator_names for item in allowed
        ):
            raise StabilityError(
                f"phases[{index}].allowed_out_of_range_actuators is invalid"
            )
        if len(allowed) != len(set(allowed)):
            raise StabilityError(
                f"phases[{index}].allowed_out_of_range_actuators has duplicates"
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
        raise StabilityError(f"Stability profile is missing categories: {missing}")
    if phases[0].get("category") != "neutral" or phases[-1].get("category") != "neutral":
        raise StabilityError("Stability profile must start and end in neutral")

    return StabilityConfig(
        raw=raw,
        actuator_names=actuator_names,
        timestep_seconds=timestep,
        transition_steps=transition_steps,
        hold_steps=hold_steps,
    )


def minimum_jerk(progress: float) -> float:
    """Return a clamped quintic minimum-jerk blend."""
    if not math.isfinite(progress):
        raise StabilityError("minimum-jerk progress must be finite")
    value = min(max(progress, 0.0), 1.0)
    return value * value * value * (10.0 + value * (-15.0 + 6.0 * value))


def percentile(sorted_values: list[float], fraction: float) -> float:
    """Interpolate one percentile from sorted finite values."""
    if not sorted_values:
        return 0.0
    scaled = fraction * (len(sorted_values) - 1)
    lower = math.floor(scaled)
    upper = math.ceil(scaled)
    if lower == upper:
        return sorted_values[lower]
    weight = scaled - lower
    return sorted_values[lower] + (
        sorted_values[upper] - sorted_values[lower]
    ) * weight


def file_sha256(path: Path) -> str:
    """Return one file's SHA-256 digest."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def total_warning_count(data: Any) -> int:
    """Return the sum of MuJoCo warning counters."""
    return sum(max(int(warning.number), 0) for warning in data.warning)


def finite_array(values: Any, label: str) -> None:
    """Reject NaN or infinity without requiring NumPy."""
    for index, value in enumerate(values):
        if not math.isfinite(float(value)):
            raise StabilityError(f"Non-finite {label}[{index}]")


def scalar_joint_limit_violation(model: Any, data: Any, mujoco: Any) -> float:
    """Return the largest hinge/slide qpos excursion beyond a limited range."""
    maximum = 0.0
    hinge = int(mujoco.mjtJoint.mjJNT_HINGE)
    slide = int(mujoco.mjtJoint.mjJNT_SLIDE)
    for joint_id in range(int(model.njnt)):
        if not bool(model.jnt_limited[joint_id]):
            continue
        joint_type = int(model.jnt_type[joint_id])
        if joint_type not in (hinge, slide):
            continue
        qpos_address = int(model.jnt_qposadr[joint_id])
        value = float(data.qpos[qpos_address])
        lower = float(model.jnt_range[joint_id][0])
        upper = float(model.jnt_range[joint_id][1])
        maximum = max(maximum, lower - value, value - upper, 0.0)
    return maximum


def equality_residual(data: Any, mujoco: Any) -> float:
    """Return the largest equality-only position residual."""
    equality_type = int(mujoco.mjtConstraint.mjCNSTR_EQUALITY)
    maximum = 0.0
    for row in range(int(data.nefc)):
        if int(data.efc_type[row]) == equality_type:
            maximum = max(maximum, abs(float(data.efc_pos[row])))
    return maximum


def maximum_contact_penetration(data: Any) -> float:
    """Return the deepest active MuJoCo contact penetration."""
    maximum = 0.0
    for index in range(int(data.ncon)):
        maximum = max(maximum, -float(data.contact[index].dist), 0.0)
    return maximum


def validate_model(
    config: StabilityConfig,
    model_path: Path,
    model: Any,
    mujoco: Any,
) -> list[int]:
    """Require exact source/runtime/topology/actuator identity."""
    source = config.raw["source"]
    actual_hash = file_sha256(model_path)
    if actual_hash != source["model_sha256"]:
        raise StabilityError(
            f"Model SHA-256 mismatch: expected {source['model_sha256']}, found {actual_hash}"
        )
    if mujoco.__version__ != source["mujoco_version"]:
        raise StabilityError(
            f"MuJoCo version mismatch: expected {source['mujoco_version']}, "
            f"found {mujoco.__version__}"
        )
    if abs(float(model.opt.timestep) - config.timestep_seconds) > 1.0e-12:
        raise StabilityError(
            f"Model timestep is {float(model.opt.timestep):.17g}, expected "
            f"{config.timestep_seconds:.17g}"
        )

    expected = config.raw["expected_counts"]
    actual = {
        "actuators": int(model.nu),
        "bodies_including_world": int(model.nbody),
        "equalities": int(model.neq),
        "joints": int(model.njnt),
        "nq": int(model.nq),
        "nv": int(model.nv),
    }
    if actual != expected:
        raise StabilityError(
            f"Compiled model counts differ from stability profile: expected {expected}, "
            f"found {actual}"
        )

    actuator_ids: list[int] = []
    for name in config.actuator_names:
        actuator_id = int(
            mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_ACTUATOR, name)
        )
        if actuator_id < 0:
            raise StabilityError(f"Model is missing actuator {name}")
        actuator_ids.append(actuator_id)
    if actuator_ids != list(range(len(actuator_ids))):
        raise StabilityError(
            f"Actuator order differs from stability profile: {actuator_ids}"
        )
    return actuator_ids


def phase_command_exceedances(
    phase: dict[str, Any],
    config: StabilityConfig,
    model: Any,
    actuator_ids: list[int],
) -> list[dict[str, Any]]:
    """Validate and report requested targets outside finite actuator ranges."""
    actual_names: list[str] = []
    exceedances: list[dict[str, Any]] = []
    targets = phase["targets_radians"]
    for index, actuator_id in enumerate(actuator_ids):
        if not bool(model.actuator_ctrllimited[actuator_id]):
            continue
        target = float(targets[index])
        lower = float(model.actuator_ctrlrange[actuator_id][0])
        upper = float(model.actuator_ctrlrange[actuator_id][1])
        if target < lower or target > upper:
            name = config.actuator_names[index]
            actual_names.append(name)
            exceedances.append(
                {
                    "actuator": name,
                    "requested": target,
                    "lower": lower,
                    "upper": upper,
                    "distance_outside_range": max(lower - target, target - upper),
                }
            )
    expected_names = phase["allowed_out_of_range_actuators"]
    if actual_names != expected_names:
        raise StabilityError(
            f"Phase {phase['name']} command-range exceedances differ: "
            f"expected {expected_names}, found {actual_names}"
        )
    return exceedances


def monitor_step(
    model: Any,
    data: Any,
    mujoco: Any,
    metrics: Metrics,
    monitoring: dict[str, Any],
    phase_name: str,
    global_step: int,
) -> None:
    """Update metrics and fail on the first unexplained stability violation."""
    finite_array(data.qpos, "qpos")
    finite_array(data.qvel, "qvel")
    finite_array(data.qacc, "qacc")
    finite_array(data.ctrl, "ctrl")
    finite_array(data.actuator_force, "actuator_force")
    finite_array(data.efc_pos[: int(data.nefc)], "efc_pos")

    residual = equality_residual(data, mujoco)
    joint_violation = scalar_joint_limit_violation(model, data, mujoco)
    contact_penetration = maximum_contact_penetration(data)
    warning_count = total_warning_count(data)
    mujoco.mj_energyPos(model, data)
    mujoco.mj_energyVel(model, data)
    total_energy = float(data.energy[0] + data.energy[1])
    if not math.isfinite(total_energy):
        message = (
            f"Non-finite total energy in phase {phase_name} "
            f"at step {global_step}"
        )
        raise StabilityError(message)

    metrics.completed_steps += 1
    metrics.maximum_equality_residual = max(
        metrics.maximum_equality_residual,
        residual,
    )
    metrics.maximum_joint_limit_violation = max(
        metrics.maximum_joint_limit_violation,
        joint_violation,
    )
    metrics.maximum_contact_penetration = max(
        metrics.maximum_contact_penetration,
        contact_penetration,
    )
    metrics.maximum_contact_count = max(
        metrics.maximum_contact_count,
        int(data.ncon),
    )
    metrics.minimum_total_energy = min(metrics.minimum_total_energy, total_energy)
    metrics.maximum_total_energy = max(metrics.maximum_total_energy, total_energy)
    metrics.maximum_absolute_total_energy = max(
        metrics.maximum_absolute_total_energy,
        abs(total_energy),
    )
    metrics.warning_count += warning_count

    checks = (
        (
            residual,
            float(monitoring["maximum_equality_residual"]),
            "equality residual",
        ),
        (
            joint_violation,
            float(monitoring["maximum_scalar_joint_limit_violation_radians"]),
            "scalar joint-limit violation",
        ),
        (
            contact_penetration,
            float(monitoring["maximum_contact_penetration_metres"]),
            "contact penetration",
        ),
        (
            abs(total_energy),
            float(monitoring["maximum_absolute_total_energy_joules"]),
            "absolute total energy",
        ),
    )
    for actual, maximum, label in checks:
        if actual > maximum:
            raise StabilityError(
                f"{label} {actual:.17g} exceeds {maximum:.17g} in phase "
                f"{phase_name} at step {global_step}"
            )
    if warning_count != 0:
        raise StabilityError(
            f"MuJoCo warning count increased by {warning_count} in phase "
            f"{phase_name} at step {global_step}"
        )


def metrics_report(metrics: Metrics, timings: list[float]) -> dict[str, Any]:
    """Render deterministic JSON-compatible metrics."""
    sorted_timings = sorted(timings)
    if math.isfinite(metrics.minimum_total_energy):
        minimum_energy = metrics.minimum_total_energy
    else:
        minimum_energy = 0.0
    if math.isfinite(metrics.maximum_total_energy):
        maximum_energy = metrics.maximum_total_energy
    else:
        maximum_energy = 0.0
    maximum_joint_violation = metrics.maximum_joint_limit_violation
    maximum_absolute_energy = metrics.maximum_absolute_total_energy
    median_step = statistics.median(sorted_timings) if sorted_timings else 0.0
    return {
        "completed_steps": metrics.completed_steps,
        "maximum_equality_residual": metrics.maximum_equality_residual,
        "maximum_scalar_joint_limit_violation_radians": maximum_joint_violation,
        "maximum_contact_penetration_metres": metrics.maximum_contact_penetration,
        "maximum_contact_count": metrics.maximum_contact_count,
        "minimum_total_energy_joules": minimum_energy,
        "maximum_total_energy_joules": maximum_energy,
        "maximum_absolute_total_energy_joules": maximum_absolute_energy,
        "warning_count": metrics.warning_count,
        "median_step_microseconds": median_step,
        "p95_step_microseconds": percentile(sorted_timings, 0.95),
        "maximum_step_microseconds": max(sorted_timings, default=0.0),
    }


def run_suite(
    config: StabilityConfig,
    model_path: Path,
    cycles: int,
) -> dict[str, Any]:
    """Run the validated stability suite through the official Python MuJoCo API."""
    try:
        import mujoco
    except ImportError as exc:
        raise StabilityError("The pinned Python MuJoCo runtime is not installed") from exc

    if cycles <= 0:
        raise StabilityError("cycles must be positive")
    model = mujoco.MjModel.from_xml_path(str(model_path))
    actuator_ids = validate_model(config, model_path, model, mujoco)
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)

    previous_targets = [float(data.ctrl[actuator_id]) for actuator_id in actuator_ids]
    aggregate = Metrics()
    aggregate_timings: list[float] = []
    phase_reports: list[dict[str, Any]] = []
    global_step = 0
    monitoring = config.raw["monitoring"]

    for cycle in range(cycles):
        for phase in config.raw["phases"]:
            phase_name = str(phase["name"])
            targets = [float(value) for value in phase["targets_radians"]]
            exceedances = phase_command_exceedances(
                phase,
                config,
                model,
                actuator_ids,
            )
            phase_metrics = Metrics()
            phase_timings: list[float] = []
            total_phase_steps = config.transition_steps + config.hold_steps
            for phase_step in range(total_phase_steps):
                if phase_step < config.transition_steps:
                    progress = (phase_step + 1) / config.transition_steps
                    blend = minimum_jerk(progress)
                else:
                    blend = 1.0
                for index, actuator_id in enumerate(actuator_ids):
                    target_delta = targets[index] - previous_targets[index]
                    data.ctrl[actuator_id] = (
                        previous_targets[index] + target_delta * blend
                    )

                start = time.perf_counter_ns()
                mujoco.mj_step(model, data)
                elapsed_microseconds = (time.perf_counter_ns() - start) / 1000.0
                if not math.isfinite(elapsed_microseconds) or elapsed_microseconds < 0.0:
                    raise StabilityError("Step timing is invalid")
                phase_timings.append(elapsed_microseconds)
                aggregate_timings.append(elapsed_microseconds)
                global_step += 1
                monitor_step(
                    model,
                    data,
                    mujoco,
                    phase_metrics,
                    monitoring,
                    phase_name,
                    global_step,
                )

            aggregate.update_from(phase_metrics)
            phase_reports.append(
                {
                    "cycle": cycle,
                    "name": phase_name,
                    "category": phase["category"],
                    "requested_out_of_range_commands": exceedances,
                    "metrics": metrics_report(phase_metrics, phase_timings),
                }
            )
            previous_targets = targets

    expected_seconds = (
        cycles
        * len(config.raw["phases"])
        * (config.transition_steps + config.hold_steps)
        * config.timestep_seconds
    )
    if abs(float(data.time) - expected_seconds) > 1.0e-7:
        raise StabilityError(
            f"Simulation time mismatch: expected {expected_seconds:.17g}, "
            f"found {float(data.time):.17g}"
        )

    return {
        "schema_version": 1,
        "status": "ok",
        "profile_id": config.raw["profile_id"],
        "platform": "desktop_reference",
        "source": config.raw["source"],
        "timestep_seconds": config.timestep_seconds,
        "cycles": cycles,
        "phase_count_per_cycle": len(config.raw["phases"]),
        "simulated_seconds": float(data.time),
        "expected_counts": config.raw["expected_counts"],
        "monitoring": monitoring,
        "aggregate_metrics": metrics_report(aggregate, aggregate_timings),
        "phases": phase_reports,
    }


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--profile", required=True, type=Path)
    parser.add_argument("--cycles", type=int, default=1)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Run the stability suite and emit a structured report on success or failure."""
    args = parse_args()
    output = args.output.resolve()
    report: dict[str, Any]
    exit_code = 0
    try:
        raw = read_json(args.profile.resolve())
        config = validate_config(raw)
        model_path = args.model.resolve()
        if not model_path.is_file():
            raise StabilityError(f"Reachy MJCF is not a file: {model_path}")
        report = run_suite(config, model_path, args.cycles)
    except (StabilityError, OSError, RuntimeError, ValueError) as exc:
        report = {
            "schema_version": 1,
            "status": "failed",
            "error": str(exc),
        }
        print(f"Reachy upstream baseline stability failed: {exc}", file=sys.stderr)
        exit_code = 1

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    if exit_code == 0:
        aggregate = report["aggregate_metrics"]
        print(
            "Reachy upstream baseline stability passed: "
            f"steps={aggregate['completed_steps']} "
            f"simulated_seconds={report['simulated_seconds']:.9f} "
            f"max_residual={aggregate['maximum_equality_residual']:.9g} "
            f"max_energy={aggregate['maximum_absolute_total_energy_joules']:.9g} "
            f"output={output}"
        )
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
