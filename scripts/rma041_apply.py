#!/usr/bin/env python3
"""Apply the guarded RMA-041 model-parameter audit hardening patch."""

from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AUDIT_PATH = ROOT / "models" / "reachy-mini" / "model-parameter-audit.json"
VALIDATOR_PATH = ROOT / "scripts" / "validate_model_parameter_audit.py"
TEST_PATH = ROOT / "scripts" / "tests" / "test_model_parameter_audit.py"
DOC_PATH = ROOT / "docs" / "model-parameter-audit.md"


def replace_once(text: str, old: str, new: str, label: str) -> str:
    """Replace one exact source fragment and fail on drift."""
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def write_text(path: Path, text: str) -> None:
    """Write one normalized UTF-8 text file."""
    path.write_text(text, encoding="utf-8", newline="\n")


def update_audit() -> None:
    """Add explicit diagnostics, provenance, and uncertainty contracts."""
    audit = json.loads(AUDIT_PATH.read_text(encoding="utf-8"))
    audit["schema_version"] = 2
    audit["contract_id"] = "rma041_parameter_fidelity_v2"

    source = audit["source"]
    fidelity = audit["fidelity"]
    fidelity["diagnostics_severity"] = "warning"
    fidelity["blocking_classifications"] = ["placeholder"]

    audit["diagnostics"] = {
        "schema_version": 1,
        "profile_id": fidelity["profile_id"],
        "status": fidelity["diagnostics_status"],
        "severity": fidelity["diagnostics_severity"],
        "title": fidelity["display_name"],
        "classification": fidelity["classification"],
        "calibrated": fidelity["calibrated"],
        "summary": fidelity["summary"],
        "source_model_sha256": source["model_sha256"],
        "blocking_classifications": fidelity["blocking_classifications"],
    }

    explicit = {
        "yaw_body",
        "stewart_1",
        "stewart_2",
        "stewart_3",
        "stewart_4",
        "stewart_5",
        "stewart_6",
    }
    passive = {f"passive_{index}" for index in range(1, 8)}
    antennas = {"right_antenna", "left_antenna"}
    for joint in audit["joints"]:
        name = joint["name"]
        if name in explicit:
            joint["range_provenance_id"] = "explicit_joint_ranges"
        elif name in passive:
            joint["range_provenance_id"] = "passive_ball_joint_limits"
        elif name in antennas:
            joint["range_provenance_id"] = "unbounded_antenna_joints"
        else:
            raise RuntimeError(f"Unexpected joint in RMA-041 audit: {name}")

    audit["joint_limit_provenance"] = {
        "source_kind": "pinned_upstream_mjcf",
        "source_commit": source["commit"],
        "source_model_sha256": source["model_sha256"],
        "attribute": "joint.range",
        "units": "radian",
        "explicit_range_joints": sorted(explicit),
        "unrestricted_ball_joints": sorted(passive),
        "missing_hard_stop_range_joints": sorted(antennas),
        "manufacturer_specification": None,
        "measurement_report": None,
    }

    exact_uncertainties = [
        {
            "scope": "actuator_model",
            "id": "xc330m288t",
            "comment": "probably wrong, would need to re-identify",
            "classification": "placeholder",
        },
        {
            "scope": "actuator_model",
            "id": "sts3215_345",
            "comment": "Confident that this is realistic (mini duck walks with these values)",
            "classification": "upstream_approximation",
        },
        {
            "scope": "actuator_model",
            "id": "sts3215_147",
            "comment": "(Estimation based on the gear ratio difference)",
            "classification": "upstream_approximation",
        },
        {
            "scope": "collision_mesh_selection",
            "id": "collision_meshes",
            "comment": "Collision models defualt: coarse - there is also fine (but much more detailed models)",
            "classification": "upstream_approximation",
        },
    ]
    audit["source_uncertainties"] = exact_uncertainties
    audit["uncertainty_notes"] = [entry["comment"] for entry in exact_uncertainties]

    actuator_models = {entry["id"]: entry for entry in audit["actuator_models"]}
    for entry in exact_uncertainties:
        if entry["scope"] == "actuator_model":
            actuator_models[entry["id"]]["source_comment"] = entry["comment"]

    AUDIT_PATH.write_text(
        json.dumps(audit, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def update_validator() -> None:
    """Strengthen the validator without replacing its source/model checks."""
    text = VALIDATOR_PATH.read_text(encoding="utf-8")
    text = replace_once(text, "import json\nimport sys", "import json\nimport re\nimport sys", "validator import")

    constants_marker = """}\nREQUIRED_GROUPS = {\n"""
    constants = """}\nABSENT_EVIDENCE_CLASSIFICATIONS = {\n    "manufacturer_specification",\n    "measured",\n    "fitted",\n}\nREQUIRED_GROUP_CLASSIFICATIONS = {\n    "body_geometry_transforms_mass_and_inertia": "cad_derived",\n    "explicit_joint_ranges": "upstream_approximation",\n    "unbounded_antenna_joints": "placeholder",\n    "passive_ball_joint_limits": "upstream_approximation",\n    "active_position_actuator_dynamics": "placeholder",\n    "loop_closure_solver_parameters": "upstream_approximation",\n}\nREQUIRED_ACTUATOR_MODEL_EVIDENCE = {\n    "chosen_actuator": ("placeholder", None),\n    "xc330m288t": ("placeholder", "probably wrong, would need to re-identify"),\n    "sts3215_345": (\n        "upstream_approximation",\n        "Confident that this is realistic (mini duck walks with these values)",\n    ),\n    "sts3215_147": (\n        "upstream_approximation",\n        "(Estimation based on the gear ratio difference)",\n    ),\n}\nREQUIRED_SOURCE_UNCERTAINTIES = {\n    ("actuator_model", model_id): {\n        "comment": source_comment,\n        "classification": classification,\n    }\n    for model_id, (classification, source_comment) in REQUIRED_ACTUATOR_MODEL_EVIDENCE.items()\n    if source_comment is not None\n}\nREQUIRED_SOURCE_UNCERTAINTIES[("collision_mesh_selection", "collision_meshes")] = {\n    "comment": (\n        "Collision models defualt: coarse - there is also fine "\n        "(but much more detailed models)"\n    ),\n    "classification": "upstream_approximation",\n}\nREQUIRED_GROUPS = {\n"""
    text = replace_once(text, constants_marker, constants, "validator constants")
    text = text.replace(
        '    "Estimation based on the gear ratio difference",\n',
        '    "(Estimation based on the gear ratio difference)",\n',
        1,
    )

    string_block = '''def require_string(value: object, label: str) -> str:\n    """Return a required nonempty string."""\n    if not isinstance(value, str) or not value:\n        raise AuditValidationError(f"{label} must be a nonempty string")\n    return value\n\n\n'''
    string_replacement = string_block + '''def require_bool(value: object, label: str) -> bool:\n    """Return a required boolean without accepting integer lookalikes."""\n    if not isinstance(value, bool):\n        raise AuditValidationError(f"{label} must be a boolean")\n    return value\n\n\n'''
    text = replace_once(text, string_block, string_replacement, "validator require_bool")

    old_fidelity = '''def validate_fidelity(audit: dict[str, Any]) -> None:\n    """Reject unknown evidence classes and false calibration claims."""\n    fidelity = require_dict(audit.get("fidelity"), "audit.fidelity")\n    calibrated = fidelity.get("calibrated")\n    may_label = fidelity.get("may_be_labeled_calibrated")\n    if not isinstance(calibrated, bool) or not isinstance(may_label, bool):\n        raise AuditValidationError("Fidelity calibration flags must be booleans")\n    if fidelity.get("classification") != "geometric_baseline":\n        raise AuditValidationError("Fidelity classification must be geometric_baseline")\n    if fidelity.get("diagnostics_status") != "uncalibrated_upstream_baseline":\n        raise AuditValidationError("Diagnostics status must be uncalibrated_upstream_baseline")\n\n    classifications = collect_classifications(audit)\n    for path, classification in classifications:\n        if classification not in ALLOWED_CLASSIFICATIONS:\n            raise AuditValidationError(\n                f"Unknown parameter classification {classification!r} at {path}"\n            )\n    has_placeholder = any(classification == "placeholder" for _, classification in classifications)\n    if has_placeholder and (calibrated or may_label):\n        raise AuditValidationError(\n            "Audit contains placeholder parameters but permits a calibrated label"\n        )\n    if calibrated or may_label:\n        raise AuditValidationError("RMA-041 baseline must remain uncalibrated")\n\n\n'''
    new_fidelity = '''def validate_fidelity(audit: dict[str, Any]) -> None:\n    """Reject unknown evidence classes, false calibration claims, and UI drift."""\n    if audit.get("contract_id") != "rma041_parameter_fidelity_v2":\n        raise AuditValidationError("Audit contract_id must identify the RMA-041 v2 contract")\n    source = require_dict(audit.get("source"), "audit.source")\n    fidelity = require_dict(audit.get("fidelity"), "audit.fidelity")\n    calibrated = require_bool(fidelity.get("calibrated"), "fidelity.calibrated")\n    may_label = require_bool(\n        fidelity.get("may_be_labeled_calibrated"),\n        "fidelity.may_be_labeled_calibrated",\n    )\n    for field in ("profile_id", "display_name", "summary", "required_next_level"):\n        require_string(fidelity.get(field), f"fidelity.{field}")\n    if fidelity.get("classification") != "geometric_baseline":\n        raise AuditValidationError("Fidelity classification must be geometric_baseline")\n    if fidelity.get("diagnostics_status") != "uncalibrated_upstream_baseline":\n        raise AuditValidationError("Diagnostics status must be uncalibrated_upstream_baseline")\n    if fidelity.get("diagnostics_severity") != "warning":\n        raise AuditValidationError("Diagnostics severity must visibly warn about calibration")\n    if fidelity.get("blocking_classifications") != ["placeholder"]:\n        raise AuditValidationError("Placeholder must remain the calibration-blocking class")\n\n    classifications = collect_classifications(audit)\n    for path, classification in classifications:\n        if classification not in ALLOWED_CLASSIFICATIONS:\n            raise AuditValidationError(\n                f"Unknown parameter classification {classification!r} at {path}"\n            )\n    has_placeholder = any(classification == "placeholder" for _, classification in classifications)\n    if has_placeholder and (calibrated or may_label):\n        raise AuditValidationError(\n            "Audit contains placeholder parameters but permits a calibrated label"\n        )\n    if calibrated or may_label:\n        raise AuditValidationError("RMA-041 baseline must remain uncalibrated")\n\n    diagnostics = require_dict(audit.get("diagnostics"), "audit.diagnostics")\n    expected = {\n        "schema_version": 1,\n        "profile_id": fidelity["profile_id"],\n        "status": fidelity["diagnostics_status"],\n        "severity": fidelity["diagnostics_severity"],\n        "title": fidelity["display_name"],\n        "classification": fidelity["classification"],\n        "calibrated": calibrated,\n        "summary": fidelity["summary"],\n        "source_model_sha256": source.get("model_sha256"),\n        "blocking_classifications": fidelity["blocking_classifications"],\n    }\n    if diagnostics != expected:\n        raise AuditValidationError("Machine-readable diagnostics payload differs from fidelity data")\n\n\n'''
    text = replace_once(text, old_fidelity, new_fidelity, "validator fidelity")

    old_groups = '''def validate_groups(audit: dict[str, Any]) -> None:\n    """Require each audit group to record scope, evidence, and limitations."""\n    groups = index_entries(\n        audit.get("parameter_groups"),\n        "audit.parameter_groups",\n        key="id",\n    )\n    if set(groups) != REQUIRED_GROUPS:\n        raise AuditValidationError(f"Parameter groups differ from required set: {sorted(groups)}")\n    for group_id, group in groups.items():\n        for field in ("applies_to", "evidence", "limitations"):\n            values = require_list(group.get(field), f"group {group_id}.{field}")\n            if not values or not all(isinstance(item, str) and item for item in values):\n                raise AuditValidationError(\n                    f"Group {group_id}.{field} must contain nonempty strings"\n                )\n\n\n'''
    new_groups = '''def validate_groups(audit: dict[str, Any]) -> None:\n    """Require each parameter group to carry scope, evidence, limits, and classification."""\n    groups = index_entries(\n        audit.get("parameter_groups"),\n        "audit.parameter_groups",\n        key="id",\n    )\n    if set(groups) != REQUIRED_GROUPS:\n        raise AuditValidationError(f"Parameter groups differ from required set: {sorted(groups)}")\n    for group_id, group in groups.items():\n        expected_classification = REQUIRED_GROUP_CLASSIFICATIONS[group_id]\n        if group.get("classification") != expected_classification:\n            raise AuditValidationError(\n                f"Group {group_id} classification must be {expected_classification}"\n            )\n        for field in ("applies_to", "evidence", "limitations"):\n            values = require_list(group.get(field), f"group {group_id}.{field}")\n            if not values or not all(isinstance(item, str) and item for item in values):\n                raise AuditValidationError(\n                    f"Group {group_id}.{field} must contain nonempty strings"\n                )\n\n\n'''
    text = replace_once(text, old_groups, new_groups, "validator groups")

    old_joint = '''def validate_joint_inventory(audit: dict[str, Any], lock: dict[str, Any]) -> None:\n    """Require all pinned joints and active actuators to be classified."""\n    requirements = require_dict(lock.get("model_requirements"), "lock requirements")\n    expected_types = require_dict(\n        requirements.get("required_joint_types"),\n        "required joint types",\n    )\n    expected_actuators = require_dict(\n        requirements.get("required_actuator_joints"),\n        "required actuator joints",\n    )\n    joints = index_entries(audit.get("joints"), "audit.joints")\n    actuators = index_entries(audit.get("actuators"), "audit.actuators")\n    if set(joints) != set(expected_types):\n        raise AuditValidationError("Audit joint set differs from the pinned contract")\n    if set(actuators) != set(expected_actuators):\n        raise AuditValidationError("Audit actuator set differs from the pinned contract")\n\n    actuated_joints = set(expected_actuators.values())\n    for name, expected_type in expected_types.items():\n        joint = joints[name]\n        if joint.get("type") != expected_type:\n            raise AuditValidationError(f"Joint {name} type differs from the pin")\n        if joint.get("actuated") is not (name in actuated_joints):\n            raise AuditValidationError(f"Joint {name} has an incorrect actuated flag")\n        if joint.get("range_radians") is not None:\n            values = numeric_list(joint.get("range_radians"), f"joint {name} range", 2)\n            if values[0] >= values[1]:\n                raise AuditValidationError(f"Joint {name} range must be increasing")\n\n    for name, expected_joint in expected_actuators.items():\n        actuator = actuators[name]\n        if actuator.get("joint") != expected_joint:\n            raise AuditValidationError(f"Actuator {name} joint differs from the pin")\n        if actuator.get("source_class") != "chosen_actuator":\n            raise AuditValidationError(f"Actuator {name} must use chosen_actuator")\n        if actuator.get("classification") != "placeholder":\n            raise AuditValidationError(f"Actuator {name} must remain a placeholder")\n\n\n'''
    new_joint = '''def validate_joint_inventory(audit: dict[str, Any], lock: dict[str, Any]) -> None:\n    """Require all pinned joints, range provenance, and active actuators to be classified."""\n    requirements = require_dict(lock.get("model_requirements"), "lock requirements")\n    expected_types = require_dict(\n        requirements.get("required_joint_types"),\n        "required joint types",\n    )\n    expected_actuators = require_dict(\n        requirements.get("required_actuator_joints"),\n        "required actuator joints",\n    )\n    joints = index_entries(audit.get("joints"), "audit.joints")\n    actuators = index_entries(audit.get("actuators"), "audit.actuators")\n    if set(joints) != set(expected_types):\n        raise AuditValidationError("Audit joint set differs from the pinned contract")\n    if set(actuators) != set(expected_actuators):\n        raise AuditValidationError("Audit actuator set differs from the pinned contract")\n\n    actuated_joints = set(expected_actuators.values())\n    explicit = {"yaw_body", *(f"stewart_{index}" for index in range(1, 7))}\n    passive = {f"passive_{index}" for index in range(1, 8)}\n    antennas = {"right_antenna", "left_antenna"}\n    for name, expected_type in expected_types.items():\n        joint = joints[name]\n        if joint.get("type") != expected_type:\n            raise AuditValidationError(f"Joint {name} type differs from the pin")\n        if joint.get("actuated") is not (name in actuated_joints):\n            raise AuditValidationError(f"Joint {name} has an incorrect actuated flag")\n        if name in explicit:\n            expected_classification = "upstream_approximation"\n            expected_provenance = "explicit_joint_ranges"\n        elif name in passive:\n            expected_classification = "upstream_approximation"\n            expected_provenance = "passive_ball_joint_limits"\n        elif name in antennas:\n            expected_classification = "placeholder"\n            expected_provenance = "unbounded_antenna_joints"\n        else:\n            raise AuditValidationError(f"Joint {name} lacks an RMA-041 provenance policy")\n        if joint.get("range_classification") != expected_classification:\n            raise AuditValidationError(\n                f"Joint {name} range classification must be {expected_classification}"\n            )\n        if joint.get("range_provenance_id") != expected_provenance:\n            raise AuditValidationError(\n                f"Joint {name} range provenance must be {expected_provenance}"\n            )\n        if joint.get("range_radians") is not None:\n            values = numeric_list(joint.get("range_radians"), f"joint {name} range", 2)\n            if values[0] >= values[1]:\n                raise AuditValidationError(f"Joint {name} range must be increasing")\n\n    for name, expected_joint in expected_actuators.items():\n        actuator = actuators[name]\n        if actuator.get("joint") != expected_joint:\n            raise AuditValidationError(f"Actuator {name} joint differs from the pin")\n        if actuator.get("source_class") != "chosen_actuator":\n            raise AuditValidationError(f"Actuator {name} must use chosen_actuator")\n        if actuator.get("classification") != "placeholder":\n            raise AuditValidationError(f"Actuator {name} must remain a placeholder")\n\n\n'''
    text = replace_once(text, old_joint, new_joint, "validator joints")

    old_models = '''def validate_actuator_models(audit: dict[str, Any]) -> None:\n    """Require all retained upstream actuator-default classes to be audited."""\n    models = index_entries(\n        audit.get("actuator_models"),\n        "audit.actuator_models",\n        key="id",\n    )\n    if set(models) != REQUIRED_ACTUATOR_MODELS:\n        raise AuditValidationError("Audit actuator model set is incomplete")\n    chosen = models["chosen_actuator"]\n    if chosen.get("active_in_pinned_model") is not True:\n        raise AuditValidationError("chosen_actuator must be marked active")\n    if chosen.get("source_default_class") != "perfect_actuator":\n        raise AuditValidationError("chosen_actuator must inherit perfect_actuator")\n    if chosen.get("classification") != "placeholder":\n        raise AuditValidationError("chosen_actuator must remain a placeholder")\n\n    for name, model in models.items():\n        joint = require_dict(model.get("joint"), f"actuator model {name}.joint")\n        position = require_dict(\n            model.get("position"),\n            f"actuator model {name}.position",\n        )\n        for attribute in ("damping", "frictionloss", "armature"):\n            numeric_list([joint.get(attribute)], f"{name}.joint.{attribute}", 1)\n        for attribute in ("kp", "kv"):\n            numeric_list([position.get(attribute)], f"{name}.position.{attribute}", 1)\n        numeric_list(position.get("forcerange"), f"{name}.forcerange", 2)\n        if name != "chosen_actuator" and model.get("active_in_pinned_model") is not False:\n            raise AuditValidationError(f"Inactive actuator model {name} is marked active")\n\n\n'''
    new_models = '''def validate_actuator_models(audit: dict[str, Any]) -> None:\n    """Require all retained upstream actuator defaults and uncertainty comments."""\n    models = index_entries(\n        audit.get("actuator_models"),\n        "audit.actuator_models",\n        key="id",\n    )\n    if set(models) != REQUIRED_ACTUATOR_MODELS:\n        raise AuditValidationError("Audit actuator model set is incomplete")\n    chosen = models["chosen_actuator"]\n    if chosen.get("active_in_pinned_model") is not True:\n        raise AuditValidationError("chosen_actuator must be marked active")\n    if chosen.get("source_default_class") != "perfect_actuator":\n        raise AuditValidationError("chosen_actuator must inherit perfect_actuator")\n\n    for name, model in models.items():\n        expected_classification, expected_comment = REQUIRED_ACTUATOR_MODEL_EVIDENCE[name]\n        if model.get("classification") != expected_classification:\n            raise AuditValidationError(\n                f"Actuator model {name} classification must be {expected_classification}"\n            )\n        if model.get("source_comment") != expected_comment:\n            raise AuditValidationError(f"Actuator model {name} source comment differs from audit")\n        require_string(model.get("rationale"), f"actuator model {name}.rationale")\n        joint = require_dict(model.get("joint"), f"actuator model {name}.joint")\n        position = require_dict(\n            model.get("position"),\n            f"actuator model {name}.position",\n        )\n        for attribute in ("damping", "frictionloss", "armature"):\n            numeric_list([joint.get(attribute)], f"{name}.joint.{attribute}", 1)\n        for attribute in ("kp", "kv"):\n            numeric_list([position.get(attribute)], f"{name}.position.{attribute}", 1)\n        numeric_list(position.get("forcerange"), f"{name}.forcerange", 2)\n        if name != "chosen_actuator" and model.get("active_in_pinned_model") is not False:\n            raise AuditValidationError(f"Inactive actuator model {name} is marked active")\n\n\n'''
    text = replace_once(text, old_models, new_models, "validator actuator models")

    old_absent = '''def validate_absent_evidence(audit: dict[str, Any]) -> None:\n    """Require unsupported evidence categories to remain empty and explicit."""\n    absent = require_dict(audit.get("evidence_absent"), "audit.evidence_absent")\n    for category in (\n        "manufacturer_specification",\n        "measured",\n        "fitted",\n        "calibrated_profiles",\n    ):\n        if require_list(absent.get(category), f"evidence_absent.{category}"):\n            raise AuditValidationError(f"Current baseline cannot claim {category}")\n    notes = set(require_list(audit.get("uncertainty_notes"), "uncertainty notes"))\n    if notes != REQUIRED_UNCERTAINTY_NOTES:\n        raise AuditValidationError("Audit uncertainty notes differ from the pinned set")\n\n\n'''
    new_absent = '''def validate_joint_limit_provenance(audit: dict[str, Any]) -> None:\n    """Require one explicit machine-readable source contract for all joint limits."""\n    source = require_dict(audit.get("source"), "audit.source")\n    provenance = require_dict(\n        audit.get("joint_limit_provenance"),\n        "audit.joint_limit_provenance",\n    )\n    expected = {\n        "source_kind": "pinned_upstream_mjcf",\n        "source_commit": source.get("commit"),\n        "source_model_sha256": source.get("model_sha256"),\n        "attribute": "joint.range",\n        "units": "radian",\n        "explicit_range_joints": sorted(\n            {"yaw_body", *(f"stewart_{index}" for index in range(1, 7))}\n        ),\n        "unrestricted_ball_joints": sorted(\n            f"passive_{index}" for index in range(1, 8)\n        ),\n        "missing_hard_stop_range_joints": ["left_antenna", "right_antenna"],\n        "manufacturer_specification": None,\n        "measurement_report": None,\n    }\n    if provenance != expected:\n        raise AuditValidationError("Joint-limit provenance differs from the pinned baseline")\n\n\ndef validate_source_uncertainty_contract(audit: dict[str, Any]) -> None:\n    """Bind every upstream uncertainty comment to its exact audited scope."""\n    indexed: dict[tuple[str, str], dict[str, Any]] = {}\n    for position, raw in enumerate(\n        require_list(audit.get("source_uncertainties"), "audit.source_uncertainties")\n    ):\n        entry = require_dict(raw, f"source_uncertainties[{position}]")\n        key = (\n            require_string(entry.get("scope"), f"source_uncertainties[{position}].scope"),\n            require_string(entry.get("id"), f"source_uncertainties[{position}].id"),\n        )\n        if key in indexed:\n            raise AuditValidationError(f"Duplicate source uncertainty scope: {key}")\n        indexed[key] = entry\n    if set(indexed) != set(REQUIRED_SOURCE_UNCERTAINTIES):\n        raise AuditValidationError("Source uncertainty scopes differ from the pinned set")\n    for key, expected in REQUIRED_SOURCE_UNCERTAINTIES.items():\n        entry = indexed[key]\n        if entry.get("comment") != expected["comment"]:\n            raise AuditValidationError(f"Source uncertainty comment differs for {key}")\n        if entry.get("classification") != expected["classification"]:\n            raise AuditValidationError(f"Source uncertainty classification differs for {key}")\n\n\ndef validate_absent_evidence(audit: dict[str, Any]) -> None:\n    """Require unsupported evidence categories to remain empty and unclaimed."""\n    absent = require_dict(audit.get("evidence_absent"), "audit.evidence_absent")\n    for category in (\n        "manufacturer_specification",\n        "measured",\n        "fitted",\n        "calibrated_profiles",\n    ):\n        if require_list(absent.get(category), f"evidence_absent.{category}"):\n            raise AuditValidationError(f"Current baseline cannot claim {category}")\n    for path, classification in collect_classifications(audit):\n        if classification in ABSENT_EVIDENCE_CLASSIFICATIONS:\n            raise AuditValidationError(\n                f"Audit claims absent evidence class {classification!r} at {path}"\n            )\n    notes = set(require_list(audit.get("uncertainty_notes"), "uncertainty notes"))\n    if notes != REQUIRED_UNCERTAINTY_NOTES:\n        raise AuditValidationError("Audit uncertainty notes differ from the pinned set")\n    validate_source_uncertainty_contract(audit)\n\n\n'''
    text = replace_once(text, old_absent, new_absent, "validator absent evidence")

    text = replace_once(
        text,
        '    if audit.get("schema_version") != 1:\n',
        '    if audit.get("schema_version") != 2:\n',
        "validator schema",
    )
    text = replace_once(
        text,
        "    validate_joint_inventory(audit, lock)\n    validate_actuator_models(audit)\n",
        "    validate_joint_inventory(audit, lock)\n    validate_joint_limit_provenance(audit)\n    validate_actuator_models(audit)\n",
        "validator provenance call",
    )

    location_marker = '''def validate_pinned_model(model_path: Path, audit: dict[str, Any]) -> None:\n'''
    location_function = '''def validate_source_uncertainty_locations(\n    source_text: str,\n    audit: dict[str, Any],\n) -> None:\n    """Require actuator uncertainty comments to remain in their source class blocks."""\n    validate_source_uncertainty_contract(audit)\n    for model_id, (_, source_comment) in REQUIRED_ACTUATOR_MODEL_EVIDENCE.items():\n        if source_comment is None:\n            continue\n        pattern = re.compile(\n            rf'<default class="{re.escape(model_id)}">(?P<body>.*?)</default>',\n            re.DOTALL,\n        )\n        match = pattern.search(source_text)\n        if match is None:\n            raise AuditValidationError(f"Pinned source lacks actuator class {model_id}")\n        if source_comment not in match.group("body"):\n            raise AuditValidationError(\n                f"Pinned source uncertainty comment is not bound to actuator class {model_id}"\n            )\n\n\n'''
    text = replace_once(
        text,
        location_marker,
        location_function + location_marker,
        "validator source uncertainty locations",
    )
    text = replace_once(
        text,
        '    source_text = model_bytes.decode("utf-8")\n    for note in REQUIRED_UNCERTAINTY_NOTES:\n',
        '    source_text = model_bytes.decode("utf-8")\n    validate_source_uncertainty_locations(source_text, audit)\n    for note in REQUIRED_UNCERTAINTY_NOTES:\n',
        "validator location call",
    )
    text = replace_once(
        text,
        "        f\"diagnostics={audit['fidelity']['diagnostics_status']}\"\n",
        "        f\"diagnostics={audit['fidelity']['diagnostics_status']} \"\n        f\"contract={audit['contract_id']}\"\n",
        "validator completion output",
    )
    write_text(VALIDATOR_PATH, text)


def update_tests() -> None:
    """Add regression coverage for every newly enforced RMA-041 contract."""
    text = TEST_PATH.read_text(encoding="utf-8")
    text = replace_once(text, "import xml.etree.ElementTree as ET\n", "", "test ET import")
    old_fixture_start = '''        lines = ['<?xml version="1.0"?>']\n        for note in audit["uncertainty_notes"]:\n            lines.append(f"<!-- {note} -->")\n        lines.extend(\n'''
    new_fixture_start = '''        uncertainties = {\n            (entry["scope"], entry["id"]): entry["comment"]\n            for entry in audit["source_uncertainties"]\n        }\n        lines = [\n            '<?xml version="1.0"?>',\n            f'<!-- {uncertainties[("collision_mesh_selection", "collision_meshes")]} -->',\n        ]\n        lines.extend(\n'''
    text = replace_once(text, old_fixture_start, new_fixture_start, "test fixture comments")
    text = replace_once(
        text,
        '''            lines.append(f'    <default class="{source_class}">')\n            joint = model["joint"]\n''',
        '''            lines.append(f'    <default class="{source_class}">')\n            source_comment = uncertainties.get(("actuator_model", model["id"]))\n            if source_comment is not None:\n                lines.append(f"      <!-- {source_comment} -->")\n            joint = model["joint"]\n''',
        "test actuator comments",
    )
    old_antenna = '''        audit = copy.deepcopy(self.audit)\n        root = ET.fromstring(self.fixture_model(audit))\n        antenna = root.find(".//joint[@name='right_antenna']")\n        self.assertIsNotNone(antenna)\n        antenna.set("range", "-1 1")\n        model_bytes = ET.tostring(root, encoding="utf-8", xml_declaration=True)\n        notes = "\\n".join(f"<!-- {note} -->" for note in audit["uncertainty_notes"])\n        model_bytes = model_bytes.replace(b"?>", f"?>\\n{notes}".encode(), 1)\n        result = self.run_validator(audit, copy.deepcopy(self.baseline), model_bytes)\n'''
    new_antenna = '''        audit = copy.deepcopy(self.audit)\n        model_text = self.fixture_model(audit).replace(\n            '<joint name="right_antenna" type="hinge"/>',\n            '<joint name="right_antenna" type="hinge" range="-1 1"/>',\n            1,\n        )\n        result = self.run_validator(\n            audit,\n            copy.deepcopy(self.baseline),\n            model_text.encode(),\n        )\n'''
    text = replace_once(text, old_antenna, new_antenna, "test antenna mutation")

    insertion = '''\n    def test_missing_parameter_group_classification_fails(self) -> None:\n        """Every audited parameter group must carry its evidence classification."""\n        audit = copy.deepcopy(self.audit)\n        del audit["parameter_groups"][0]["classification"]\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("classification must be", result.stderr)\n\n    def test_absent_measured_evidence_cannot_be_claimed(self) -> None:\n        """An empty measured-evidence category cannot support a measured label."""\n        audit = copy.deepcopy(self.audit)\n        audit["parameter_groups"][0]["classification"] = "measured"\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("classification must be cad_derived", result.stderr)\n\n    def test_joint_limit_provenance_drift_fails(self) -> None:\n        """Joint ranges must stay bound to the exact pinned source identity."""\n        audit = copy.deepcopy(self.audit)\n        audit["joint_limit_provenance"]["source_commit"] = "0" * 40\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("Joint-limit provenance", result.stderr)\n\n    def test_joint_range_provenance_id_is_required(self) -> None:\n        """Each joint must point to the policy that explains its encoded range."""\n        audit = copy.deepcopy(self.audit)\n        del audit["joints"][0]["range_provenance_id"]\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("range provenance", result.stderr)\n\n    def test_uncertainty_comment_cannot_be_reassigned(self) -> None:\n        """Upstream uncertainty text must remain bound to its exact actuator class."""\n        audit = copy.deepcopy(self.audit)\n        first = audit["source_uncertainties"][0]\n        second = audit["source_uncertainties"][1]\n        first["comment"], second["comment"] = second["comment"], first["comment"]\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("Source uncertainty comment differs", result.stderr)\n\n    def test_diagnostics_payload_must_match_fidelity(self) -> None:\n        """Future diagnostics UI data cannot silently contradict the audit."""\n        audit = copy.deepcopy(self.audit)\n        audit["diagnostics"]["calibrated"] = True\n        result = self.run_validator(audit, copy.deepcopy(self.baseline))\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("diagnostics payload differs", result.stderr)\n\n    def test_source_comment_must_remain_inside_matching_actuator_class(self) -> None:\n        """A comment elsewhere in the MJCF cannot satisfy model-specific evidence."""\n        audit = copy.deepcopy(self.audit)\n        model_text = self.fixture_model(audit)\n        comment = audit["actuator_models"][1]["source_comment"]\n        model_text = model_text.replace(f"      <!-- {comment} -->\\n", "", 1)\n        model_text = model_text.replace("<mujoco model=", f"<!-- {comment} -->\\n<mujoco model=", 1)\n        result = self.run_validator(\n            audit,\n            copy.deepcopy(self.baseline),\n            model_text.encode(),\n        )\n        self.assertNotEqual(0, result.returncode)\n        self.assertIn("not bound to actuator class", result.stderr)\n'''
    text = replace_once(
        text,
        "\n\nif __name__ == \"__main__\":\n",
        insertion + "\n\nif __name__ == \"__main__\":\n",
        "test insertion",
    )
    write_text(TEST_PATH, text)


def update_documentation() -> None:
    """Document the structured machine-readable closure contract."""
    text = DOC_PATH.read_text(encoding="utf-8")
    diagnostics_section = '''\n## Machine-readable diagnostics contract\n\nThe audit schema is version `2` and identifies the contract as\n`rma041_parameter_fidelity_v2`. Its `diagnostics` object is intentionally small\nand display-ready: it carries the profile ID, status, warning severity, title,\nfidelity classification, calibration flag, summary, source-model SHA-256, and\nthe classifications that block a calibrated claim. CI requires that payload to\nbe an exact projection of the authoritative `fidelity` and `source` fields, so a\nfuture diagnostics screen cannot silently contradict the audit.\n\nThe current payload reports `uncalibrated_upstream_baseline` with warning\nseverity and `calibrated=false`.\n\n## Structured provenance and uncertainty binding\n\n`joint_limit_provenance` binds every range decision to the pinned MJCF commit,\nmodel SHA-256, `joint.range` attribute, and radian units. It separately lists\nexplicit hinge ranges, unrestricted passive ball joints, and antenna joints that\nlack encoded hard stops. Each joint points back to the applicable policy group\nthrough `range_provenance_id`. No manufacturer specification or measurement\nreport is claimed.\n\n`source_uncertainties` binds each upstream cautionary comment to its exact scope:\nthe three retained actuator-default classes and the collision-mesh selection.\nFor actuator classes, full source validation requires the comment to remain\ninside the matching `<default class=...>` block; moving the same text elsewhere\nno longer satisfies the audit.\n\n'''
    text = replace_once(
        text,
        "\n## Source-derived geometry and inertias\n",
        diagnostics_section + "## Source-derived geometry and inertias\n",
        "documentation diagnostics insertion",
    )
    validation_section = '''\n## Validation policy\n\nThe validator now fails closed when a required classification or provenance ID\nis missing, when a parameter claims manufacturer, measured, or fitted evidence\nthat the audit explicitly records as absent, when a source comment is reassigned\nto another model, when the diagnostics payload drifts from fidelity data, or when\nthe pinned source moves an actuator uncertainty comment outside its class block.\n\n'''
    text = replace_once(
        text,
        "\n## Required follow-on evidence\n",
        validation_section + "## Required follow-on evidence\n",
        "documentation validation insertion",
    )
    write_text(DOC_PATH, text)


def main() -> None:
    """Apply every guarded transformation."""
    update_audit()
    update_validator()
    update_tests()
    update_documentation()


if __name__ == "__main__":
    main()
