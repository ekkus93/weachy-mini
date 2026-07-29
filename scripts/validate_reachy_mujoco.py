#!/usr/bin/env python3
"""Load the official Reachy MJCF through MuJoCo and emit deterministic evidence."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

import mujoco
import numpy as np


class MujocoValidationError(RuntimeError):
    """Raised when a compiled MuJoCo model violates the imported model map."""


def read_json(path: Path) -> dict[str, Any]:
    """Read a JSON object from disk."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise MujocoValidationError(f"Cannot read JSON file {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise MujocoValidationError(f"JSON root must be an object: {path}")
    return value


def require_count(label: str, actual: int, expected: int) -> None:
    """Require an exact compiled-model count."""
    if actual != expected:
        raise MujocoValidationError(
            f"Compiled MuJoCo {label} count mismatch: expected {expected}, found {actual}"
        )


def require_names(
    model: mujoco.MjModel,
    model_map: dict[str, Any],
    category: str,
    object_type: mujoco.mjtObj,
) -> None:
    """Require every named map entity to exist in the compiled model."""
    missing = []
    for entry in model_map[category]:
        name = entry["name"]
        if name is None:
            continue
        if mujoco.mj_name2id(model, object_type, name) < 0:
            missing.append(name)
    if missing:
        raise MujocoValidationError(
            f"Compiled MuJoCo model is missing {category}: {', '.join(sorted(missing))}"
        )


def finite_array(label: str, values: np.ndarray[Any, Any]) -> None:
    """Reject NaN or infinity in a MuJoCo state array."""
    if not bool(np.all(np.isfinite(values))):
        raise MujocoValidationError(f"Compiled MuJoCo state contains non-finite {label}")


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--model-map", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--expected-version", required=True)
    parser.add_argument("--reference-body", default="xl_330")
    parser.add_argument("--steps", type=int, default=100)
    return parser.parse_args()


def main() -> int:
    """Compile, step, and validate the pinned Reachy model."""
    args = parse_args()
    try:
        if args.steps < 0:
            raise MujocoValidationError("Step count must not be negative")
        if mujoco.__version__ != args.expected_version:
            raise MujocoValidationError(
                f"MuJoCo version mismatch: expected {args.expected_version}, "
                f"found {mujoco.__version__}"
            )
        model_path = args.model.resolve()
        if not model_path.is_file():
            raise MujocoValidationError(f"Reachy MJCF is not a file: {model_path}")
        model_map = read_json(args.model_map.resolve())
        model = mujoco.MjModel.from_xml_path(str(model_path))

        counts = model_map["counts"]
        require_count("body", model.nbody, counts["bodies"] + 1)
        require_count("joint", model.njnt, counts["joints"])
        require_count("actuator", model.nu, counts["actuators"])
        require_count("equality", model.neq, counts["equalities"])
        require_count("site", model.nsite, counts["sites"])
        require_count("camera", model.ncam, counts["cameras"])
        require_names(model, model_map, "bodies", mujoco.mjtObj.mjOBJ_BODY)
        require_names(model, model_map, "joints", mujoco.mjtObj.mjOBJ_JOINT)
        require_names(model, model_map, "actuators", mujoco.mjtObj.mjOBJ_ACTUATOR)
        require_names(model, model_map, "sites", mujoco.mjtObj.mjOBJ_SITE)
        require_names(model, model_map, "cameras", mujoco.mjtObj.mjOBJ_CAMERA)

        reference_body_id = mujoco.mj_name2id(
            model,
            mujoco.mjtObj.mjOBJ_BODY,
            args.reference_body,
        )
        if reference_body_id < 0:
            raise MujocoValidationError(
                f"Reference body is missing from compiled model: {args.reference_body}"
            )

        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        initial_position = data.xpos[reference_body_id].copy()
        initial_quaternion = data.xquat[reference_body_id].copy()
        for _ in range(args.steps):
            mujoco.mj_step(model, data)

        finite_array("qpos", data.qpos)
        finite_array("qvel", data.qvel)
        finite_array("body positions", data.xpos)
        finite_array("body quaternions", data.xquat)

        report = {
            "compiled_counts": {
                "actuators": model.nu,
                "bodies_including_world": model.nbody,
                "cameras": model.ncam,
                "equalities": model.neq,
                "joints": model.njnt,
                "nq": model.nq,
                "nv": model.nv,
                "sites": model.nsite,
            },
            "model": model_map["model"],
            "mujoco_version": mujoco.__version__,
            "reference_body": {
                "initial_position_metres": initial_position.tolist(),
                "initial_quaternion_wxyz": initial_quaternion.tolist(),
                "name": args.reference_body,
            },
            "schema_version": 1,
            "source_model": model_map["source_model"],
            "steps_completed": args.steps,
        }
        output_path = args.output.resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    except (MujocoValidationError, OSError, RuntimeError, ValueError) as exc:
        print(f"Reachy MuJoCo validation failed: {exc}", file=sys.stderr)
        return 1

    print(
        "Reachy MuJoCo validation passed: "
        f"version={mujoco.__version__} "
        f"steps={args.steps} "
        f"reference_body={args.reference_body} "
        f"output={output_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
