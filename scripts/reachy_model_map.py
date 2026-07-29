"""Build and validate a deterministic machine-readable map of a Reachy MJCF."""

from __future__ import annotations

import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


class ModelMapError(RuntimeError):
    """Raised when an MJCF cannot produce the required deterministic model map."""


def sha256(path: Path) -> str:
    """Return a file's SHA-256 digest."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_mjcf(path: Path, display_path: str) -> ET.Element:
    """Parse an MJCF root and reject malformed or non-MuJoCo XML."""
    try:
        root = ET.fromstring(path.read_bytes())
    except (OSError, ET.ParseError) as exc:
        raise ModelMapError(f"Cannot parse Reachy MJCF {display_path}: {exc}") from exc
    if root.tag != "mujoco":
        raise ModelMapError(f"MJCF root must be <mujoco>: {display_path}")
    if not root.attrib.get("model"):
        raise ModelMapError(f"MJCF model name is missing: {display_path}")
    if root.find("worldbody") is None:
        raise ModelMapError(f"MJCF worldbody is missing: {display_path}")
    return root


def sorted_attributes(element: ET.Element) -> dict[str, str]:
    """Return XML attributes in deterministic key order."""
    return dict(sorted(element.attrib.items()))


def entity_path(parent_path: str, kind: str, index: int, name: str | None) -> str:
    """Create a stable path for named and anonymous MJCF entities."""
    segment = name if name else f"@{kind}[{index}]"
    return f"{parent_path}/{segment}"


def require_unique_name(
    seen: dict[str, str],
    category: str,
    name: str | None,
    path: str,
) -> None:
    """Reject duplicate nonempty names within an MJCF namespace category."""
    if not name:
        return
    previous = seen.get(name)
    if previous is not None:
        raise ModelMapError(f"Duplicate {category} name {name!r}: {previous} and {path}")
    seen[name] = path


def build_model_map(
    root: ET.Element,
    model_path: Path,
    source_relative_path: str,
) -> dict[str, Any]:
    """Build a deterministic map of bodies, joints, actuators, sites, and cameras."""
    bodies: list[dict[str, Any]] = []
    joints: list[dict[str, Any]] = []
    actuators: list[dict[str, Any]] = []
    equalities: list[dict[str, Any]] = []
    sites: list[dict[str, Any]] = []
    cameras: list[dict[str, Any]] = []
    seen: dict[str, dict[str, str]] = {
        "body": {},
        "joint": {},
        "actuator": {},
        "site": {},
        "camera": {},
        "equality": {},
    }

    def add_site_or_camera(
        element: ET.Element,
        owner_name: str | None,
        owner_path: str,
        index: int,
    ) -> None:
        category = element.tag
        name = element.attrib.get("name")
        path = entity_path(owner_path, category, index, name)
        require_unique_name(seen[category], category, name, path)
        entry = {
            "attributes": sorted_attributes(element),
            "body": owner_name,
            "body_path": owner_path,
            "index": len(sites) if category == "site" else len(cameras),
            "name": name,
            "path": path,
        }
        if category == "site":
            sites.append(entry)
        else:
            cameras.append(entry)

    def walk_body(
        body: ET.Element,
        parent_name: str | None,
        parent_path: str,
        sibling_index: int,
    ) -> None:
        name = body.attrib.get("name")
        path = entity_path(parent_path, "body", sibling_index, name)
        require_unique_name(seen["body"], "body", name, path)
        bodies.append(
            {
                "attributes": sorted_attributes(body),
                "index": len(bodies),
                "name": name,
                "parent": parent_name,
                "parent_path": parent_path,
                "path": path,
            }
        )

        joint_elements = [child for child in body if child.tag in {"joint", "freejoint"}]
        for joint_index, joint in enumerate(joint_elements):
            joint_name = joint.attrib.get("name")
            joint_path = entity_path(path, "joint", joint_index, joint_name)
            require_unique_name(seen["joint"], "joint", joint_name, joint_path)
            joint_type = "free" if joint.tag == "freejoint" else joint.attrib.get("type", "hinge")
            joints.append(
                {
                    "attributes": sorted_attributes(joint),
                    "body": name,
                    "body_path": path,
                    "index": len(joints),
                    "kind": joint.tag,
                    "name": joint_name,
                    "path": joint_path,
                    "type": joint_type,
                }
            )

        for category in ("site", "camera"):
            elements = [child for child in body if child.tag == category]
            for element_index, element in enumerate(elements):
                add_site_or_camera(element, name, path, element_index)

        child_bodies = [child for child in body if child.tag == "body"]
        for child_index, child_body in enumerate(child_bodies):
            walk_body(child_body, name, path, child_index)

    worldbody = root.find("worldbody")
    assert worldbody is not None
    for camera_index, camera in enumerate(worldbody.findall("camera")):
        add_site_or_camera(camera, None, "/world", camera_index)
    for body_index, body in enumerate(worldbody.findall("body")):
        walk_body(body, None, "/world", body_index)

    actuator_root = root.find("actuator")
    if actuator_root is not None:
        for actuator_index, actuator in enumerate(list(actuator_root)):
            name = actuator.attrib.get("name")
            path = entity_path("/actuator", actuator.tag, actuator_index, name)
            require_unique_name(seen["actuator"], "actuator", name, path)
            actuators.append(
                {
                    "attributes": sorted_attributes(actuator),
                    "index": actuator_index,
                    "name": name,
                    "path": path,
                    "type": actuator.tag,
                }
            )

    equality_root = root.find("equality")
    if equality_root is not None:
        for equality_index, equality in enumerate(list(equality_root)):
            name = equality.attrib.get("name")
            path = entity_path("/equality", equality.tag, equality_index, name)
            require_unique_name(seen["equality"], "equality", name, path)
            equalities.append(
                {
                    "attributes": sorted_attributes(equality),
                    "index": equality_index,
                    "name": name,
                    "path": path,
                    "type": equality.tag,
                }
            )

    return {
        "actuators": actuators,
        "bodies": bodies,
        "cameras": cameras,
        "counts": {
            "actuators": len(actuators),
            "bodies": len(bodies),
            "cameras": len(cameras),
            "equalities": len(equalities),
            "joints": len(joints),
            "named_bodies": sum(body["name"] is not None for body in bodies),
            "sites": len(sites),
        },
        "equalities": equalities,
        "joints": joints,
        "model": root.attrib["model"],
        "schema_version": 1,
        "sites": sites,
        "source_model": {
            "path": source_relative_path,
            "sha256": sha256(model_path),
        },
    }


def named_entries(model_map: dict[str, Any], category: str) -> dict[str, dict[str, Any]]:
    """Index named model-map entries by name."""
    return {entry["name"]: entry for entry in model_map[category] if entry["name"] is not None}


def require_expected_subset(
    actual: dict[str, str],
    expected: dict[str, Any],
    label: str,
) -> None:
    """Require an XML attribute dictionary to contain an expected subset."""
    for key, value in expected.items():
        if actual.get(key) != value:
            raise ModelMapError(
                f"{label} attribute {key!r} mismatch: expected {value!r}, found {actual.get(key)!r}"
            )


def validate_model_requirements(
    model_map: dict[str, Any],
    requirements: object,
) -> None:
    """Reject structural drift from the topology contract in the source lock."""
    if not isinstance(requirements, dict):
        raise ModelMapError("model_requirements must be a JSON object")

    expected_model = requirements.get("model_name")
    if model_map["model"] != expected_model:
        raise ModelMapError(
            f"MJCF model mismatch: expected {expected_model!r}, found {model_map['model']!r}"
        )

    exact_counts = requirements.get("exact_counts")
    if not isinstance(exact_counts, dict):
        raise ModelMapError("model_requirements.exact_counts must be an object")
    for category, expected in exact_counts.items():
        actual = model_map["counts"].get(category)
        if not isinstance(expected, int) or isinstance(expected, bool) or expected < 0:
            raise ModelMapError(f"Invalid expected count for {category!r}")
        if actual != expected:
            raise ModelMapError(
                f"MJCF {category} count mismatch: expected {expected}, found {actual}"
            )

    required_names = requirements.get("required_names")
    if not isinstance(required_names, dict):
        raise ModelMapError("model_requirements.required_names must be an object")
    for category, names in required_names.items():
        if category not in {"bodies", "joints", "actuators", "sites", "cameras"}:
            raise ModelMapError(f"Unsupported required-name category: {category!r}")
        if not isinstance(names, list) or any(not isinstance(name, str) for name in names):
            raise ModelMapError(f"Required names for {category!r} must be strings")
        available = named_entries(model_map, category)
        missing = sorted(set(names).difference(available))
        if missing:
            raise ModelMapError(f"MJCF is missing required {category}: {', '.join(missing)}")

    joints = named_entries(model_map, "joints")
    joint_types = requirements.get("required_joint_types", {})
    if not isinstance(joint_types, dict):
        raise ModelMapError("required_joint_types must be an object")
    for name, expected_type in joint_types.items():
        entry = joints.get(name)
        if entry is None:
            raise ModelMapError(f"MJCF is missing required joint {name!r}")
        if entry["type"] != expected_type:
            raise ModelMapError(
                f"Joint {name!r} type mismatch: expected {expected_type!r}, found {entry['type']!r}"
            )

    actuators = named_entries(model_map, "actuators")
    actuator_joints = requirements.get("required_actuator_joints", {})
    if not isinstance(actuator_joints, dict):
        raise ModelMapError("required_actuator_joints must be an object")
    for name, expected_joint in actuator_joints.items():
        entry = actuators.get(name)
        if entry is None:
            raise ModelMapError(f"MJCF is missing required actuator {name!r}")
        require_expected_subset(
            entry["attributes"],
            {"joint": expected_joint},
            f"Actuator {name!r}",
        )

    required_equalities = requirements.get("required_equalities", [])
    if not isinstance(required_equalities, list):
        raise ModelMapError("required_equalities must be an array")
    for expected in required_equalities:
        if not isinstance(expected, dict) or not isinstance(expected.get("type"), str):
            raise ModelMapError("Each required equality must have a string type")
        expected_attributes = expected.get("attributes", {})
        if not isinstance(expected_attributes, dict):
            raise ModelMapError("Required equality attributes must be an object")
        matches = [
            equality
            for equality in model_map["equalities"]
            if equality["type"] == expected["type"]
            and all(
                equality["attributes"].get(key) == value
                for key, value in expected_attributes.items()
            )
        ]
        if not matches:
            raise ModelMapError(
                f"MJCF is missing required {expected['type']} equality "
                f"with attributes {expected_attributes}"
            )

    cameras = named_entries(model_map, "cameras")
    camera_attributes = requirements.get("required_camera_attributes", {})
    if not isinstance(camera_attributes, dict):
        raise ModelMapError("required_camera_attributes must be an object")
    for name, expected in camera_attributes.items():
        if not isinstance(expected, dict):
            raise ModelMapError(f"Required camera attributes for {name!r} must be an object")
        entry = cameras.get(name)
        if entry is None:
            raise ModelMapError(f"MJCF is missing required camera {name!r}")
        require_expected_subset(entry["attributes"], expected, f"Camera {name!r}")


def write_model_map(path: Path, model_map: dict[str, Any]) -> None:
    """Write a deterministic model-map JSON file."""
    path.write_text(
        json.dumps(model_map, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
