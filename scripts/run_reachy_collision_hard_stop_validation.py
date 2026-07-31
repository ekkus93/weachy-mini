#!/usr/bin/env python3
"""Validate the generated RMA-065 Reachy collision and hard-stop model."""

from __future__ import annotations

import argparse
import numpy as np
import copy
import json
import math
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


class CollisionValidationError(RuntimeError):
    """Raised when the generated model violates RMA-065 acceptance."""


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise CollisionValidationError(f"JSON root must be an object: {path}")
    return value


def warning_count(data: Any) -> int:
    return sum(max(int(item.number), 0) for item in data.warning)


def object_id(mujoco: Any, model: Any, object_type: Any, name: str) -> int:
    value = int(mujoco.mj_name2id(model, object_type, name))
    if value < 0:
        raise CollisionValidationError(f"model is missing {name}")
    return value


def contact_metrics(mujoco: Any, model: Any, data: Any) -> dict[str, Any]:
    pairs: dict[tuple[int, int], dict[str, float | int]] = {}
    maximum_penetration = 0.0
    maximum_normal_force = 0.0
    maximum_tangent_force = 0.0
    maximum_impulse = 0.0
    force = np.zeros(6, dtype=np.float64)
    for index in range(int(data.ncon)):
        contact = data.contact[index]
        mujoco.mj_contactForce(model, data, index, force)
        normal_force = abs(float(force[0]))
        tangent_force = math.hypot(float(force[1]), float(force[2]))
        penetration = max(0.0, -float(contact.dist))
        impulse = normal_force * float(model.opt.timestep)
        key = tuple(sorted((int(contact.geom1), int(contact.geom2))))
        record = pairs.setdefault(
            key,
            {
                "samples": 0,
                "maximum_penetration_metres": 0.0,
                "maximum_normal_force_newtons": 0.0,
                "maximum_tangent_force_newtons": 0.0,
                "maximum_impulse_newton_seconds": 0.0,
            },
        )
        record["samples"] = int(record["samples"]) + 1
        record["maximum_penetration_metres"] = max(
            float(record["maximum_penetration_metres"]), penetration
        )
        record["maximum_normal_force_newtons"] = max(
            float(record["maximum_normal_force_newtons"]), normal_force
        )
        record["maximum_tangent_force_newtons"] = max(
            float(record["maximum_tangent_force_newtons"]), tangent_force
        )
        record["maximum_impulse_newton_seconds"] = max(
            float(record["maximum_impulse_newton_seconds"]), impulse
        )
        maximum_penetration = max(maximum_penetration, penetration)
        maximum_normal_force = max(maximum_normal_force, normal_force)
        maximum_tangent_force = max(maximum_tangent_force, tangent_force)
        maximum_impulse = max(maximum_impulse, impulse)
    return {
        "contact_count": int(data.ncon),
        "maximum_penetration_metres": maximum_penetration,
        "maximum_normal_force_newtons": maximum_normal_force,
        "maximum_tangent_force_newtons": maximum_tangent_force,
        "maximum_impulse_newton_seconds": maximum_impulse,
        "pairs": [
            {"geom_ids": list(pair), **record}
            for pair, record in sorted(pairs.items())
        ],
    }


def run_steps(mujoco: Any, model: Any, steps: int) -> dict[str, Any]:
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    maximum_contact_count = int(data.ncon)
    maximum_penetration = 0.0
    maximum_normal_force = 0.0
    maximum_impulse = 0.0
    timings: list[float] = []
    observed_contact = int(data.ncon) > 0
    for _ in range(steps):
        start = time.perf_counter_ns()
        mujoco.mj_step(model, data)
        timings.append((time.perf_counter_ns() - start) / 1000.0)
        metrics = contact_metrics(mujoco, model, data)
        observed_contact = observed_contact or metrics["contact_count"] > 0
        maximum_contact_count = max(maximum_contact_count, metrics["contact_count"])
        maximum_penetration = max(
            maximum_penetration, metrics["maximum_penetration_metres"]
        )
        maximum_normal_force = max(
            maximum_normal_force, metrics["maximum_normal_force_newtons"]
        )
        maximum_impulse = max(
            maximum_impulse, metrics["maximum_impulse_newton_seconds"]
        )
    if not all(math.isfinite(float(value)) for value in data.qpos):
        raise CollisionValidationError("simulation produced non-finite qpos")
    if not all(math.isfinite(float(value)) for value in data.qvel):
        raise CollisionValidationError("simulation produced non-finite qvel")
    sorted_timings = sorted(timings)
    p95_index = min(len(sorted_timings) - 1, math.ceil(0.95 * len(sorted_timings)) - 1)
    elapsed_seconds = sum(timings) / 1_000_000.0
    simulated_seconds = steps * float(model.opt.timestep)
    return {
        "steps": steps,
        "observed_contact": observed_contact,
        "maximum_contact_count": maximum_contact_count,
        "maximum_penetration_metres": maximum_penetration,
        "maximum_normal_force_newtons": maximum_normal_force,
        "maximum_impulse_newton_seconds": maximum_impulse,
        "warning_count": warning_count(data),
        "median_step_microseconds": sorted_timings[len(sorted_timings) // 2],
        "p95_step_microseconds": sorted_timings[p95_index],
        "maximum_step_microseconds": max(sorted_timings),
        "realtime_factor": simulated_seconds / elapsed_seconds if elapsed_seconds else 0.0,
        "finite_qpos": True,
        "finite_qvel": True,
    }


def local_from_world(data: Any, body_id: int, point: list[float]) -> list[float]:
    position = [float(value) for value in data.xpos[body_id]]
    rotation = [float(value) for value in data.xmat[body_id]]
    delta = [point[index] - position[index] for index in range(3)]
    return [
        rotation[index] * delta[0]
        + rotation[3 + index] * delta[1]
        + rotation[6 + index] * delta[2]
        for index in range(3)
    ]


def add_probe_geom(
    body: ET.Element,
    name: str,
    pos: list[float],
    contype: int,
    conaffinity: int,
) -> None:
    ET.SubElement(
        body,
        "geom",
        {
            "name": name,
            "type": "sphere",
            "pos": " ".join(format(value, ".17g") for value in pos),
            "size": "0.008",
            "contype": str(contype),
            "conaffinity": str(conaffinity),
            "friction": "0.8 0.02 0.002",
            "solref": "0.01 1",
            "solimp": "0.9 0.95 0.001 0.5 2",
            "mass": "0.000001",
        },
    )


def internal_contact_fixture(
    mujoco: Any,
    model_path: Path,
    output_path: Path,
) -> None:
    base_model = mujoco.MjModel.from_xml_path(str(model_path))
    data = mujoco.MjData(base_model)
    mujoco.mj_forward(base_model, data)
    arm_id = object_id(
        mujoco,
        base_model,
        mujoco.mjtObj.mjOBJ_BODY,
        "dc15_a01_horn_dummy",
    )
    arm_origin = [float(value) for value in data.xpos[arm_id]]
    contract_tree = ET.parse(model_path)
    penetration_nodes = [
        node
        for node in contract_tree.getroot().findall("./custom/numeric")
        if "penetration" in (node.get("name") or "").lower()
    ]
    if len(penetration_nodes) != 1:
        raise CollisionValidationError(
            "generated model must expose exactly one penetration limit"
        )
    penetration_data = penetration_nodes[0].get("data")
    if penetration_data is None:
        raise CollisionValidationError(
            "generated model penetration limit has no data"
        )
    penetration_parts = penetration_data.split()
    if len(penetration_parts) != 1:
        raise CollisionValidationError(
            "generated model penetration limit must contain one value"
        )
    try:
        maximum_penetration = float(penetration_parts[0])
    except ValueError as exc:
        raise CollisionValidationError(
            "generated model penetration limit is not numeric"
        ) from exc
    if not (0.0 < maximum_penetration < float("inf")):
        raise CollisionValidationError(
            "generated model penetration limit must be finite and positive"
        )
    probe_radius = 0.008
    target_penetration = maximum_penetration / 16.0
    centre_separation = 2.0 * probe_radius - target_penetration
    if not (
        0.0 < target_penetration < maximum_penetration
        and 0.0 < centre_separation < 2.0 * probe_radius
    ):
        raise CollisionValidationError(
            "derived internal fixture penetration is invalid"
        )
    direction = [centre_separation, 0.0, 0.0]
    arm_point = [
        arm_origin[index] - 0.5 * direction[index]
        for index in range(3)
    ]
    shell_point = [
        arm_origin[index] + 0.5 * direction[index]
        for index in range(3)
    ]
    arm_local = local_from_world(data, arm_id, arm_point)

    tree = ET.parse(model_path)
    root = tree.getroot()
    for geom in root.findall(".//geom"):
        geom.set("contype", "0")
        geom.set("conaffinity", "0")
    bodies = {
        body.get("name"): body
        for body in root.findall(".//body")
        if body.get("name")
    }
    worldbody = root.find("worldbody")
    if worldbody is None:
        raise CollisionValidationError("model has no worldbody")
    add_probe_geom(
        bodies["dc15_a01_horn_dummy"],
        "rma065_fixture_internal_moving",
        arm_local,
        2,
        5,
    )
    add_probe_geom(
        worldbody,
        "rma065_fixture_internal_shell",
        shell_point,
        1,
        6,
    )
    ET.indent(tree, space="  ")
    tree.write(output_path, encoding="utf-8", xml_declaration=True)


def external_contact_fixture(
    mujoco: Any,
    model_path: Path,
    output_path: Path,
) -> None:
    base_model = mujoco.MjModel.from_xml_path(str(model_path))
    data = mujoco.MjData(base_model)
    mujoco.mj_forward(base_model, data)
    geom_id = object_id(
        mujoco,
        base_model,
        mujoco.mjtObj.mjOBJ_GEOM,
        "rma065_head_shell",
    )
    body_id = int(base_model.geom_bodyid[geom_id])
    body_name = mujoco.mj_id2name(
        base_model,
        mujoco.mjtObj.mjOBJ_BODY,
        body_id,
    )
    if not body_name:
        raise CollisionValidationError(
            "rma065_head_shell parent body has no name"
        )
    centre = [float(value) for value in data.geom_xpos[geom_id]]

    tree = ET.parse(model_path)
    root = tree.getroot()
    for geom in root.findall(".//geom"):
        geom.set("contype", "0")
        geom.set("conaffinity", "0")

    penetration_nodes = [
        node
        for node in root.findall("./custom/numeric")
        if "penetration" in (node.get("name") or "").lower()
    ]
    if len(penetration_nodes) != 1:
        raise CollisionValidationError(
            "generated model must expose exactly one penetration limit"
        )
    penetration_data = penetration_nodes[0].get("data")
    if penetration_data is None:
        raise CollisionValidationError(
            "generated model penetration limit has no data"
        )
    penetration_parts = penetration_data.split()
    if len(penetration_parts) != 1:
        raise CollisionValidationError(
            "generated model penetration limit must contain one value"
        )
    try:
        maximum_penetration = float(penetration_parts[0])
    except ValueError as exc:
        raise CollisionValidationError(
            "generated model penetration limit is not numeric"
        ) from exc
    if not (0.0 < maximum_penetration < float("inf")):
        raise CollisionValidationError(
            "generated model penetration limit must be finite and positive"
        )

    probe_radius = 0.008
    target_penetration = maximum_penetration / 16.0
    centre_separation = 2.0 * probe_radius - target_penetration
    if not (
        0.0 < target_penetration < maximum_penetration
        and 0.0 < centre_separation < 2.0 * probe_radius
    ):
        raise CollisionValidationError(
            "derived external fixture penetration is invalid"
        )
    direction = [centre_separation, 0.0, 0.0]
    shell_point = [
        centre[index] - 0.5 * direction[index]
        for index in range(3)
    ]
    external_point = [
        centre[index] + 0.5 * direction[index]
        for index in range(3)
    ]
    shell_local = local_from_world(data, body_id, shell_point)

    bodies = {
        body.get("name"): body
        for body in root.findall(".//body")
        if body.get("name")
    }
    shell_body = bodies.get(body_name)
    if shell_body is None:
        raise CollisionValidationError(
            f"rma065_head_shell parent body is missing: {body_name}"
        )
    worldbody = root.find("worldbody")
    if worldbody is None:
        raise CollisionValidationError("model has no worldbody")
    add_probe_geom(
        shell_body,
        "rma065_fixture_external_shell",
        shell_local,
        1,
        6,
    )
    add_probe_geom(
        worldbody,
        "rma065_fixture_external_sphere",
        external_point,
        4,
        3,
    )
    ET.indent(tree, space="  ")
    tree.write(output_path, encoding="utf-8", xml_declaration=True)


def hard_stop_trial(mujoco: Any, model: Any, joint_name: str) -> dict[str, Any]:
    data = mujoco.MjData(model)
    joint_id = object_id(mujoco, model, mujoco.mjtObj.mjOBJ_JOINT, joint_name)
    qpos_address = int(model.jnt_qposadr[joint_id])
    dof_address = int(model.jnt_dofadr[joint_id])
    lower = float(model.jnt_range[joint_id][0])
    upper = float(model.jnt_range[joint_id][1])
    data.qpos[qpos_address] = upper - 0.0005
    data.qvel[dof_address] = 8.0
    mujoco.mj_forward(model, data)
    maximum_position = float(data.qpos[qpos_address])
    observed_limit_constraint = False
    maximum_limit_force = 0.0
    for _ in range(200):
        mujoco.mj_step(model, data)
        maximum_position = max(maximum_position, float(data.qpos[qpos_address]))
        for row in range(int(data.nefc)):
            if (
                int(data.efc_type[row])
                == int(mujoco.mjtConstraint.mjCNSTR_LIMIT_JOINT)
                and int(data.efc_id[row]) == joint_id
            ):
                observed_limit_constraint = True
                maximum_limit_force = max(
                    maximum_limit_force, abs(float(data.efc_force[row]))
                )
    if warning_count(data) != 0:
        raise CollisionValidationError(f"hard-stop trial for {joint_name} produced warnings")
    tolerance = 1.0e-6
    if maximum_position > upper + tolerance:
        raise CollisionValidationError(
            f"{joint_name} passed its hard stop: {maximum_position} > {upper}"
        )
    if not observed_limit_constraint:
        raise CollisionValidationError(
            f"{joint_name} trial did not produce a hard-stop constraint"
        )
    return {
        "joint": joint_name,
        "lower_limit": lower,
        "upper_limit": upper,
        "maximum_position": maximum_position,
        "maximum_limit_force": maximum_limit_force,
        "observed_limit_constraint": observed_limit_constraint,
        "warning_count": 0,
    }


def validate_model_inventory(mujoco: Any, model: Any, profile: dict[str, Any]) -> dict[str, Any]:
    generated_geom_ids = [
        object_id(mujoco, model, mujoco.mjtObj.mjOBJ_GEOM, shape["name"])
        for shape in profile["shapes"]
    ]
    collision_bodies = {
        int(model.geom_bodyid[geom_id])
        for geom_id in range(int(model.ngeom))
        if int(model.geom_contype[geom_id]) != 0
        or int(model.geom_conaffinity[geom_id]) != 0
    }
    limited_joints = [
        joint_id
        for joint_id in range(int(model.njnt))
        if bool(model.jnt_limited[joint_id])
    ]
    if len(generated_geom_ids) != len(profile["shapes"]):
        raise CollisionValidationError("generated collision geom count mismatch")
    if len(collision_bodies) < 17:
        raise CollisionValidationError(
            f"enhanced model covers only {len(collision_bodies)} collision bodies"
        )
    if len(limited_joints) != 9:
        raise CollisionValidationError(
            f"enhanced model has {len(limited_joints)} limited joints, expected 9"
        )
    for stop in profile["hard_stops"]:
        joint_id = object_id(
            mujoco, model, mujoco.mjtObj.mjOBJ_JOINT, stop["joint"]
        )
        actuator_id = object_id(
            mujoco, model, mujoco.mjtObj.mjOBJ_ACTUATOR, stop["actuator"]
        )
        hard_lower = float(model.jnt_range[joint_id][0])
        hard_upper = float(model.jnt_range[joint_id][1])
        soft_lower = float(model.actuator_ctrlrange[actuator_id][0])
        soft_upper = float(model.actuator_ctrlrange[actuator_id][1])
        if not hard_lower < soft_lower < soft_upper < hard_upper:
            raise CollisionValidationError(
                f"soft command range is not inside hard range for {stop['joint']}"
            )
    return {
        "compiled_geom_count": int(model.ngeom),
        "generated_collision_geom_count": len(generated_geom_ids),
        "collision_body_count": len(collision_bodies),
        "limited_joint_count": len(limited_joints),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-model", type=Path, required=True)
    parser.add_argument("--enhanced-model", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--neutral-steps", type=int, default=5000)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        import mujoco
    except ImportError as exc:
        raise CollisionValidationError("mujoco is not installed") from exc
    profile = read_json(args.profile)
    source_model = mujoco.MjModel.from_xml_path(str(args.source_model))
    enhanced_model = mujoco.MjModel.from_xml_path(str(args.enhanced_model))
    inventory = validate_model_inventory(mujoco, enhanced_model, profile)
    source_neutral = run_steps(mujoco, source_model, args.neutral_steps)
    enhanced_neutral = run_steps(mujoco, enhanced_model, args.neutral_steps)
    maximum_penetration = float(
        profile["contact_parameters"]["maximum_penetration_metres"]
    )
    if enhanced_neutral["warning_count"] != 0:
        raise CollisionValidationError("enhanced neutral run produced warnings")
    if enhanced_neutral["maximum_penetration_metres"] > maximum_penetration:
        raise CollisionValidationError("enhanced neutral penetration exceeds profile")
    overhead = (
        enhanced_neutral["p95_step_microseconds"]
        / source_neutral["p95_step_microseconds"]
        - 1.0
    )
    hosted_budget = float(
        profile["android_budget"]["maximum_p95_step_overhead_ratio"]
    )
    if overhead > hosted_budget:
        raise CollisionValidationError(
            f"hosted p95 collision overhead {overhead:.6f} exceeds {hosted_budget:.6f}"
        )

    with tempfile.TemporaryDirectory() as temp_text:
        temp = Path(temp_text)
        assets = (args.enhanced_model.parent / "assets").resolve()
        if not assets.is_dir():
            raise SystemExit(
                f"enhanced model asset directory is missing: {assets}"
            )
        (temp / "assets").symlink_to(
            assets,
            target_is_directory=True,
        )
        internal_path = temp / "internal.xml"
        external_path = temp / "external.xml"
        internal_contact_fixture(mujoco, args.enhanced_model, internal_path)
        external_contact_fixture(mujoco, args.enhanced_model, external_path)
        internal = run_steps(
            mujoco,
            mujoco.MjModel.from_xml_path(str(internal_path)),
            500,
        )
        external = run_steps(
            mujoco,
            mujoco.MjModel.from_xml_path(str(external_path)),
            500,
        )
    for label, result in (("internal", internal), ("external", external)):
        if not result["observed_contact"]:
            raise CollisionValidationError(f"{label} fixture produced no contact")
        if result["warning_count"] != 0:
            raise CollisionValidationError(f"{label} fixture produced warnings")
        if result["maximum_penetration_metres"] > maximum_penetration:
            raise CollisionValidationError(
                f"{label} fixture penetration exceeds the profile"
            )
        if result["maximum_normal_force_newtons"] <= 0.0:
            raise CollisionValidationError(f"{label} fixture exposed no contact force")
        if result["maximum_impulse_newton_seconds"] <= 0.0:
            raise CollisionValidationError(f"{label} fixture exposed no impulse")

    hard_stops = [
        hard_stop_trial(mujoco, enhanced_model, "yaw_body"),
        hard_stop_trial(mujoco, enhanced_model, "right_antenna"),
    ]
    report = {
        "contract": "rma065_collision_hard_stop_validation_v1",
        "inventory": inventory,
        "source_neutral": source_neutral,
        "enhanced_neutral": enhanced_neutral,
        "hosted_p95_overhead_ratio": overhead,
        "internal_contact_fixture": internal,
        "external_contact_fixture": external,
        "hard_stop_trials": hard_stops,
        "acceptance": {
            "representative_internal_contact_stable": True,
            "representative_external_contact_stable": True,
            "hard_stops_contain_outward_motion": True,
            "contact_force_and_impulse_exposed": True,
            "hosted_complexity_within_budget": True,
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
