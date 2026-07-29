#!/usr/bin/env python3
"""Load the official Reachy MJCF through MuJoCo and validate its baseline."""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

import mujoco
import numpy as np


class MujocoValidationError(RuntimeError):
    """Raised when a compiled MuJoCo model violates the pinned baseline."""


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


def require_close_vector(
    label: str,
    actual: np.ndarray[Any, Any],
    expected: object,
    tolerance: object,
) -> None:
    """Require a finite numeric vector to match an absolute tolerance."""
    if not isinstance(expected, list) or not expected:
        raise MujocoValidationError(f"Baseline {label} must be a nonempty array")
    if not isinstance(tolerance, int | float) or isinstance(tolerance, bool) or tolerance < 0:
        raise MujocoValidationError(f"Baseline {label} tolerance must be nonnegative")
    expected_array = np.asarray(expected, dtype=np.float64)
    if actual.shape != expected_array.shape:
        raise MujocoValidationError(
            f"Baseline {label} shape mismatch: expected {expected_array.shape}, "
            f"found {actual.shape}"
        )
    finite_array(f"baseline {label}", expected_array)
    maximum_error = float(np.max(np.abs(actual - expected_array)))
    if maximum_error > float(tolerance):
        raise MujocoValidationError(
            f"Baseline {label} mismatch: maximum absolute error {maximum_error:.17g} "
            f"exceeds tolerance {float(tolerance):.17g}"
        )


def require_compiler_attributes(model_path: Path, baseline: dict[str, Any]) -> None:
    """Require source compiler units and mesh resolution semantics."""
    try:
        root = ET.fromstring(model_path.read_bytes())
    except (OSError, ET.ParseError) as exc:
        raise MujocoValidationError(f"Cannot parse source MJCF {model_path}: {exc}") from exc
    compiler = root.find("compiler")
    if compiler is None:
        raise MujocoValidationError("Source MJCF is missing its compiler element")
    coordinate_convention = baseline.get("coordinate_convention")
    if not isinstance(coordinate_convention, dict):
        raise MujocoValidationError("Baseline coordinate_convention must be an object")
    expected_attributes = coordinate_convention.get("source_compiler_attributes")
    if not isinstance(expected_attributes, dict):
        raise MujocoValidationError("Baseline source_compiler_attributes must be an object")
    for name, expected in expected_attributes.items():
        actual = compiler.attrib.get(name)
        if actual != expected:
            raise MujocoValidationError(
                f"MJCF compiler attribute {name!r} mismatch: expected {expected!r}, "
                f"found {actual!r}"
            )


def compiled_counts(model: mujoco.MjModel) -> dict[str, int]:
    """Return the compiled dimensions covered by the baseline."""
    return {
        "actuators": model.nu,
        "bodies_including_world": model.nbody,
        "cameras": model.ncam,
        "equalities": model.neq,
        "joints": model.njnt,
        "nq": model.nq,
        "nv": model.nv,
        "sites": model.nsite,
    }


def validate_baseline_identity(
    baseline: dict[str, Any],
    model_map: dict[str, Any],
    expected_version: str,
) -> None:
    """Require the baseline, source map, and independently pinned runtime to agree."""
    if baseline.get("schema_version") != 1:
        raise MujocoValidationError(
            f"Unsupported model baseline schema: {baseline.get('schema_version')!r}"
        )
    baseline_version = baseline.get("mujoco_version")
    if baseline_version != expected_version:
        raise MujocoValidationError(
            f"Baseline MuJoCo version mismatch: expected {expected_version}, "
            f"found {baseline_version!r}"
        )
    source = baseline.get("source")
    if not isinstance(source, dict):
        raise MujocoValidationError("Baseline source must be an object")
    source_model = model_map.get("source_model")
    if not isinstance(source_model, dict):
        raise MujocoValidationError("Model map source_model must be an object")
    comparisons = {
        "model path": (source_model.get("path"), source.get("model_path")),
        "model SHA-256": (source_model.get("sha256"), source.get("model_sha256")),
    }
    for label, (actual, expected) in comparisons.items():
        if actual != expected:
            raise MujocoValidationError(
                f"Baseline {label} mismatch: expected {expected!r}, found {actual!r}"
            )


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--model-map", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--expected-version", required=True)
    parser.add_argument("--reference-body")
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
        baseline_path = args.baseline.resolve()
        baseline = read_json(baseline_path)
        validate_baseline_identity(baseline, model_map, args.expected_version)
        require_compiler_attributes(model_path, baseline)
        model = mujoco.MjModel.from_xml_path(str(model_path))

        map_counts = model_map["counts"]
        require_count("body", model.nbody, map_counts["bodies"] + 1)
        require_count("joint", model.njnt, map_counts["joints"])
        require_count("actuator", model.nu, map_counts["actuators"])
        require_count("equality", model.neq, map_counts["equalities"])
        require_count("site", model.nsite, map_counts["sites"])
        require_count("camera", model.ncam, map_counts["cameras"])
        require_names(model, model_map, "bodies", mujoco.mjtObj.mjOBJ_BODY)
        require_names(model, model_map, "joints", mujoco.mjtObj.mjOBJ_JOINT)
        require_names(model, model_map, "actuators", mujoco.mjtObj.mjOBJ_ACTUATOR)
        require_names(model, model_map, "sites", mujoco.mjtObj.mjOBJ_SITE)
        require_names(model, model_map, "cameras", mujoco.mjtObj.mjOBJ_CAMERA)

        actual_counts = compiled_counts(model)
        expected_counts = baseline.get("compiled_counts")
        if not isinstance(expected_counts, dict):
            raise MujocoValidationError("Baseline compiled_counts must be an object")
        if actual_counts != expected_counts:
            raise MujocoValidationError(
                f"Compiled dimensions differ from baseline: expected {expected_counts}, "
                f"found {actual_counts}"
            )

        reference_pose = baseline.get("reference_pose")
        if not isinstance(reference_pose, dict):
            raise MujocoValidationError("Baseline reference_pose must be an object")
        reference_body = reference_pose.get("body")
        if not isinstance(reference_body, str) or not reference_body:
            raise MujocoValidationError("Baseline reference body must be a nonempty string")
        if args.reference_body is not None and args.reference_body != reference_body:
            raise MujocoValidationError(
                f"Requested reference body {args.reference_body!r} does not match "
                f"baseline body {reference_body!r}"
            )
        reference_body_id = mujoco.mj_name2id(
            model,
            mujoco.mjtObj.mjOBJ_BODY,
            reference_body,
        )
        if reference_body_id < 0:
            raise MujocoValidationError(
                f"Reference body is missing from compiled model: {reference_body}"
            )

        validation = baseline.get("validation")
        if not isinstance(validation, dict):
            raise MujocoValidationError("Baseline validation must be an object")
        expected_steps = validation.get("uncommanded_steps")
        if args.steps != expected_steps:
            raise MujocoValidationError(
                f"Validation step count mismatch: expected {expected_steps}, found {args.steps}"
            )

        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        initial_position = data.xpos[reference_body_id].copy()
        initial_quaternion = data.xquat[reference_body_id].copy()
        require_close_vector(
            "reference position",
            initial_position,
            reference_pose.get("initial_position_metres"),
            reference_pose.get("absolute_position_tolerance_metres"),
        )
        require_close_vector(
            "reference quaternion",
            initial_quaternion,
            reference_pose.get("initial_quaternion_wxyz"),
            reference_pose.get("absolute_quaternion_component_tolerance"),
        )

        for _ in range(args.steps):
            mujoco.mj_step(model, data)

        finite_array("qpos", data.qpos)
        finite_array("qvel", data.qvel)
        finite_array("body positions", data.xpos)
        finite_array("body quaternions", data.xquat)

        report = {
            "baseline": {
                "path": baseline_path.relative_to(Path.cwd()).as_posix()
                if baseline_path.is_relative_to(Path.cwd())
                else str(baseline_path),
                "reference_pose_within_tolerance": True,
            },
            "compiled_counts": actual_counts,
            "coordinate_convention": baseline["coordinate_convention"],
            "model": model_map["model"],
            "mujoco_version": mujoco.__version__,
            "reference_body": {
                "initial_position_metres": initial_position.tolist(),
                "initial_quaternion_wxyz": initial_quaternion.tolist(),
                "name": reference_body,
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
        f"reference_body={reference_body} "
        f"output={output_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
