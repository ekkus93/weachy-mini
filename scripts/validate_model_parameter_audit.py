#!/usr/bin/env python3
"""Validate the Reachy model parameter audit against pinned source contracts."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ALLOWED_CLASSIFICATIONS = {
    "cad_derived",
    "upstream_approximation",
    "manufacturer_specification",
    "measured",
    "fitted",
    "placeholder",
}
REQUIRED_UNCERTAINTY_NOTES = {
    "probably wrong, would need to re-identify",
    "Confident that this is realistic (mini duck walks with these values)",
    "Estimation based on the gear ratio difference",
    "Collision models defualt: coarse - there is also fine (but much more detailed models)",
}
FLOAT_TOLERANCE = 1e-12


class AuditValidationError(RuntimeError):
    """Raised when model-fidelity evidence is missing or inconsistent."""


def read_json(path: Path) -> dict[str, Any]:
    """Read a JSON object with a useful error."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise AuditValidationError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise AuditValidationError(f"JSON root must be an object: {path}")
    return value


def require_dict(value: object, label: str) -> dict[str, Any]:
    """Return a required object value."""
    if not isinstance(value, dict):
        raise AuditValidationError(f"{label} must be an object")
    return value


def require_list(value: object, label: str) -> list[Any]:
    """Return a required array value."""
    if not isinstance(value, list):
        raise AuditValidationError(f"{label} must be an array")
    return value


def require_string(value: object, label: str) -> str:
    """Return a required nonempty string."""
    if not isinstance(value, str) or not value:
        raise AuditValidationError(f"{label} must be a nonempty string")
    return value


def index_named(entries: object, label: str, key: str = "name") -> dict[str, dict[str, Any]]:
    """Index an array of uniquely named objects."""
    indexed: dict[str, dict[str, Any]] = {}
    for position, raw_entry in enumerate(require_list(entries, label)):
        entry = require_dict(raw_entry, f"{label}[{position}]")
        name = require_string(entry.get(key), f"{label}[{position}].{key}")
        if name in indexed:
            raise AuditValidationError(f"{label} contains duplicate {key} {name!r}")
        indexed[name] = entry
    return indexed


def collect_classifications(value: object, path: str = "audit") -> list[tuple[str, str]]:
    """Collect every classification-bearing field recursively."""
    found: list[tuple[str, str]] = []
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = f"{path}.{key}"
            if key == "classification" or key.endswith("_classification"):
                if not isinstance(child, str):
                    raise AuditValidationError(f"{child_path} must be a string")
                found.append((child_path, child))
            found.extend(collect_classifications(child, child_path))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found.extend(collect_classifications(child, f"{path}[{index}]"))
    return found


def require_matching_source(
    audit: dict[str, Any],
    lock: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Require the audit to identify the same immutable source as existing pins."""
    source = require_dict(audit.get("source"), "audit.source")
    baseline_source = require_dict(baseline.get("source"), "baseline.source")
    comparisons = {
        "repository": (source.get("repository"), lock.get("repository")),
        "commit": (source.get("commit"), lock.get("commit")),
        "model path": (source.get("model_path"), lock.get("model_file")),
        "baseline repository": (source.get("repository"), baseline_source.get("repository")),
        "baseline commit": (source.get("commit"), baseline_source.get("commit")),
        "baseline model path": (source.get("model_path"), baseline_source.get("model_path")),
        "model SHA-256": (source.get("model_sha256"), baseline_source.get("model_sha256")),
    }
    for label, (actual, expected) in comparisons.items():
        if actual != expected:
            raise AuditValidationError(
                f"Audit {label} mismatch: expected {expected!r}, found {actual!r}"
            )
    require_string(source.get("generator"), "audit.source.generator")
    require_string(source.get("cad_document"), "audit.source.cad_document")


def require_fidelity_policy(audit: dict[str, Any]) -> None:
    """Prevent placeholder parameters from being presented as calibrated."""
    fidelity = require_dict(audit.get("fidelity"), "audit.fidelity")
    calibrated = fidelity.get("calibrated")
    may_label = fidelity.get("may_be_labeled_calibrated")
    if not isinstance(calibrated, bool) or not isinstance(may_label, bool):
        raise AuditValidationError("Fidelity calibration flags must be booleans")
    if fidelity.get("classification") != "geometric_baseline":
        raise AuditValidationError("Current fidelity classification must be geometric_baseline")
    if fidelity.get("diagnostics_status") != "uncalibrated_upstream_baseline":
        raise AuditValidationError(
            "Current diagnostics status must be uncalibrated_upstream_baseline"
        )

    classifications = collect_classifications(audit)
    invalid = [(path, value) for path, value in classifications if value not in ALLOWED_CLASSIFICATIONS]
    if invalid:
        path, value = invalid[0]
        raise AuditValidationError(f"Unknown parameter classification {value!r} at {path}")
    has_placeholder = any(value == "placeholder" for _, value in classifications)
    if has_placeholder and (calibrated or may_label):
        raise AuditValidationError(
            "Audit contains placeholder parameters but permits a calibrated label"
        )
    if calibrated or may_label:
        raise AuditValidationError("RMA-041 baseline must remain explicitly uncalibrated")


def require_parameter_groups(audit: dict[str, Any]) -> None:
    """Require complete provenance and limitation text for each parameter group."""
    groups = index_named(audit.get("parameter_groups"), "audit.parameter_groups", key="id")
    required_ids = {
        "body_geometry_transforms_mass_and_inertia",
        "explicit_joint_ranges",
        "unbounded_antenna_joints",
        "passive_ball_joint_limits",
        "active_position_actuator_dynamics",
        "loop_closure_solver_parameters",
    }
    if set(groups) != required_ids:
        raise AuditValidationError(
            f"Parameter group set mismatch: expected {sorted(required_ids)}, found {sorted(groups)}"
        )
    for group_id, group in groups.items():
        for field in ("applies_to", "evidence", "limitations"):
            values = require_list(group.get(field), f"parameter group {group_id}.{field}")
            if not values or not all(isinstance(value, str) and value for value in values):
                raise AuditValidationError(
                    f"Parameter group {group_id}.{field} must contain nonempty strings"
                )


def require_joint_and_actuator_inventory(audit: dict[str, Any], lock: dict[str, Any]) -> None:
    """Require all pinned joints and active actuators to be classified."""
    requirements = require_dict(lock.get("model_requirements"), "lock.model_requirements")
    required_joint_types = require_dict(
        requirements.get("required_joint_types"),
        "lock.model_requirements.required_joint_types",
    )
    required_actuators = require_dict(
        requirements.get("required_actuator_joints"),
        "lock.model_requirements.required_actuator_joints",
    )
    joints = index_named(audit.get("joints"), "audit.joints")
    actuators = index_named(audit.get("actuators"), "audit.actuators")
    if set(joints) != set(required_joint_types):
        raise AuditValidationError(
            f"Audit joint set mismatch: expected {sorted(required_joint_types)}, found {sorted(joints)}"
        )
    if set(actuators) != set(required_actuators):
        raise AuditValidationError(
            "Audit actuator set does not match the pinned active actuator map"
        )

    for name, expected_type in required_joint_types.items():
        joint = joints[name]
        if joint.get("type") != expected_type:
            raise AuditValidationError(
                f"Joint {name} type mismatch: expected {expected_type!r}, found {joint.get('type')!r}"
            )
        expected_actuated = name in set(required_actuators.values())
        if joint.get("actuated") is not expected_actuated:
            raise AuditValidationError(f"Joint {name} has an incorrect actuated flag")
        joint_range = joint.get("range_radians")
        if joint_range is not None:
            values = require_list(joint_range, f"joint {name}.range_radians")
            if len(values) != 2 or not all(
                isinstance(value, int | float) and not isinstance(value, bool) for value in values
            ):
                raise AuditValidationError(f"Joint {name} range must contain two numbers")
            if float(values[0]) >= float(values[1]):
                raise AuditValidationError(f"Joint {name} range must be increasing")

    for name, expected_joint in required_actuators.items():
        actuator = actuators[name]
        if actuator.get("joint") != expected_joint:
            raise AuditValidationError(
                f"Actuator {name} joint mismatch: expected {expected_joint!r}, "
                f"found {actuator.get('joint')!r}"
            )
        if actuator.get("source_class") != "chosen_actuator":
            raise AuditValidationError(f"Actuator {name} must record chosen_actuator source class")
        if actuator.get("classification") != "placeholder":
            raise AuditValidationError(
                f"Active actuator {name} must remain classified as placeholder"
            )


def require_actuator_models(audit: dict[str, Any]) -> None:
    """Require active and retained upstream actuator classes to be audited."""
    models = index_named(audit.get("actuator_models"), "audit.actuator_models", key="id")
    required = {"chosen_actuator", "xc330m288t", "sts3215_345", "sts3215_147"}
    if set(models) != required:
        raise AuditValidationError(
            f"Actuator model set mismatch: expected {sorted(required)}, found {sorted(models)}"
        )
    chosen = models["chosen_actuator"]
    if chosen.get("active_in_pinned_model") is not True:
        raise AuditValidationError("chosen_actuator must be recorded as active")
    if chosen.get("source_default_class") != "perfect_actuator":
        raise AuditValidationError("chosen_actuator must inherit perfect_actuator")
    if chosen.get("classification") != "placeholder":
        raise AuditValidationError("chosen_actuator must be classified as placeholder")
    for name, model in models.items():
        require_dict(model.get("joint"), f"actuator model {name}.joint")
        position = require_dict(model.get("position"), f"actuator model {name}.position")
        force_range = require_list(position.get("forcerange"), f"actuator model {name}.forcerange")
        if len(force_range) != 2:
            raise AuditValidationError(f"Actuator model {name} force range must have two values")
        if name != "chosen_actuator" and model.get("active_in_pinned_model") is not False:
            raise AuditValidationError(f"Inactive candidate actuator model {name} is marked active")


def require_no_unsubstantiated_evidence(audit: dict[str, Any]) -> None:
    """Require unsupported evidence categories to remain empty."""
    evidence_absent = require_dict(audit.get("evidence_absent"), "audit.evidence_absent")
    for category in (
        "manufacturer_specification",
        "measured",
        "fitted",
        "calibrated_profiles",
    ):
        values = require_list(evidence_absent.get(category), f"evidence_absent.{category}")
        if values:
            raise AuditValidationError(
                f"Current baseline cannot claim {category} evidence without a later audited profile"
            )
    notes = set(require_list(audit.get("uncertainty_notes"), "audit.uncertainty_notes"))
    if notes != REQUIRED_UNCERTAINTY_NOTES:
        raise AuditValidationError(
            f"Uncertainty note set mismatch: expected {sorted(REQUIRED_UNCERTAINTY_NOTES)}, "
            f"found {sorted(notes)}"
        )


def validate_static_audit(
    audit: dict[str, Any],
    lock: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Validate audit structure without requiring the upstream checkout."""
    if audit.get("schema_version") != 1:
        raise AuditValidationError(
            f"Unsupported audit schema version: {audit.get('schema_version')!r}"
        )
    require_matching_source(audit, lock, baseline)
    require_fidelity_policy(audit)
    require_parameter_groups(audit)
    require_joint_and_actuator_inventory(audit, lock)
    require_actuator_models(audit)
    require_no_unsubstantiated_evidence(audit)
    equality = require_dict(audit.get("equality_solver"), "audit.equality_solver")
    expected_equalities = require_dict(
        require_dict(lock.get("model_requirements"), "lock.model_requirements").get(
            "exact_counts"
        ),
        "lock.model_requirements.exact_counts",
    ).get("equalities")
    if equality.get("count") != expected_equalities:
        raise AuditValidationError(
            f"Equality count mismatch: expected {expected_equalities}, found {equality.get('count')}"
        )


def parse_numeric_attribute(value: str | None, label: str) -> list[float]:
    """Parse a whitespace-separated numeric XML attribute."""
    if value is None:
        raise AuditValidationError(f"Pinned model is missing numeric attribute {label}")
    try:
        return [float(part) for part in value.split()]
    except ValueError as exc:
        raise AuditValidationError(f"Pinned model has invalid numeric attribute {label}") from exc


def require_close_values(actual: list[float], expected: object, label: str) -> None:
    """Require numeric arrays to match the audited source values."""
    expected_values = require_list(expected, label)
    if len(actual) != len(expected_values):
        raise AuditValidationError(
            f"{label} length mismatch: expected {len(expected_values)}, found {len(actual)}"
        )
    for index, (actual_value, expected_value) in enumerate(zip(actual, expected_values, strict=True)):
        if not isinstance(expected_value, int | float) or isinstance(expected_value, bool):
            raise AuditValidationError(f"{label}[{index}] must be numeric")
        if abs(actual_value - float(expected_value)) > FLOAT_TOLERANCE:
            raise AuditValidationError(
                f"{label}[{index}] mismatch: expected {expected_value}, found {actual_value}"
            )


def direct_child(parent: ET.Element, tag: str, label: str) -> ET.Element:
    """Return one required direct child element."""
    child = parent.find(tag)
    if child is None:
        raise AuditValidationError(f"Pinned model is missing {label}")
    return child


def validate_model_actuator_defaults(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare audited actuator constants with the pinned MJCF defaults."""
    defaults = {
        element.get("class"): element
        for element in root.findall(".//default[@class]")
        if element.get("class")
    }
    models = index_named(audit.get("actuator_models"), "audit.actuator_models", key="id")
    for model_id, model in models.items():
        source_class = require_string(
            model.get("source_default_class"),
            f"actuator model {model_id}.source_default_class",
        )
        source_default = defaults.get(source_class)
        if source_default is None:
            raise AuditValidationError(
                f"Pinned model is missing actuator default class {source_class!r}"
            )
        if model_id == "chosen_actuator":
            chosen = source_default.find("default[@class='chosen_actuator']")
            if chosen is None:
                raise AuditValidationError(
                    "Pinned model no longer nests chosen_actuator under perfect_actuator"
                )
        joint_element = direct_child(
            source_default,
            "joint",
            f"{source_class} joint defaults",
        )
        position_element = direct_child(
            source_default,
            "position",
            f"{source_class} position defaults",
        )
        joint = require_dict(model.get("joint"), f"actuator model {model_id}.joint")
        position = require_dict(model.get("position"), f"actuator model {model_id}.position")
        for attribute in ("damping", "frictionloss", "armature"):
            require_close_values(
                parse_numeric_attribute(
                    joint_element.get(attribute),
                    f"{source_class}.joint.{attribute}",
                ),
                [joint.get(attribute)],
                f"actuator model {model_id}.joint.{attribute}",
            )
        for attribute in ("kp", "kv"):
            require_close_values(
                parse_numeric_attribute(
                    position_element.get(attribute),
                    f"{source_class}.position.{attribute}",
                ),
                [position.get(attribute)],
                f"actuator model {model_id}.position.{attribute}",
            )
        require_close_values(
            parse_numeric_attribute(
                position_element.get("forcerange"),
                f"{source_class}.position.forcerange",
            ),
            position.get("forcerange"),
            f"actuator model {model_id}.position.forcerange",
        )


def validate_model_joints_and_actuators(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare joint types/ranges and active actuator mappings with the pinned MJCF."""
    source_joints = {
        element.get("name"): element
        for element in root.findall(".//joint[@name]")
        if element.get("name")
    }
    audit_joints = index_named(audit.get("joints"), "audit.joints")
    if set(source_joints) != set(audit_joints):
        raise AuditValidationError("Pinned model joint set differs from the parameter audit")
    for name, audited in audit_joints.items():
        source = source_joints[name]
        if source.get("type") != audited.get("type"):
            raise AuditValidationError(f"Pinned model joint {name} type differs from audit")
        source_range = source.get("range")
        audited_range = audited.get("range_radians")
        if audited_range is None:
            if source_range is not None:
                raise AuditValidationError(
                    f"Pinned model joint {name} gained a range not recorded in the audit"
                )
        else:
            require_close_values(
                parse_numeric_attribute(source_range, f"joint {name}.range"),
                audited_range,
                f"joint {name}.range_radians",
            )

    actuator_parent = root.find("actuator")
    if actuator_parent is None:
        raise AuditValidationError("Pinned model is missing its actuator section")
    source_actuators = {
        element.get("name"): element
        for element in actuator_parent.findall("position[@name]")
        if element.get("name")
    }
    audit_actuators = index_named(audit.get("actuators"), "audit.actuators")
    if set(source_actuators) != set(audit_actuators):
        raise AuditValidationError("Pinned model actuator set differs from the parameter audit")
    for name, audited in audit_actuators.items():
        source = source_actuators[name]
        if source.get("joint") != audited.get("joint"):
            raise AuditValidationError(f"Pinned actuator {name} joint differs from audit")
        if source.get("class") != audited.get("source_class"):
            raise AuditValidationError(f"Pinned actuator {name} class differs from audit")


def validate_model_equalities(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare loop-closure solver settings and count with the audit."""
    equality = require_dict(audit.get("equality_solver"), "audit.equality_solver")
    equality_default = root.find(".//default/equality")
    if equality_default is None:
        raise AuditValidationError("Pinned model is missing equality solver defaults")
    require_close_values(
        parse_numeric_attribute(equality_default.get("solref"), "equality.solref"),
        equality.get("solref"),
        "audit.equality_solver.solref",
    )
    require_close_values(
        parse_numeric_attribute(equality_default.get("solimp"), "equality.solimp"),
        equality.get("solimp"),
        "audit.equality_solver.solimp",
    )
    equality_parent = root.find("equality")
    if equality_parent is None:
        raise AuditValidationError("Pinned model is missing equality constraints")
    connect_count = len(equality_parent.findall("connect"))
    if connect_count != equality.get("count"):
        raise AuditValidationError(
            f"Pinned equality count differs from audit: expected {equality.get('count')}, "
            f"found {connect_count}"
        )


def validate_pinned_model(model_path: Path, audit: dict[str, Any]) -> None:
    """Validate the audit against the exact pinned upstream MJCF."""
    try:
        model_bytes = model_path.read_bytes()
    except OSError as exc:
        raise AuditValidationError(f"Cannot read pinned model {model_path}: {exc}") from exc
    expected_sha = require_dict(audit.get("source"), "audit.source").get("model_sha256")
    actual_sha = hashlib.sha256(model_bytes).hexdigest()
    if actual_sha != expected_sha:
        raise AuditValidationError(
            f"Pinned model SHA-256 mismatch: expected {expected_sha}, found {actual_sha}"
        )
    source_text = model_bytes.decode("utf-8")
    for note in REQUIRED_UNCERTAINTY_NOTES:
        if note not in source_text:
            raise AuditValidationError(
                f"Pinned source no longer contains audited uncertainty note: {note!r}"
            )
    try:
        root = ET.fromstring(model_bytes)
    except ET.ParseError as exc:
        raise AuditValidationError(f"Cannot parse pinned model {model_path}: {exc}") from exc
    validate_model_actuator_defaults(root, audit)
    validate_model_joints_and_actuators(root, audit)
    validate_model_equalities(root, audit)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--model", type=Path)
    return parser.parse_args()


def main() -> int:
    """Validate static audit policy and, when supplied, the pinned MJCF."""
    args = parse_args()
    try:
        audit = read_json(args.audit.resolve())
        lock = read_json(args.lock.resolve())
        baseline = read_json(args.baseline.resolve())
        validate_static_audit(audit, lock, baseline)
        if args.model is not None:
            validate_pinned_model(args.model.resolve(), audit)
    except AuditValidationError as exc:
        print(f"Model parameter audit validation failed: {exc}", file=sys.stderr)
        return 1

    mode = "static-and-model" if args.model is not None else "static"
    print(
        "Model parameter audit validation passed: "
        f"mode={mode} profile={audit['fidelity']['profile_id']} "
        f"diagnostics={audit['fidelity']['diagnostics_status']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
