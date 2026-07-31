#!/usr/bin/env python3
"""Audit Reachy Mini MuJoCo collision geometry, contacts, limits, and step cost."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import time
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


class CollisionAuditError(RuntimeError):
    """Raised when model provenance or collision diagnostics are invalid."""


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def object_name(mujoco: Any, model: Any, object_type: Any, object_id: int) -> str:
    return mujoco.mj_id2name(model, object_type, object_id) or f"unnamed_{object_id}"


def warning_count(data: Any) -> int:
    return sum(max(int(item.number), 0) for item in data.warning)


def source_inventory(model_path: Path) -> dict[str, Any]:
    root = ET.parse(model_path).getroot()
    bodies: list[dict[str, Any]] = []
    collision_geoms: list[dict[str, str]] = []
    for body in root.findall(".//body"):
        geoms = body.findall("./geom")
        bodies.append(
            {
                "name": body.get("name"),
                "direct_geom_count": len(geoms),
                "direct_joint_names": [
                    joint.get("name") for joint in body.findall("./joint")
                ],
                "direct_collision_geom_count": sum(
                    geom.get("class") == "collision"
                    or geom.get("contype") not in (None, "0")
                    or geom.get("conaffinity") not in (None, "0")
                    for geom in geoms
                ),
            }
        )
    for geom in root.findall(".//geom"):
        if (
            geom.get("class") == "collision"
            or geom.get("contype") not in (None, "0")
            or geom.get("conaffinity") not in (None, "0")
        ):
            collision_geoms.append(dict(sorted(geom.attrib.items())))
    return {
        "body_count": len(bodies),
        "bodies": bodies,
        "collision_geom_count": len(collision_geoms),
        "collision_geoms": collision_geoms,
    }


def compiled_inventory(mujoco: Any, model: Any) -> dict[str, Any]:
    collision_by_body: dict[str, list[dict[str, Any]]] = defaultdict(list)
    geom_type_counts: Counter[str] = Counter()
    collision_geom_count = 0
    for geom_id in range(int(model.ngeom)):
        body_id = int(model.geom_bodyid[geom_id])
        body_name = object_name(
            mujoco, model, mujoco.mjtObj.mjOBJ_BODY, body_id
        )
        geom_name = object_name(
            mujoco, model, mujoco.mjtObj.mjOBJ_GEOM, geom_id
        )
        geom_type = int(model.geom_type[geom_id])
        type_name = mujoco.mjtGeom(geom_type).name
        geom_type_counts[type_name] += 1
        record = {
            "geom_id": geom_id,
            "geom_name": geom_name,
            "type": type_name,
            "contype": int(model.geom_contype[geom_id]),
            "conaffinity": int(model.geom_conaffinity[geom_id]),
            "group": int(model.geom_group[geom_id]),
            "size": [float(value) for value in model.geom_size[geom_id]],
        }
        if record["contype"] != 0 or record["conaffinity"] != 0:
            collision_by_body[body_name].append(record)
            collision_geom_count += 1

    limited_joints: list[dict[str, Any]] = []
    for joint_id in range(int(model.njnt)):
        if not bool(model.jnt_limited[joint_id]):
            continue
        joint_name = object_name(
            mujoco, model, mujoco.mjtObj.mjOBJ_JOINT, joint_id
        )
        limited_joints.append(
            {
                "joint_id": joint_id,
                "joint_name": joint_name,
                "joint_type": mujoco.mjtJoint(int(model.jnt_type[joint_id])).name,
                "qpos_address": int(model.jnt_qposadr[joint_id]),
                "range": [
                    float(model.jnt_range[joint_id][0]),
                    float(model.jnt_range[joint_id][1]),
                ],
                "margin": float(model.jnt_margin[joint_id]),
            }
        )
    return {
        "counts": {
            "nbody": int(model.nbody),
            "njnt": int(model.njnt),
            "ngeom": int(model.ngeom),
            "nu": int(model.nu),
            "neq": int(model.neq),
            "nq": int(model.nq),
            "nv": int(model.nv),
        },
        "geom_type_counts": dict(sorted(geom_type_counts.items())),
        "collision_geom_count": collision_geom_count,
        "collision_body_count": len(collision_by_body),
        "collision_by_body": dict(sorted(collision_by_body.items())),
        "limited_joint_count": len(limited_joints),
        "limited_joints": limited_joints,
    }


def run_neutral_audit(
    mujoco: Any,
    model: Any,
    steps: int,
) -> dict[str, Any]:
    if steps <= 0:
        raise CollisionAuditError("steps must be positive")
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    pair_steps: Counter[tuple[str, str]] = Counter()
    pair_maximums: dict[tuple[str, str], dict[str, float]] = {}
    max_contacts = 0
    max_penetration = 0.0
    max_normal_force = 0.0
    max_tangent_force = 0.0
    step_times: list[float] = []
    contact_force = [0.0] * 6
    for _ in range(steps):
        start = time.perf_counter_ns()
        mujoco.mj_step(model, data)
        step_times.append((time.perf_counter_ns() - start) / 1000.0)
        max_contacts = max(max_contacts, int(data.ncon))
        for contact_index in range(int(data.ncon)):
            contact = data.contact[contact_index]
            geom1 = object_name(
                mujoco, model, mujoco.mjtObj.mjOBJ_GEOM, int(contact.geom1)
            )
            geom2 = object_name(
                mujoco, model, mujoco.mjtObj.mjOBJ_GEOM, int(contact.geom2)
            )
            pair = tuple(sorted((geom1, geom2)))
            pair_steps[pair] += 1
            penetration = max(0.0, -float(contact.dist))
            mujoco.mj_contactForce(model, data, contact_index, contact_force)
            normal = abs(float(contact_force[0]))
            tangent = math.hypot(
                float(contact_force[1]), float(contact_force[2])
            )
            maxima = pair_maximums.setdefault(
                pair,
                {
                    "maximum_penetration_metres": 0.0,
                    "maximum_normal_force_newtons": 0.0,
                    "maximum_tangent_force_newtons": 0.0,
                },
            )
            maxima["maximum_penetration_metres"] = max(
                maxima["maximum_penetration_metres"], penetration
            )
            maxima["maximum_normal_force_newtons"] = max(
                maxima["maximum_normal_force_newtons"], normal
            )
            maxima["maximum_tangent_force_newtons"] = max(
                maxima["maximum_tangent_force_newtons"], tangent
            )
            max_penetration = max(max_penetration, penetration)
            max_normal_force = max(max_normal_force, normal)
            max_tangent_force = max(max_tangent_force, tangent)

    if not all(math.isfinite(float(value)) for value in data.qpos):
        raise CollisionAuditError("neutral audit produced non-finite qpos")
    if not all(math.isfinite(float(value)) for value in data.qvel):
        raise CollisionAuditError("neutral audit produced non-finite qvel")
    sorted_times = sorted(step_times)
    p95_index = min(
        len(sorted_times) - 1,
        math.ceil(0.95 * len(sorted_times)) - 1,
    )
    simulated_seconds = steps * float(model.opt.timestep)
    elapsed_seconds = sum(step_times) / 1_000_000.0
    return {
        "steps": steps,
        "simulated_seconds": simulated_seconds,
        "elapsed_seconds": elapsed_seconds,
        "realtime_factor": (
            simulated_seconds / elapsed_seconds if elapsed_seconds > 0.0 else 0.0
        ),
        "median_step_microseconds": sorted_times[len(sorted_times) // 2],
        "p95_step_microseconds": sorted_times[p95_index],
        "maximum_step_microseconds": max(sorted_times),
        "warning_count": warning_count(data),
        "maximum_contact_count": max_contacts,
        "maximum_penetration_metres": max_penetration,
        "maximum_normal_force_newtons": max_normal_force,
        "maximum_tangent_force_newtons": max_tangent_force,
        "contact_pairs": [
            {
                "pair": list(pair),
                "contact_steps": count,
                **pair_maximums[pair],
            }
            for pair, count in pair_steps.most_common()
        ],
        "finite_qpos": True,
        "finite_qvel": True,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expected-sha256")
    parser.add_argument("--steps", type=int, default=5000)
    parser.add_argument("--contract", default="rma065_collision_audit_v1")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        import mujoco
    except ImportError as exc:
        raise CollisionAuditError("mujoco is not installed") from exc

    model_path = args.model.resolve()
    if not model_path.is_file():
        raise CollisionAuditError(f"model does not exist: {model_path}")
    digest = file_sha256(model_path)
    if args.expected_sha256 and digest != args.expected_sha256:
        raise CollisionAuditError(
            "model SHA-256 mismatch: "
            f"expected {args.expected_sha256}, found {digest}"
        )

    model = mujoco.MjModel.from_xml_path(str(model_path))
    report = {
        "contract": args.contract,
        "model_path": model_path.name,
        "model_sha256": digest,
        "mujoco_version": mujoco.__version__,
        "source_inventory": source_inventory(model_path),
        "compiled_inventory": compiled_inventory(mujoco, model),
        "neutral_audit": run_neutral_audit(mujoco, model, args.steps),
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
