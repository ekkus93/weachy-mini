#!/usr/bin/env python3
"""Validate the Reachy model parameter audit against pinned source contracts."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
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
ABSENT_EVIDENCE_CLASSIFICATIONS = {
    "manufacturer_specification",
    "measured",
    "fitted",
}
REQUIRED_GROUP_CLASSIFICATIONS = {
    "body_geometry_transforms_mass_and_inertia": "cad_derived",
    "explicit_joint_ranges": "upstream_approximation",
    "unbounded_antenna_joints": "placeholder",
    "passive_ball_joint_limits": "upstream_approximation",
    "active_position_actuator_dynamics": "placeholder",
    "loop_closure_solver_parameters": "upstream_approximation",
}
REQUIRED_ACTUATOR_MODEL_EVIDENCE = {
    "chosen_actuator": ("placeholder", None),
    "xc330m288t": ("placeholder", "probably wrong, would need to re-identify"),
    "sts3215_345": (
        "upstream_approximation",
        "Confident that this is realistic (mini duck walks with these values)",
    ),
    "sts3215_147": (
        "upstream_approximation",
        "(Estimation based on the gear ratio difference)",
    ),
}
REQUIRED_SOURCE_UNCERTAINTIES = {
    ("actuator_model", model_id): {
        "comment": source_comment,
        "classification": classification,
    }
    for model_id, (classification, source_comment) in REQUIRED_ACTUATOR_MODEL_EVIDENCE.items()
    if source_comment is not None
}
REQUIRED_SOURCE_UNCERTAINTIES[("collision_mesh_selection", "collision_meshes")] = {
    "comment": (
        "Collision models defualt: coarse - there is also fine (but much more detailed models)"
    ),
    "classification": "upstream_approximation",
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
    "(Estimation based on the gear ratio difference)",
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


def require_bool(value: object, label: str) -> bool:
    """Return a required boolean without accepting integer lookalikes."""
    if not isinstance(value, bool):
        raise AuditValidationError(f"{label} must be a boolean")
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
            is_fidelity_level = child_path in {
                "audit.fidelity.classification",
                "audit.diagnostics.classification",
            }
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
    """Reject unknown evidence classes, false calibration claims, and UI drift."""
    if audit.get("contract_id") != "rma041_parameter_fidelity_v2":
        raise AuditValidationError("Audit contract_id must identify the RMA-041 v2 contract")
    source = require_dict(audit.get("source"), "audit.source")
    fidelity = require_dict(audit.get("fidelity"), "audit.fidelity")
    calibrated = require_bool(fidelity.get("calibrated"), "fidelity.calibrated")
    may_label = require_bool(
        fidelity.get("may_be_labeled_calibrated"),
        "fidelity.may_be_labeled_calibrated",
    )
    for field in ("profile_id", "display_name", "summary", "required_next_level"):
        require_string(fidelity.get(field), f"fidelity.{field}")
    if fidelity.get("classification") != "geometric_baseline":
        raise AuditValidationError("Fidelity classification must be geometric_baseline")
    if fidelity.get("diagnostics_status") != "uncalibrated_upstream_baseline":
        raise AuditValidationError("Diagnostics status must be uncalibrated_upstream_baseline")
    if fidelity.get("diagnostics_severity") != "warning":
        raise AuditValidationError("Diagnostics severity must visibly warn about calibration")
    if fidelity.get("blocking_classifications") != ["placeholder"]:
        raise AuditValidationError("Placeholder must remain the calibration-blocking class")

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

    diagnostics = require_dict(audit.get("diagnostics"), "audit.diagnostics")
    expected = {
        "schema_version": 1,
        "profile_id": fidelity["profile_id"],
        "status": fidelity["diagnostics_status"],
        "severity": fidelity["diagnostics_severity"],
        "title": fidelity["display_name"],
        "classification": fidelity["classification"],
        "calibrated": calibrated,
        "summary": fidelity["summary"],
        "source_model_sha256": source.get("model_sha256"),
        "blocking_classifications": fidelity["blocking_classifications"],
    }
    if diagnostics != expected:
        raise AuditValidationError(
            "Machine-readable diagnostics payload differs from fidelity data"
        )


def validate_groups(audit: dict[str, Any]) -> None:
    """Require each parameter group to carry scope, evidence, limits, and classification."""
    groups = index_entries(
        audit.get("parameter_groups"),
        "audit.parameter_groups",
        key="id",
    )
    if set(groups) != REQUIRED_GROUPS:
        raise AuditValidationError(f"Parameter groups differ from required set: {sorted(groups)}")
    for group_id, group in groups.items():
        expected_classification = REQUIRED_GROUP_CLASSIFICATIONS[group_id]
        if group.get("classification") != expected_classification:
            raise AuditValidationError(
                f"Group {group_id} classification must be {expected_classification}"
            )
        for field in ("applies_to", "evidence", "limitations"):
            values = require_list(group.get(field), f"group {group_id}.{field}")
            if not values or not all(isinstance(item, str) and item for item in values):
                raise AuditValidationError(
                    f"Group {group_id}.{field} must contain nonempty strings"
                )


def validate_joint_inventory(audit: dict[str, Any], lock: dict[str, Any]) -> None:
    """Require all pinned joints, range provenance, and active actuators to be classified."""
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
    explicit = {"yaw_body", *(f"stewart_{index}" for index in range(1, 7))}
    passive = {f"passive_{index}" for index in range(1, 8)}
    antennas = {"right_antenna", "left_antenna"}
    for name, expected_type in expected_types.items():
        joint = joints[name]
        if joint.get("type") != expected_type:
            raise AuditValidationError(f"Joint {name} type differs from the pin")
        if joint.get("actuated") is not (name in actuated_joints):
            raise AuditValidationError(f"Joint {name} has an incorrect actuated flag")
        if name in explicit:
            expected_classification = "upstream_approximation"
            expected_provenance = "explicit_joint_ranges"
        elif name in passive:
            expected_classification = "upstream_approximation"
            expected_provenance = "passive_ball_joint_limits"
        elif name in antennas:
            expected_classification = "placeholder"
            expected_provenance = "unbounded_antenna_joints"
        else:
            raise AuditValidationError(f"Joint {name} lacks an RMA-041 provenance policy")
        if joint.get("range_classification") != expected_classification:
            raise AuditValidationError(
                f"Joint {name} range classification must be {expected_classification}"
            )
        if joint.get("range_provenance_id") != expected_provenance:
            raise AuditValidationError(
                f"Joint {name} range provenance must be {expected_provenance}"
            )
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
    """Require all retained upstream actuator defaults and uncertainty comments."""
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

    for name, model in models.items():
        expected_classification, expected_comment = REQUIRED_ACTUATOR_MODEL_EVIDENCE[name]
        if model.get("classification") != expected_classification:
            raise AuditValidationError(
                f"Actuator model {name} classification must be {expected_classification}"
            )
        if model.get("source_comment") != expected_comment:
            raise AuditValidationError(f"Actuator model {name} source comment differs from audit")
        require_string(model.get("rationale"), f"actuator model {name}.rationale")
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


def validate_joint_limit_provenance(audit: dict[str, Any]) -> None:
    """Require one explicit machine-readable source contract for all joint limits."""
    source = require_dict(audit.get("source"), "audit.source")
    provenance = require_dict(
        audit.get("joint_limit_provenance"),
        "audit.joint_limit_provenance",
    )
    expected = {
        "source_kind": "pinned_upstream_mjcf",
        "source_commit": source.get("commit"),
        "source_model_sha256": source.get("model_sha256"),
        "attribute": "joint.range",
        "units": "radian",
        "explicit_range_joints": sorted(
            {"yaw_body", *(f"stewart_{index}" for index in range(1, 7))}
        ),
        "unrestricted_ball_joints": sorted(f"passive_{index}" for index in range(1, 8)),
        "missing_hard_stop_range_joints": ["left_antenna", "right_antenna"],
        "manufacturer_specification": None,
        "measurement_report": None,
    }
    if provenance != expected:
        raise AuditValidationError("Joint-limit provenance differs from the pinned baseline")


def validate_source_uncertainty_contract(audit: dict[str, Any]) -> None:
    """Bind every upstream uncertainty comment to its exact audited scope."""
    indexed: dict[tuple[str, str], dict[str, Any]] = {}
    for position, raw in enumerate(
        require_list(audit.get("source_uncertainties"), "audit.source_uncertainties")
    ):
        entry = require_dict(raw, f"source_uncertainties[{position}]")
        key = (
            require_string(entry.get("scope"), f"source_uncertainties[{position}].scope"),
            require_string(entry.get("id"), f"source_uncertainties[{position}].id"),
        )
        if key in indexed:
            raise AuditValidationError(f"Duplicate source uncertainty scope: {key}")
        indexed[key] = entry
    if set(indexed) != set(REQUIRED_SOURCE_UNCERTAINTIES):
        raise AuditValidationError("Source uncertainty scopes differ from the pinned set")
    for key, expected in REQUIRED_SOURCE_UNCERTAINTIES.items():
        entry = indexed[key]
        if entry.get("comment") != expected["comment"]:
            raise AuditValidationError(f"Source uncertainty comment differs for {key}")
        if entry.get("classification") != expected["classification"]:
            raise AuditValidationError(f"Source uncertainty classification differs for {key}")


def validate_absent_evidence(audit: dict[str, Any]) -> None:
    """Require unsupported evidence categories to remain empty and unclaimed."""
    absent = require_dict(audit.get("evidence_absent"), "audit.evidence_absent")
    for category in (
        "manufacturer_specification",
        "measured",
        "fitted",
        "calibrated_profiles",
    ):
        if require_list(absent.get(category), f"evidence_absent.{category}"):
            raise AuditValidationError(f"Current baseline cannot claim {category}")
    for path, classification in collect_classifications(audit):
        if classification in ABSENT_EVIDENCE_CLASSIFICATIONS:
            raise AuditValidationError(
                f"Audit claims absent evidence class {classification!r} at {path}"
            )
    notes = set(require_list(audit.get("uncertainty_notes"), "uncertainty notes"))
    if notes != REQUIRED_UNCERTAINTY_NOTES:
        raise AuditValidationError("Audit uncertainty notes differ from the pinned set")
    validate_source_uncertainty_contract(audit)


def validate_static_audit(
    audit: dict[str, Any],
    lock: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Validate audit policy without requiring an upstream checkout."""
    if audit.get("schema_version") != 2:
        raise AuditValidationError("Unsupported model parameter audit schema")
    validate_source_identity(audit, lock, baseline)
    validate_fidelity(audit)
    validate_groups(audit)
    validate_joint_inventory(audit, lock)
    validate_joint_limit_provenance(audit)
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


def validate_source_uncertainty_locations(
    source_text: str,
    audit: dict[str, Any],
) -> None:
    """Require actuator uncertainty comments to remain in their source class blocks."""
    validate_source_uncertainty_contract(audit)
    for model_id, (_, source_comment) in REQUIRED_ACTUATOR_MODEL_EVIDENCE.items():
        if source_comment is None:
            continue
        pattern = re.compile(
            rf'<default class="{re.escape(model_id)}">(?P<body>.*?)</default>',
            re.DOTALL,
        )
        match = pattern.search(source_text)
        if match is None:
            raise AuditValidationError(f"Pinned source lacks actuator class {model_id}")
        if source_comment not in match.group("body"):
            raise AuditValidationError(
                f"Pinned source uncertainty comment is not bound to actuator class {model_id}"
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
    validate_source_uncertainty_locations(source_text, audit)
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
        f"diagnostics={audit['fidelity']['diagnostics_status']} "
        f"contract={audit['contract_id']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
