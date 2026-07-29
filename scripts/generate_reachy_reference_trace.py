#!/usr/bin/env python3
"""Generate the deterministic desktop RMA-042 reference trace."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any

import mujoco
import numpy as np


class TraceGenerationError(RuntimeError):
    """Raised when the model or scenario cannot produce a valid reference trace."""


def read_json(path: Path) -> tuple[dict[str, Any], bytes]:
    """Read a JSON object and retain its exact source bytes."""
    try:
        raw = path.read_bytes()
        value = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise TraceGenerationError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise TraceGenerationError(f"JSON root must be an object: {path}")
    return value, raw


def require_exact_counts(model: mujoco.MjModel, expected: dict[str, Any]) -> dict[str, int]:
    """Require the compiled model dimensions declared by the scenario."""
    actual = {
        "bodies_including_world": model.nbody,
        "joints": model.njnt,
        "actuators": model.nu,
        "equalities": model.neq,
        "sites": model.nsite,
        "cameras": model.ncam,
        "nq": model.nq,
        "nv": model.nv,
    }
    if actual != expected:
        raise TraceGenerationError(
            f"Compiled model dimensions differ from scenario: expected {expected}, found {actual}"
        )
    return actual


def resolve_names(
    model: mujoco.MjModel,
    names: list[str],
    object_type: mujoco.mjtObj,
    label: str,
) -> list[int]:
    """Resolve a complete ordered list of MuJoCo object IDs."""
    ids: list[int] = []
    for name in names:
        object_id = mujoco.mj_name2id(model, object_type, name)
        if object_id < 0:
            raise TraceGenerationError(f"Compiled model is missing {label}: {name}")
        ids.append(object_id)
    return ids


def warning_count(data: mujoco.MjData) -> int:
    """Return the cumulative MuJoCo warning count."""
    return sum(int(item.number) for item in data.warning)


def maximum_equality_residual(data: mujoco.MjData) -> float:
    """Return the maximum absolute equality-constraint residual."""
    if data.nefc == 0:
        return 0.0
    constraint_types = np.asarray(data.efc_type)
    residuals = np.asarray(data.efc_pos)
    equality_value = int(mujoco.mjtConstraint.mjCNSTR_EQUALITY)
    equality_rows = residuals[constraint_types == equality_value]
    if equality_rows.size == 0:
        return 0.0
    maximum = float(np.max(np.abs(equality_rows)))
    if not math.isfinite(maximum):
        raise TraceGenerationError("Equality residual is not finite")
    return maximum


def require_finite(label: str, values: np.ndarray[Any, Any]) -> None:
    """Reject non-finite state before serializing it as evidence."""
    if not bool(np.all(np.isfinite(values))):
        raise TraceGenerationError(f"Non-finite {label} detected")


def phase_for_step(phases: list[dict[str, Any]], step: int) -> dict[str, Any]:
    """Return the active command phase for a zero-based step index."""
    active = phases[0]
    for phase in phases[1:]:
        if step < phase["start_step"]:
            break
        active = phase
    return active


def apply_targets(
    data: mujoco.MjData,
    actuator_ids: list[int],
    phase: dict[str, Any],
) -> None:
    """Apply one ordered position-target vector."""
    targets = phase["targets_radians"]
    for actuator_id, target in zip(actuator_ids, targets, strict=True):
        data.ctrl[actuator_id] = float(target)


def capture_checkpoint(
    data: mujoco.MjData,
    body_names: list[str],
    body_ids: list[int],
    step: int,
    maximum_allowed_residual: float,
) -> dict[str, Any]:
    """Capture one complete cross-platform comparison checkpoint."""
    require_finite("qpos", data.qpos)
    require_finite("qvel", data.qvel)
    require_finite("body positions", data.xpos)
    require_finite("body quaternions", data.xquat)
    residual = maximum_equality_residual(data)
    if residual > maximum_allowed_residual:
        raise TraceGenerationError(
            f"Equality residual {residual:.17g} exceeds {maximum_allowed_residual:.17g} "
            f"at step {step}"
        )
    warnings = warning_count(data)
    if warnings != 0:
        raise TraceGenerationError(f"MuJoCo warnings present at step {step}: {warnings}")
    bodies = []
    for name, body_id in zip(body_names, body_ids, strict=True):
        bodies.append(
            {
                "name": name,
                "position_metres": data.xpos[body_id].tolist(),
                "quaternion_wxyz": data.xquat[body_id].tolist(),
            }
        )
    return {
        "step": step,
        "simulation_time": float(data.time),
        "maximum_equality_residual": residual,
        "warning_count": warnings,
        "qpos": data.qpos.tolist(),
        "qvel": data.qvel.tolist(),
        "bodies": bodies,
    }


def generate_trace(model_path: Path, scenario_path: Path) -> dict[str, Any]:
    """Compile the model and execute the shared reference scenario."""
    scenario, scenario_raw = read_json(scenario_path)
    source = scenario["source"]
    if mujoco.__version__ != source["mujoco_version"]:
        raise TraceGenerationError(
            f"MuJoCo version mismatch: expected {source['mujoco_version']}, "
            f"found {mujoco.__version__}"
        )
    try:
        model_raw = model_path.read_bytes()
    except OSError as exc:
        raise TraceGenerationError(f"Cannot read model {model_path}: {exc}") from exc
    model_sha256 = hashlib.sha256(model_raw).hexdigest()
    if model_sha256 != source["model_sha256"]:
        raise TraceGenerationError(
            f"Model SHA-256 mismatch: expected {source['model_sha256']}, "
            f"found {model_sha256}"
        )

    model = mujoco.MjModel.from_xml_path(str(model_path))
    counts = require_exact_counts(model, scenario["expected_counts"])
    if abs(float(model.opt.timestep) - float(scenario["timestep_seconds"])) > 1e-12:
        raise TraceGenerationError("Compiled model timestep differs from scenario")
    actuator_names = list(scenario["actuator_names"])
    body_names = list(scenario["body_names"])
    actuator_ids = resolve_names(
        model,
        actuator_names,
        mujoco.mjtObj.mjOBJ_ACTUATOR,
        "actuator",
    )
    body_ids = resolve_names(model, body_names, mujoco.mjtObj.mjOBJ_BODY, "body")
    phases = list(scenario["phases"])
    checkpoints = list(scenario["checkpoint_steps"])
    checkpoint_set = set(checkpoints)
    maximum_allowed_residual = float(
        scenario["tolerances"]["maximum_equality_residual"]
    )

    data = mujoco.MjData(model)
    apply_targets(data, actuator_ids, phase_for_step(phases, 0))
    mujoco.mj_forward(model, data)
    trace_checkpoints = [
        capture_checkpoint(
            data,
            body_names,
            body_ids,
            0,
            maximum_allowed_residual,
        )
    ]
    for zero_based_step in range(scenario["total_steps"]):
        apply_targets(data, actuator_ids, phase_for_step(phases, zero_based_step))
        mujoco.mj_step(model, data)
        completed_step = zero_based_step + 1
        if completed_step in checkpoint_set:
            trace_checkpoints.append(
                capture_checkpoint(
                    data,
                    body_names,
                    body_ids,
                    completed_step,
                    maximum_allowed_residual,
                )
            )
    actual_steps = [checkpoint["step"] for checkpoint in trace_checkpoints]
    if actual_steps != checkpoints:
        raise TraceGenerationError(
            f"Captured checkpoint set differs from scenario: {actual_steps}"
        )

    return {
        "schema_version": 1,
        "status": "ok",
        "platform": "desktop_reference",
        "scenario_id": scenario["scenario_id"],
        "scenario_sha256": hashlib.sha256(scenario_raw).hexdigest(),
        "source_model_sha256": model_sha256,
        "mujoco_version": mujoco.__version__,
        "compiled_counts": counts,
        "checkpoints": trace_checkpoints,
    }


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--scenario", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Generate and write the desktop reference trace."""
    args = parse_args()
    try:
        trace = generate_trace(args.model.resolve(), args.scenario.resolve())
        output = args.output.resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(
            json.dumps(trace, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    except (TraceGenerationError, KeyError, TypeError, ValueError, RuntimeError) as exc:
        print(f"Reference trace generation failed: {exc}", file=sys.stderr)
        return 1
    print(
        "Desktop reference trace generated: "
        f"scenario={trace['scenario_id']} checkpoints={len(trace['checkpoints'])} "
        f"output={output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
