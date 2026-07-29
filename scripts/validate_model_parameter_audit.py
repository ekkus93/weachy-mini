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
REQUIRED_GROUPS = {
    "body_geometry_transforms_mass_and_inertia",
    "explicit_joint_ranges",
    "unbounded_antenna_joints",
    "passive_ball_joint_limits",
    "active_position_actuator_dynamics",
    "loop_closure_solver_parameters",
}
REQUIRED_ACTUATOR_MODELS = {
    "chosen_actuator",
    "xc330m288t",
    "sts3215_345",
    "sts3215_147",
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


def index_entries(
    value: object,
    label: str,
    key: str = "name",
) -> dict[str, dict[str, Any]]:
    """Index an array of uniquely named objects."""
    indexed: dict[str, dict[str, Any]] = {}
    for position, raw_entry in enumerate(require_list(value, label)):
        entry = require_dict(raw_entry, f"{label}[{position}]")
        name = require_string(entry.get(key), f"{label}[{position}].{key}")
        if name in indexed:
            raise AuditValidationError(f"{label} contains duplicate {key} {name!r}")
        indexed[name] = entry
    return indexed


def collect_classifications(
    value: object,
    path: str = "audit",
) -> list[tuple[str, str]]:
    """Collect every parameter-classification field recursively."""
    found: list[tuple[str, str]] = []
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = f"{path}.{key}"
            is_parameter_classification = key == "classification" or key.endswith("_classification")
            is_fidelity_level = child_path == "audit.fidelity.classification"
            if is_parameter_classification and not is_fidelity_level:
                if not isinstance(child, str):
                    raise AuditValidationError(f"{child_path} must be a string")
                found.append((child_path, child))
            found.extend(collect_classifications(child, child_path))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found.extend(collect_classifications(child, f"{path}[{index}]"))
    return found


def numeric_list(value: object, label: str, length: int) -> list[float]:
    """Return a fixed-length numeric array."""
    raw_values = require_list(value, label)
    if len(raw_values) != length:
        raise AuditValidationError(f"{label} must contain {length} values")
    values: list[float] = []
    for index, raw_value in enumerate(raw_values):
        if not isinstance(raw_value, int | float) or isinstance(raw_value, bool):
            raise AuditValidationError(f"{label}[{index}] must be numeric")
        values.append(float(raw_value))
    return values


def parse_numbers(value: str | None, label: str) -> list[float]:
    """Parse a whitespace-separated numeric XML attribute."""
    if value is None:
        raise AuditValidationError(f"Pinned model is missing {label}")
    try:
        return [float(part) for part in value.split()]
    except ValueError as exc:
        raise AuditValidationError(f"Pinned model has invalid {label}") from exc


def require_close(actual: list[float], expected: list[float], label: str) -> None:
    """Require two numeric arrays to match within source-rounding tolerance."""
    if len(actual) != len(expected):
        raise AuditValidationError(
            f"{label} length mismatch: expected {len(expected)}, found {len(actual)}"
        )
    for index, (actual_value, expected_value) in enumerate(zip(actual, expected, strict=True)):
        if abs(actual_value - expected_value) > FLOAT_TOLERANCE:
            raise AuditValidationError(
                f"{label}[{index}] mismatch: expected {expected_value}, found {actual_value}"
            )


def validate_source_identity(
    audit: dict[str, Any],
    lock: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Require every source pin to identify the same immutable MJCF."""
    source = require_dict(audit.get("source"), "audit.source")
    baseline_source = require_dict(baseline.get("source"), "baseline.source")
    comparisons = {
        "repository": (source.get("repository"), lock.get("repository")),
        "commit": (source.get("commit"), lock.get("commit")),
        "model path": (source.get("model_path"), lock.get("model_file")),
        "baseline repository": (
            source.get("repository"),
            baseline_source.get("repository"),
        ),
        "baseline commit": (source.get("commit"), baseline_source.get("commit")),
        "baseline model path": (
            source.get("model_path"),
            baseline_source.get("model_path"),
        ),
        "model SHA-256": (
            source.get("model_sha256"),
            baseline_source.get("model_sha256"),
        ),
    }
    for label, (actual, expected) in comparisons.items():
        if actual != expected:
            raise AuditValidationError(
                f"Audit {label} mismatch: expected {expected!r}, found {actual!r}"
            )
    require_string(source.get("generator"), "audit.source.generator")
    require_string(source.get("cad_document"), "audit.source.cad_document")


def validate_fidelity(audit: dict[str, Any]) -> None:
    """Reject unknown evidence classes and false calibration claims."""
    fidelity = require_dict(audit.get("fidelity"), "audit.fidelity")
    calibrated = fidelity.get("calibrated")
    may_label = fidelity.get("may_be_labeled_calibrated")
    if not isinstance(calibrated, bool) or not isinstance(may_label, bool):
        raise AuditValidationError("Fidelity calibration flags must be booleans")
    if fidelity.get("classification") != "geometric_baseline":
        raise AuditValidationError("Fidelity classification must be geometric_baseline")
    if fidelity.get("diagnostics_status") != "uncalibrated_upstream_baseline":
        raise AuditValidationError("Diagnostics status must be uncalibrated_upstream_baseline")

    classifications = collect_classifications(audit)
    for path, classification in classifications:
        if classification not in ALLOWED_CLASSIFICATIONS:
            raise AuditValidationError(
                f"Unknown parameter classification {classification!r} at {path}"
            )
    has_placeholder = any(classification == "placeholder" for _, classification in classifications)
    if has_placeholder and (calibrated or may_label):
        raise AuditValidationError(
            "Audit contains placeholder parameters but permits a calibrated label"
        )
    if calibrated or may_label:
        raise AuditValidationError("RMA-041 baseline must remain uncalibrated")


def validate_groups(audit: dict[str, Any]) -> None:
    """Require each audit group to record scope, evidence, and limitations."""
    groups = index_entries(
        audit.get("parameter_groups"),
        "audit.parameter_groups",
        key="id",
    )
    if set(groups) != REQUIRED_GROUPS:
        raise AuditValidationError(f"Parameter groups differ from required set: {sorted(groups)}")
    for group_id, group in groups.items():
        for field in ("applies_to", "evidence", "limitations"):
            values = require_list(group.get(field), f"group {group_id}.{field}")
            if not values or not all(isinstance(item, str) and item for item in values):
                raise AuditValidationError(
                    f"Group {group_id}.{field} must contain nonempty strings"
                )


def validate_joint_inventory(audit: dict[str, Any], lock: dict[str, Any]) -> None:
    """Require all pinned joints and active actuators to be classified."""
    requirements = require_dict(lock.get("model_requirements"), "lock requirements")
    expected_types = require_dict(
        requirements.get("required_joint_types"),
        "required joint types",
    )
    expected_actuators = require_dict(
        requirements.get("required_actuator_joints"),
        "required actuator joints",
    )
    joints = index_entries(audit.get("joints"), "audit.joints")
    actuators = index_entries(audit.get("actuators"), "audit.actuators")
    if set(joints) != set(expected_types):
        raise AuditValidationError("Audit joint set differs from the pinned contract")
    if set(actuators) != set(expected_actuators):
        raise AuditValidationError("Audit actuator set differs from the pinned contract")

    actuated_joints = set(expected_actuators.values())
    for name, expected_type in expected_types.items():
        joint = joints[name]
        if joint.get("type") != expected_type:
            raise AuditValidationError(f"Joint {name} type differs from the pin")
        if joint.get("actuated") is not (name in actuated_joints):
            raise AuditValidationError(f"Joint {name} has an incorrect actuated flag")
        if joint.get("range_radians") is not None:
            values = numeric_list(joint.get("range_radians"), f"joint {name} range", 2)
            if values[0] >= values[1]:
                raise AuditValidationError(f"Joint {name} range must be increasing")

    for name, expected_joint in expected_actuators.items():
        actuator = actuators[name]
        if actuator.get("joint") != expected_joint:
            raise AuditValidationError(f"Actuator {name} joint differs from the pin")
        if actuator.get("source_class") != "chosen_actuator":
            raise AuditValidationError(f"Actuator {name} must use chosen_actuator")
        if actuator.get("classification") != "placeholder":
            raise AuditValidationError(f"Actuator {name} must remain a placeholder")


def validate_actuator_models(audit: dict[str, Any]) -> None:
    """Require all retained upstream actuator-default classes to be audited."""
    models = index_entries(
        audit.get("actuator_models"),
        "audit.actuator_models",
        key="id",
    )
    if set(models) != REQUIRED_ACTUATOR_MODELS:
        raise AuditValidationError("Audit actuator model set is incomplete")
    chosen = models["chosen_actuator"]
    if chosen.get("active_in_pinned_model") is not True:
        raise AuditValidationError("chosen_actuator must be marked active")
    if chosen.get("source_default_class") != "perfect_actuator":
        raise AuditValidationError("chosen_actuator must inherit perfect_actuator")
    if chosen.get("classification") != "placeholder":
        raise AuditValidationError("chosen_actuator must remain a placeholder")

    for name, model in models.items():
        joint = require_dict(model.get("joint"), f"actuator model {name}.joint")
        position = require_dict(
            model.get("position"),
            f"actuator model {name}.position",
        )
        for attribute in ("damping", "frictionloss", "armature"):
            numeric_list([joint.get(attribute)], f"{name}.joint.{attribute}", 1)
        for attribute in ("kp", "kv"):
            numeric_list([position.get(attribute)], f"{name}.position.{attribute}", 1)
        numeric_list(position.get("forcerange"), f"{name}.forcerange", 2)
        if name != "chosen_actuator" and model.get("active_in_pinned_model") is not False:
            raise AuditValidationError(f"Inactive actuator model {name} is marked active")


def validate_absent_evidence(audit: dict[str, Any]) -> None:
    """Require unsupported evidence categories to remain empty and explicit."""
    absent = require_dict(audit.get("evidence_absent"), "audit.evidence_absent")
    for category in (
        "manufacturer_specification",
        "measured",
        "fitted",
        "calibrated_profiles",
    ):
        if require_list(absent.get(category), f"evidence_absent.{category}"):
            raise AuditValidationError(f"Current baseline cannot claim {category}")
    notes = set(require_list(audit.get("uncertainty_notes"), "uncertainty notes"))
    if notes != REQUIRED_UNCERTAINTY_NOTES:
        raise AuditValidationError("Audit uncertainty notes differ from the pinned set")


def validate_static_audit(
    audit: dict[str, Any],
    lock: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Validate audit policy without requiring an upstream checkout."""
    if audit.get("schema_version") != 1:
        raise AuditValidationError("Unsupported model parameter audit schema")
    validate_source_identity(audit, lock, baseline)
    validate_fidelity(audit)
    validate_groups(audit)
    validate_joint_inventory(audit, lock)
    validate_actuator_models(audit)
    validate_absent_evidence(audit)

    equality = require_dict(audit.get("equality_solver"), "audit.equality_solver")
    exact_counts = require_dict(
        require_dict(lock.get("model_requirements"), "lock requirements").get("exact_counts"),
        "lock exact counts",
    )
    if equality.get("count") != exact_counts.get("equalities"):
        raise AuditValidationError("Audit equality count differs from the pin")
    numeric_list(equality.get("solref"), "equality solref", 2)
    numeric_list(equality.get("solimp"), "equality solimp", 5)


def validate_source_defaults(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare audited actuator constants with upstream default classes."""
    defaults = {
        element.get("class"): element
        for element in root.findall(".//default[@class]")
        if element.get("class")
    }
    models = index_entries(
        audit.get("actuator_models"),
        "audit.actuator_models",
        key="id",
    )
    for model_id, model in models.items():
        source_class = require_string(
            model.get("source_default_class"),
            f"actuator model {model_id}.source_default_class",
        )
        source = defaults.get(source_class)
        if source is None:
            raise AuditValidationError(f"Pinned model lacks class {source_class}")
        if (
            model_id == "chosen_actuator"
            and source.find("default[@class='chosen_actuator']") is None
        ):
            raise AuditValidationError("chosen_actuator no longer inherits perfect_actuator")
        source_joint = source.find("joint")
        source_position = source.find("position")
        if source_joint is None or source_position is None:
            raise AuditValidationError(f"Class {source_class} lacks required defaults")

        audited_joint = require_dict(model.get("joint"), f"{model_id}.joint")
        audited_position = require_dict(model.get("position"), f"{model_id}.position")
        for attribute in ("damping", "frictionloss", "armature"):
            require_close(
                parse_numbers(source_joint.get(attribute), f"{source_class}.{attribute}"),
                numeric_list(
                    [audited_joint.get(attribute)],
                    f"{model_id}.joint.{attribute}",
                    1,
                ),
                f"{model_id}.joint.{attribute}",
            )
        for attribute in ("kp", "kv"):
            require_close(
                parse_numbers(
                    source_position.get(attribute),
                    f"{source_class}.{attribute}",
                ),
                numeric_list(
                    [audited_position.get(attribute)],
                    f"{model_id}.position.{attribute}",
                    1,
                ),
                f"{model_id}.position.{attribute}",
            )
        require_close(
            parse_numbers(
                source_position.get("forcerange"),
                f"{source_class}.forcerange",
            ),
            numeric_list(
                audited_position.get("forcerange"),
                f"{model_id}.forcerange",
                2,
            ),
            f"{model_id}.forcerange",
        )


def validate_source_joints(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare source joint types/ranges and active actuator mappings."""
    source_joints = {
        element.get("name"): element
        for element in root.findall(".//joint[@name]")
        if element.get("name")
    }
    audited_joints = index_entries(audit.get("joints"), "audit.joints")
    if set(source_joints) != set(audited_joints):
        raise AuditValidationError("Pinned model joint set differs from the audit")
    for name, audited in audited_joints.items():
        source = source_joints[name]
        if source.get("type") != audited.get("type"):
            raise AuditValidationError(f"Pinned joint {name} type differs from audit")
        source_range = source.get("range")
        audit_range = audited.get("range_radians")
        if audit_range is None:
            if source_range is not None:
                raise AuditValidationError(
                    f"Pinned model joint {name} gained a range not recorded in the audit"
                )
        else:
            require_close(
                parse_numbers(source_range, f"joint {name}.range"),
                numeric_list(audit_range, f"joint {name}.range_radians", 2),
                f"joint {name}.range_radians",
            )

    actuator_parent = root.find("actuator")
    if actuator_parent is None:
        raise AuditValidationError("Pinned model lacks an actuator section")
    source_actuators = {
        element.get("name"): element
        for element in actuator_parent.findall("position[@name]")
        if element.get("name")
    }
    audited_actuators = index_entries(audit.get("actuators"), "audit.actuators")
    if set(source_actuators) != set(audited_actuators):
        raise AuditValidationError("Pinned actuator set differs from the audit")
    for name, audited in audited_actuators.items():
        source = source_actuators[name]
        if source.get("joint") != audited.get("joint"):
            raise AuditValidationError(f"Pinned actuator {name} joint differs from audit")
        if source.get("class") != audited.get("source_class"):
            raise AuditValidationError(f"Pinned actuator {name} class differs from audit")


def validate_source_equalities(root: ET.Element, audit: dict[str, Any]) -> None:
    """Compare equality solver defaults and loop-closure count."""
    audited = require_dict(audit.get("equality_solver"), "audit.equality_solver")
    source_default = root.find(".//default/equality")
    if source_default is None:
        raise AuditValidationError("Pinned model lacks equality solver defaults")
    require_close(
        parse_numbers(source_default.get("solref"), "equality.solref"),
        numeric_list(audited.get("solref"), "audit equality solref", 2),
        "equality.solref",
    )
    require_close(
        parse_numbers(source_default.get("solimp"), "equality.solimp"),
        numeric_list(audited.get("solimp"), "audit equality solimp", 5),
        "equality.solimp",
    )
    source_equalities = root.find("equality")
    if source_equalities is None:
        raise AuditValidationError("Pinned model lacks equality constraints")
    if len(source_equalities.findall("connect")) != audited.get("count"):
        raise AuditValidationError("Pinned equality count differs from the audit")


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
            raise AuditValidationError(f"Pinned source lacks audited uncertainty note: {note!r}")
    try:
        root = ET.fromstring(model_bytes)
    except ET.ParseError as exc:
        raise AuditValidationError(f"Cannot parse pinned model: {exc}") from exc
    validate_source_defaults(root, audit)
    validate_source_joints(root, audit)
    validate_source_equalities(root, audit)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--model", type=Path)
    return parser.parse_args()


def main() -> int:
    """Validate static policy and, when supplied, the exact pinned MJCF."""
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
