#!/usr/bin/env python3
"""Generate the RMA-065 collision and hard-stop Reachy Mini MJCF profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROFILE = ROOT / "models/reachy-mini/collision-hard-stop-baseline.json"


class CollisionProfileError(RuntimeError):
    """Raised when the collision profile or source model violates the contract."""


def read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise CollisionProfileError(f"Cannot read JSON {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise CollisionProfileError(f"JSON root must be an object: {path}")
    return value


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_number(value: object, label: str, *, positive: bool = False) -> float:
    if not isinstance(value, int | float) or isinstance(value, bool):
        raise CollisionProfileError(f"{label} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise CollisionProfileError(f"{label} must be finite")
    if positive and number <= 0.0:
        raise CollisionProfileError(f"{label} must be positive")
    return number


def require_vector(value: object, length: int, label: str) -> list[float]:
    if not isinstance(value, list) or len(value) != length:
        raise CollisionProfileError(f"{label} must contain {length} values")
    return [require_number(item, f"{label}[{index}]") for index, item in enumerate(value)]


def format_number(value: float) -> str:
    if value == 0.0:
        return "0"
    return format(value, ".17g")


def format_vector(values: list[float]) -> str:
    return " ".join(format_number(value) for value in values)


def validate_profile(profile: dict[str, Any]) -> None:
    if profile.get("schema_version") != 1:
        raise CollisionProfileError("Unsupported collision profile schema")
    if profile.get("contract") != "rma065_collision_hard_stop_v1":
        raise CollisionProfileError("Unexpected collision profile contract")
    source = profile.get("source")
    if not isinstance(source, dict):
        raise CollisionProfileError("source must be an object")
    required_source = {
        "repository",
        "commit",
        "model_path",
        "model_sha256",
        "mujoco_version",
    }
    if set(source) != required_source:
        raise CollisionProfileError("source has an unexpected key set")
    if not all(isinstance(source[key], str) and source[key] for key in required_source):
        raise CollisionProfileError("source fields must be nonempty strings")
    if len(source["model_sha256"]) != 64:
        raise CollisionProfileError("source.model_sha256 must be a SHA-256 string")

    fidelity = profile.get("fidelity")
    if not isinstance(fidelity, dict):
        raise CollisionProfileError("fidelity must be an object")
    if fidelity.get("classification") != "engineering_estimate":
        raise CollisionProfileError("collision baseline must be an engineering estimate")
    if fidelity.get("calibrated") is not False:
        raise CollisionProfileError("collision baseline cannot claim calibration")
    if not isinstance(fidelity.get("evidence_id"), str) or not fidelity["evidence_id"]:
        raise CollisionProfileError("fidelity evidence is required")

    masks = profile.get("collision_masks")
    if not isinstance(masks, dict) or set(masks) != {"shell", "moving", "external"}:
        raise CollisionProfileError("collision_masks must define shell, moving, and external")
    for role, mask in masks.items():
        if not isinstance(mask, dict) or set(mask) != {"contype", "conaffinity"}:
            raise CollisionProfileError(f"collision_masks.{role} is invalid")
        for field in ("contype", "conaffinity"):
            value = mask[field]
            if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
                raise CollisionProfileError(f"collision_masks.{role}.{field} must be positive")
    shell = masks["shell"]
    moving = masks["moving"]
    external = masks["external"]
    if not ((shell["contype"] & moving["conaffinity"]) or (moving["contype"] & shell["conaffinity"])):
        raise CollisionProfileError("shell and moving masks must collide")
    if (moving["contype"] & moving["conaffinity"]) != 0:
        raise CollisionProfileError("moving primitives must not collide with one another")
    for role in (shell, moving):
        if not ((role["contype"] & external["conaffinity"]) or (external["contype"] & role["conaffinity"])):
            raise CollisionProfileError("external mask must collide with every robot role")

    contacts = profile.get("contact_parameters")
    if not isinstance(contacts, dict):
        raise CollisionProfileError("contact_parameters must be an object")
    require_vector(contacts.get("friction"), 3, "contact_parameters.friction")
    require_vector(contacts.get("solref"), 2, "contact_parameters.solref")
    require_vector(contacts.get("solimp"), 5, "contact_parameters.solimp")
    for field in (
        "maximum_penetration_metres",
        "contact_overload_newtons",
        "contact_overload_impulse_newton_seconds",
        "hard_stop_overload_newton_metres",
    ):
        require_number(contacts.get(field), f"contact_parameters.{field}", positive=True)

    excludes = profile.get("collision_excludes")
    if not isinstance(excludes, list) or not excludes:
        raise CollisionProfileError("collision_excludes must be a nonempty array")
    exclude_pairs: set[tuple[str, str]] = set()
    for index, exclusion in enumerate(excludes):
        if not isinstance(exclusion, dict) or set(exclusion) != {
            "body1",
            "body2",
            "evidence_class",
            "evidence_id",
        }:
            raise CollisionProfileError(f"collision_excludes[{index}] has an unexpected key set")
        body1 = exclusion["body1"]
        body2 = exclusion["body2"]
        if not isinstance(body1, str) or not body1:
            raise CollisionProfileError(f"collision_excludes[{index}].body1 is required")
        if not isinstance(body2, str) or not body2 or body1 == body2:
            raise CollisionProfileError(f"collision_excludes[{index}].body2 is invalid")
        if exclusion["evidence_class"] != "pinned_topology":
            raise CollisionProfileError(
                f"collision_excludes[{index}] must be justified by pinned topology"
            )
        if not isinstance(exclusion["evidence_id"], str) or not exclusion["evidence_id"]:
            raise CollisionProfileError(f"collision_excludes[{index}] evidence is required")
        pair = tuple(sorted((body1, body2)))
        if pair in exclude_pairs:
            raise CollisionProfileError(f"collision_excludes[{index}] duplicates {pair}")
        exclude_pairs.add(pair)

    shapes = profile.get("shapes")
    if not isinstance(shapes, list) or not shapes:
        raise CollisionProfileError("shapes must be a nonempty array")
    names: set[str] = set()
    covered_bodies: set[str] = set()
    for index, shape in enumerate(shapes):
        if not isinstance(shape, dict):
            raise CollisionProfileError(f"shapes[{index}] must be an object")
        name = shape.get("name")
        body = shape.get("body")
        role = shape.get("role")
        shape_type = shape.get("type")
        if not isinstance(name, str) or not name or name in names:
            raise CollisionProfileError(f"shapes[{index}].name is missing or duplicate")
        if not isinstance(body, str) or not body:
            raise CollisionProfileError(f"shapes[{index}].body is required")
        if role not in {"shell", "moving"}:
            raise CollisionProfileError(f"shapes[{index}].role is invalid")
        if shape_type not in {"capsule", "cylinder", "ellipsoid", "sphere", "box"}:
            raise CollisionProfileError(f"shapes[{index}].type is invalid")
        if shape.get("evidence_class") != "engineering_estimate":
            raise CollisionProfileError(f"shapes[{index}] must be an engineering estimate")
        if not isinstance(shape.get("evidence_id"), str) or not shape["evidence_id"]:
            raise CollisionProfileError(f"shapes[{index}] evidence is required")
        if "pos" in shape:
            require_vector(shape["pos"], 3, f"shapes[{index}].pos")
        if "fromto" in shape:
            require_vector(shape["fromto"], 6, f"shapes[{index}].fromto")
        segment_child = shape.get("segment_child_body")
        segment_site = shape.get("segment_site")
        if segment_child is not None and (not isinstance(segment_child, str) or not segment_child):
            raise CollisionProfileError(f"shapes[{index}].segment_child_body must be a nonempty string")
        if segment_site is not None and (not isinstance(segment_site, str) or not segment_site):
            raise CollisionProfileError(f"shapes[{index}].segment_site must be a nonempty string")
        for field in ("segment_start_inset_metres", "segment_end_inset_metres"):
            if field in shape and require_number(shape[field], f"shapes[{index}].{field}") < 0.0:
                raise CollisionProfileError(f"shapes[{index}].{field} must be nonnegative")
        size = shape.get("size")
        if not isinstance(size, list) or not size:
            raise CollisionProfileError(f"shapes[{index}].size is required")
        if any(require_number(value, f"shapes[{index}].size", positive=True) <= 0 for value in size):
            raise CollisionProfileError(f"shapes[{index}].size must be positive")
        if shape_type == "capsule":
            endpoint_fields = sum(
                field in shape for field in ("fromto", "segment_child_body", "segment_site")
            )
            if len(size) != 1 or endpoint_fields != 1:
                raise CollisionProfileError(
                    f"shapes[{index}] capsule requires exactly one of fromto, "
                    "segment_child_body, or segment_site"
                )
        elif any(
            field in shape
            for field in (
                "segment_child_body",
                "segment_site",
                "segment_start_inset_metres",
                "segment_end_inset_metres",
            )
        ):
            raise CollisionProfileError(
                f"shapes[{index}] segment fields are only valid for capsules"
            )
        expected_sizes = {"cylinder": 2, "ellipsoid": 3, "sphere": 1, "box": 3}
        if shape_type in expected_sizes and len(size) != expected_sizes[shape_type]:
            raise CollisionProfileError(f"shapes[{index}].size has wrong length")
        names.add(name)
        covered_bodies.add(body)

    required_body_groups = [
        {"body_foot_3dprint"},
        {f"dc15_a01_horn_dummy{suffix}" for suffix in ("", "_2", "_3", "_4", "_5", "_6")},
        {f"stewart_link_rod{suffix}" for suffix in ("", "_2", "_3", "_4", "_5", "_6")},
        {"xl_330"},
        {"dc15_a01_horn_dummy_7", "dc15_a01_horn_dummy_8"},
    ]
    for group in required_body_groups:
        missing = group - covered_bodies
        if missing:
            raise CollisionProfileError(f"shapes omit required bodies: {sorted(missing)}")

    hard_stops = profile.get("hard_stops")
    if not isinstance(hard_stops, list) or len(hard_stops) != 9:
        raise CollisionProfileError("hard_stops must contain exactly nine actuated joints")
    joint_names: set[str] = set()
    actuator_names: set[str] = set()
    for index, stop in enumerate(hard_stops):
        if not isinstance(stop, dict):
            raise CollisionProfileError(f"hard_stops[{index}] must be an object")
        joint = stop.get("joint")
        actuator = stop.get("actuator")
        if not isinstance(joint, str) or not joint or joint in joint_names:
            raise CollisionProfileError(f"hard_stops[{index}].joint is missing or duplicate")
        if not isinstance(actuator, str) or not actuator or actuator in actuator_names:
            raise CollisionProfileError(f"hard_stops[{index}].actuator is missing or duplicate")
        source_kind = stop.get("range_source")
        if source_kind not in {"pinned_source", "engineering_estimate"}:
            raise CollisionProfileError(f"hard_stops[{index}].range_source is invalid")
        margin = require_number(stop.get("margin_radians"), f"hard_stops[{index}].margin_radians", positive=True)
        if source_kind == "pinned_source":
            inset = require_number(
                stop.get("soft_limit_inset_radians"),
                f"hard_stops[{index}].soft_limit_inset_radians",
                positive=True,
            )
            if inset <= margin:
                raise CollisionProfileError("soft limit inset must exceed the hard-stop margin")
        else:
            hard_range = require_vector(stop.get("hard_range_radians"), 2, f"hard_stops[{index}].hard_range_radians")
            soft_range = require_vector(stop.get("soft_range_radians"), 2, f"hard_stops[{index}].soft_range_radians")
            if not hard_range[0] < soft_range[0] < soft_range[1] < hard_range[1]:
                raise CollisionProfileError("soft antenna range must lie strictly inside the hard range")
            if not isinstance(stop.get("evidence_id"), str) or not stop["evidence_id"]:
                raise CollisionProfileError("estimated hard stop requires evidence")
        joint_names.add(joint)
        actuator_names.add(actuator)
    expected_actuators = {
        "yaw_body",
        *(f"stewart_{index}" for index in range(1, 7)),
        "right_antenna",
        "left_antenna",
    }
    if actuator_names != expected_actuators or joint_names != expected_actuators:
        raise CollisionProfileError("hard-stop bindings must cover the exact nine actuators")

    solver = profile.get("limit_solver")
    if not isinstance(solver, dict):
        raise CollisionProfileError("limit_solver must be an object")
    require_vector(solver.get("solref"), 2, "limit_solver.solref")
    require_vector(solver.get("solimp"), 5, "limit_solver.solimp")

    budget = profile.get("android_budget")
    if not isinstance(budget, dict) or budget.get("physical_device_required") is not True:
        raise CollisionProfileError("android budget must require a physical device")
    require_number(budget.get("minimum_realtime_factor"), "android_budget.minimum_realtime_factor", positive=True)
    overhead = require_number(
        budget.get("maximum_p95_step_overhead_ratio"),
        "android_budget.maximum_p95_step_overhead_ratio",
    )
    if overhead < 0.0 or overhead > 1.0:
        raise CollisionProfileError("Android overhead ratio must be in [0, 1]")
    steps = budget.get("benchmark_steps")
    if not isinstance(steps, int) or isinstance(steps, bool) or steps < 1000:
        raise CollisionProfileError("Android benchmark_steps must be an integer >= 1000")


def exact_named_elements(root: ET.Element, tag: str) -> dict[str, ET.Element]:
    result: dict[str, ET.Element] = {}
    for element in root.findall(f".//{tag}"):
        name = element.get("name")
        if not name:
            continue
        if name in result:
            raise CollisionProfileError(f"source contains duplicate {tag} name {name}")
        result[name] = element
    return result


def add_numeric(custom: ET.Element, name: str, value: float) -> None:
    existing = [item for item in custom.findall("numeric") if item.get("name") == name]
    if existing:
        raise CollisionProfileError(f"source already defines custom numeric {name}")
    ET.SubElement(custom, "numeric", {"name": name, "data": format_number(value)})


def parse_vector_attribute(element: ET.Element, attribute: str, label: str) -> list[float]:
    text = element.get(attribute)
    if text is None:
        raise CollisionProfileError(f"{label} has no {attribute} attribute")
    try:
        values = [float(value) for value in text.split()]
    except ValueError as exc:
        raise CollisionProfileError(f"{label}.{attribute} is not numeric") from exc
    if len(values) != 3 or any(not math.isfinite(value) for value in values):
        raise CollisionProfileError(f"{label}.{attribute} must contain three finite values")
    return values


def resolve_segment_fromto(shape: dict[str, Any], body: ET.Element) -> list[float]:
    if "fromto" in shape:
        return [float(value) for value in shape["fromto"]]
    child_name = shape.get("segment_child_body")
    site_name = shape.get("segment_site")
    if isinstance(child_name, str) and child_name:
        direct_children = [
            child for child in body.findall("body") if child.get("name") == child_name
        ]
        if len(direct_children) != 1:
            raise CollisionProfileError(
                f"shape {shape['name']} expected one direct child body {child_name}, "
                f"found {len(direct_children)}"
            )
        endpoint_element = direct_children[0]
        endpoint_label = f"body {child_name}"
    elif isinstance(site_name, str) and site_name:
        direct_sites = [
            site for site in body.findall("site") if site.get("name") == site_name
        ]
        if len(direct_sites) != 1:
            raise CollisionProfileError(
                f"shape {shape['name']} expected one direct site {site_name}, "
                f"found {len(direct_sites)}"
            )
        endpoint_element = direct_sites[0]
        endpoint_label = f"site {site_name}"
    else:
        raise CollisionProfileError(f"shape {shape['name']} has no segment endpoint")
    endpoint = parse_vector_attribute(endpoint_element, "pos", endpoint_label)
    length = math.sqrt(sum(value * value for value in endpoint))
    if not math.isfinite(length) or length <= 0.0:
        raise CollisionProfileError(f"shape {shape['name']} segment has zero length")
    start_inset = float(shape.get("segment_start_inset_metres", 0.0))
    end_inset = float(shape.get("segment_end_inset_metres", 0.0))
    if start_inset < 0.0 or end_inset < 0.0:
        raise CollisionProfileError(f"shape {shape['name']} segment insets must be nonnegative")
    if start_inset + end_inset >= length:
        raise CollisionProfileError(
            f"shape {shape['name']} segment insets consume the complete segment"
        )
    unit = [value / length for value in endpoint]
    start = [value * start_inset for value in unit]
    end = [endpoint[index] - unit[index] * end_inset for index in range(3)]
    return start + end


def transform_tree(profile: dict[str, Any], source_path: Path) -> ET.ElementTree:
    tree = ET.parse(source_path)
    root = tree.getroot()
    bodies = exact_named_elements(root, "body")
    joints = exact_named_elements(root, "joint")
    actuators: dict[str, ET.Element] = {}
    actuator_root = root.find("actuator")
    if actuator_root is None:
        raise CollisionProfileError("source model has no actuator section")
    for actuator in list(actuator_root):
        name = actuator.get("name")
        if not name:
            continue
        if name in actuators:
            raise CollisionProfileError(f"source contains duplicate actuator {name}")
        actuators[name] = actuator

    root.set("model", "reachy_mini_rma065_collision_hard_stop")
    masks = profile["collision_masks"]
    contacts = profile["contact_parameters"]
    existing_role = profile["existing_collision_role"]
    existing_mask = masks[existing_role]
    for geom in root.findall(".//geom"):
        is_collision = (
            geom.get("class") == "collision"
            or geom.get("contype") not in (None, "0")
            or geom.get("conaffinity") not in (None, "0")
        )
        if not is_collision:
            continue
        geom.set("contype", str(existing_mask["contype"]))
        geom.set("conaffinity", str(existing_mask["conaffinity"]))
        geom.set("friction", format_vector(contacts["friction"]))
        geom.set("solref", format_vector(contacts["solref"]))
        geom.set("solimp", format_vector(contacts["solimp"]))

    for shape in profile["shapes"]:
        body_name = shape["body"]
        body = bodies.get(body_name)
        if body is None:
            raise CollisionProfileError(f"source model is missing body {body_name}")
        if root.find(f".//geom[@name='{shape['name']}']") is not None:
            raise CollisionProfileError(f"source already contains generated geom {shape['name']}")
        mask = masks[shape["role"]]
        attributes = {
            "name": shape["name"],
            "type": shape["type"],
            "size": format_vector(shape["size"]),
            "contype": str(mask["contype"]),
            "conaffinity": str(mask["conaffinity"]),
            "group": "3",
            "rgba": "0.8 0.2 0.2 0.15",
            "friction": format_vector(contacts["friction"]),
            "solref": format_vector(contacts["solref"]),
            "solimp": format_vector(contacts["solimp"]),
        }
        if "pos" in shape:
            attributes["pos"] = format_vector(shape["pos"])
        if shape["type"] == "capsule":
            attributes["fromto"] = format_vector(resolve_segment_fromto(shape, body))
        ET.SubElement(body, "geom", attributes)

    contact = root.find("contact")
    if contact is None:
        contact = ET.SubElement(root, "contact")
    existing_excludes = {
        tuple(sorted((item.get("body1", ""), item.get("body2", ""))))
        for item in contact.findall("exclude")
    }
    for index, exclusion in enumerate(profile["collision_excludes"]):
        body1 = exclusion["body1"]
        body2 = exclusion["body2"]
        if body1 not in bodies:
            raise CollisionProfileError(
                f"collision_excludes[{index}] references missing body {body1}"
            )
        if body2 not in bodies:
            raise CollisionProfileError(
                f"collision_excludes[{index}] references missing body {body2}"
            )
        pair = tuple(sorted((body1, body2)))
        if pair in existing_excludes:
            raise CollisionProfileError(
                f"source already excludes collision pair {pair}"
            )
        ET.SubElement(contact, "exclude", {"body1": body1, "body2": body2})
        existing_excludes.add(pair)

    solver = profile["limit_solver"]
    hard_stop_records: list[dict[str, Any]] = []
    for stop in profile["hard_stops"]:
        joint = joints.get(stop["joint"])
        actuator = actuators.get(stop["actuator"])
        if joint is None:
            raise CollisionProfileError(f"source model is missing joint {stop['joint']}")
        if actuator is None:
            raise CollisionProfileError(f"source model is missing actuator {stop['actuator']}")
        if stop["range_source"] == "pinned_source":
            source_range_text = joint.get("range")
            if source_range_text is None:
                raise CollisionProfileError(f"source joint {stop['joint']} has no pinned range")
            source_range = [float(value) for value in source_range_text.split()]
            if len(source_range) != 2 or not source_range[0] < source_range[1]:
                raise CollisionProfileError(f"source joint {stop['joint']} range is invalid")
            hard_range = source_range
            inset = float(stop["soft_limit_inset_radians"])
            soft_range = [hard_range[0] + inset, hard_range[1] - inset]
        else:
            hard_range = [float(value) for value in stop["hard_range_radians"]]
            soft_range = [float(value) for value in stop["soft_range_radians"]]
        if not hard_range[0] < soft_range[0] < soft_range[1] < hard_range[1]:
            raise CollisionProfileError(f"soft range is not inside hard range for {stop['joint']}")
        joint.set("limited", "true")
        joint.set("range", format_vector(hard_range))
        joint.set("margin", format_number(float(stop["margin_radians"])))
        joint.set("solreflimit", format_vector(solver["solref"]))
        joint.set("solimplimit", format_vector(solver["solimp"]))
        actuator.attrib.pop("inheritrange", None)
        actuator.set("ctrllimited", "true")
        actuator.set("ctrlrange", format_vector(soft_range))
        hard_stop_records.append(
            {
                "joint": stop["joint"],
                "actuator": stop["actuator"],
                "hard_range_radians": hard_range,
                "soft_range_radians": soft_range,
                "margin_radians": float(stop["margin_radians"]),
                "range_source": stop["range_source"],
            }
        )

    custom = root.find("custom")
    if custom is None:
        custom = ET.SubElement(root, "custom")
    numeric_values = {
        "rma065_contact_overload_newtons": contacts["contact_overload_newtons"],
        "rma065_contact_overload_impulse_newton_seconds": contacts[
            "contact_overload_impulse_newton_seconds"
        ],
        "rma065_hard_stop_overload_newton_metres": contacts[
            "hard_stop_overload_newton_metres"
        ],
        "rma065_maximum_penetration_metres": contacts[
            "maximum_penetration_metres"
        ],
    }
    for name, value in numeric_values.items():
        add_numeric(custom, name, float(value))
    custom.append(
        ET.Comment(
            "RMA-065 values are engineering estimates, not calibrated physical measurements."
        )
    )
    setattr(tree, "rma065_hard_stop_records", hard_stop_records)
    return tree


def serialized_tree(tree: ET.ElementTree) -> bytes:
    ET.indent(tree, space="  ")
    return ET.tostring(tree.getroot(), encoding="utf-8", xml_declaration=True) + b"\n"


def generate(
    profile_path: Path,
    source_path: Path,
    output_path: Path,
    metadata_path: Path,
    *,
    check: bool,
) -> dict[str, Any]:
    profile = read_json(profile_path)
    validate_profile(profile)
    source_hash = file_sha256(source_path)
    expected_hash = profile["source"]["model_sha256"]
    if source_hash != expected_hash:
        raise CollisionProfileError(
            f"source model SHA-256 mismatch: expected {expected_hash}, found {source_hash}"
        )
    tree = transform_tree(profile, source_path)
    output_bytes = serialized_tree(tree)
    output_hash = sha256_bytes(output_bytes)
    profile_hash = file_sha256(profile_path)
    metadata = {
        "schema_version": 1,
        "contract": profile["contract"],
        "source_model_sha256": source_hash,
        "profile_sha256": profile_hash,
        "generated_model_sha256": output_hash,
        "calibrated": False,
        "fidelity_classification": profile["fidelity"]["classification"],
        "added_collision_geoms": [shape["name"] for shape in profile["shapes"]],
        "collision_excludes": profile["collision_excludes"],
        "hard_stops": getattr(tree, "rma065_hard_stop_records"),
        "contact_parameters": profile["contact_parameters"],
        "android_budget": profile["android_budget"],
    }
    metadata_bytes = (
        json.dumps(metadata, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")
    if check:
        if not output_path.is_file() or output_path.read_bytes() != output_bytes:
            raise CollisionProfileError(f"generated model is stale: {output_path}")
        if not metadata_path.is_file() or metadata_path.read_bytes() != metadata_bytes:
            raise CollisionProfileError(f"generated metadata is stale: {metadata_path}")
    else:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_bytes(output_bytes)
        metadata_path.write_bytes(metadata_bytes)
    return metadata


def copy_model_package(source_model: Path, output_model: Path) -> None:
    source_root = source_model.parent
    output_root = output_model.parent
    output_root.mkdir(parents=True, exist_ok=True)
    for path in source_root.rglob("*"):
        if not path.is_file() or path == source_model:
            continue
        relative = path.relative_to(source_root)
        destination = output_root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", type=Path, default=DEFAULT_PROFILE)
    parser.add_argument("--source-model", type=Path, required=True)
    parser.add_argument("--output-model", type=Path, required=True)
    parser.add_argument("--metadata", type=Path, required=True)
    parser.add_argument("--copy-package", action="store_true")
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if args.copy_package and not args.check:
            copy_model_package(args.source_model.resolve(), args.output_model.resolve())
        metadata = generate(
            args.profile.resolve(),
            args.source_model.resolve(),
            args.output_model.resolve(),
            args.metadata.resolve(),
            check=args.check,
        )
    except (CollisionProfileError, OSError, ET.ParseError, ValueError) as exc:
        print(f"RMA-065 collision model generation failed: {exc}", file=__import__("sys").stderr)
        return 1
    print(
        "RMA-065 collision model is current: "
        f"{metadata['generated_model_sha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
