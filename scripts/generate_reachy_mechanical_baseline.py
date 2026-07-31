#!/usr/bin/env python3
"""Validate and generate the RMA-063 mechanical-effects parameter registry."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

ROLES = ["body_yaw", "stewart", "antenna"]
EVIDENCE_CLASSES = ["upstream_approximation", "engineering_estimate", "calibrated"]
FIELDS = [
    "coulomb_friction_newton_metres",
    "viscous_friction_newton_metre_seconds_per_radian",
    "breakaway_torque_newton_metres",
    "stiction_enter_velocity_radians_per_second",
    "stiction_exit_velocity_radians_per_second",
    "backlash_half_width_radians",
    "compliance_stiffness_newton_metres_per_radian",
    "compliance_damping_newton_metre_seconds_per_radian",
    "maximum_elastic_deflection_radians",
]
ACTUATORS = [
    ("yaw_body", "body_yaw"),
    *((f"stewart_{index}", "stewart") for index in range(1, 7)),
    ("right_antenna", "antenna"),
    ("left_antenna", "antenna"),
]
UNITS = {
    "coulomb_friction_newton_metres": "N*m",
    "viscous_friction_newton_metre_seconds_per_radian": "N*m*s/rad",
    "breakaway_torque_newton_metres": "N*m",
    "stiction_enter_velocity_radians_per_second": "rad/s",
    "stiction_exit_velocity_radians_per_second": "rad/s",
    "backlash_half_width_radians": "rad",
    "compliance_stiffness_newton_metres_per_radian": "N*m/rad",
    "compliance_damping_newton_metre_seconds_per_radian": "N*m*s/rad",
    "maximum_elastic_deflection_radians": "rad",
}


class ContractError(ValueError):
    """Raised when the mechanical-effects contract is invalid or overclaims evidence."""


def exact_keys(value: dict[str, Any], expected: set[str], label: str) -> None:
    actual = set(value)
    if actual != expected:
        raise ContractError(
            f"{label} keys differ: expected {sorted(expected)}, found {sorted(actual)}"
        )


def nonempty(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value:
        raise ContractError(f"{label} must be a non-empty string")
    return value


def validate_scalar(value: Any, label: str, *, positive: bool) -> float:
    if not isinstance(value, dict):
        raise ContractError(f"{label} must be an object")
    exact_keys(value, {"value", "evidence_class", "evidence_id"}, label)
    if value["evidence_class"] not in EVIDENCE_CLASSES:
        raise ContractError(f"{label}.evidence_class is invalid")
    if value["evidence_class"] == "calibrated":
        raise ContractError(f"{label} may not claim calibration in RMA-063")
    nonempty(value["evidence_id"], f"{label}.evidence_id")
    numeric = value["value"]
    if isinstance(numeric, bool) or not isinstance(numeric, (int, float)):
        raise ContractError(f"{label}.value must be numeric")
    numeric = float(numeric)
    if not math.isfinite(numeric):
        raise ContractError(f"{label}.value must be finite")
    if positive and numeric <= 0.0:
        raise ContractError(f"{label}.value must be positive")
    if not positive and numeric < 0.0:
        raise ContractError(f"{label}.value must be non-negative")
    return numeric


def validate_contract(data: dict[str, Any]) -> None:
    exact_keys(
        data,
        {
            "schema_version",
            "contract_id",
            "source",
            "unit_contract",
            "effect_semantics",
            "parameter_sets",
            "actuator_bindings",
        },
        "root",
    )
    if data["schema_version"] != 1 or data["contract_id"] != "rma063_mechanical_effects_v1":
        raise ContractError("schema_version or contract_id is not the RMA-063 v1 contract")
    source = data["source"]
    exact_keys(
        source,
        {
            "reachy_commit",
            "model_sha256",
            "parameter_audit_contract",
            "electrical_contract",
            "electrical_validated_commit",
        },
        "source",
    )
    if source["reachy_commit"] != "a739a6e461eb6d722901f1cfc225265ffc85c28d":
        raise ContractError("pinned Reachy source commit drifted")
    if source["model_sha256"] != "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46":
        raise ContractError("pinned model hash drifted")
    if source["electrical_contract"] != "rma062_electrical_controller_v1":
        raise ContractError("RMA-062 source contract drifted")
    if data["unit_contract"] != UNITS:
        raise ContractError("unit_contract differs from the native mechanical parameter units")
    semantics = data["effect_semantics"]
    exact_keys(
        semantics,
        {
            "friction",
            "stiction",
            "backlash",
            "compliance",
            "identification",
            "disablement",
        },
        "effect_semantics",
    )
    for key, value in semantics.items():
        nonempty(value, f"effect_semantics.{key}")

    sets = data["parameter_sets"]
    if not isinstance(sets, list) or len(sets) != 3:
        raise ContractError("exactly three role-specific mechanical parameter sets are required")
    set_by_id: dict[str, dict[str, Any]] = {}
    roles: list[str] = []
    fingerprints: dict[tuple[float, ...], str] = {}
    for index, entry in enumerate(sets):
        label = f"parameter_sets[{index}]"
        exact_keys(
            entry,
            {
                "id",
                "role",
                "overall_evidence_class",
                "source_evidence_id",
                "parameters",
                "derivation",
                "limitations",
            },
            label,
        )
        identifier = nonempty(entry["id"], f"{label}.id")
        if identifier in set_by_id:
            raise ContractError(f"duplicate parameter set id: {identifier}")
        role = entry["role"]
        if role not in ROLES:
            raise ContractError(f"{label}.role is invalid")
        roles.append(role)
        evidence_class = entry["overall_evidence_class"]
        if evidence_class not in EVIDENCE_CLASSES or evidence_class == "calibrated":
            raise ContractError(f"{label}.overall_evidence_class is invalid for RMA-063")
        nonempty(entry["source_evidence_id"], f"{label}.source_evidence_id")
        parameters = entry["parameters"]
        if not isinstance(parameters, dict):
            raise ContractError(f"{label}.parameters must be an object")
        exact_keys(parameters, set(FIELDS), f"{label}.parameters")
        numeric: dict[str, float] = {}
        for field in FIELDS:
            positive = field in {
                "breakaway_torque_newton_metres",
                "stiction_exit_velocity_radians_per_second",
                "compliance_stiffness_newton_metres_per_radian",
                "compliance_damping_newton_metre_seconds_per_radian",
                "maximum_elastic_deflection_radians",
            }
            numeric[field] = validate_scalar(
                parameters[field], f"{label}.parameters.{field}", positive=positive
            )
        if numeric["breakaway_torque_newton_metres"] < numeric["coulomb_friction_newton_metres"]:
            raise ContractError(f"{label} breakaway torque must not be below Coulomb friction")
        if not (
            numeric["stiction_enter_velocity_radians_per_second"]
            < numeric["stiction_exit_velocity_radians_per_second"]
        ):
            raise ContractError(f"{label} stiction exit velocity must exceed enter velocity")
        derivation = entry["derivation"]
        if not isinstance(derivation, list) or not derivation:
            raise ContractError(f"{label}.derivation must contain role-specific evidence")
        for item_index, item in enumerate(derivation):
            nonempty(item, f"{label}.derivation[{item_index}]")
        limitations = entry["limitations"]
        if not isinstance(limitations, list) or not limitations:
            raise ContractError(f"{label}.limitations must be non-empty")
        for item_index, item in enumerate(limitations):
            nonempty(item, f"{label}.limitations[{item_index}]")
        fingerprint = tuple(numeric[field] for field in FIELDS)
        if fingerprint in fingerprints:
            raise ContractError(
                f"cross-role mechanical parameter copy detected between {fingerprints[fingerprint]} and {identifier}"
            )
        fingerprints[fingerprint] = identifier
        set_by_id[identifier] = entry
    if roles != ROLES:
        raise ContractError(f"parameter set roles must be {ROLES}, found {roles}")

    bindings = data["actuator_bindings"]
    if not isinstance(bindings, list) or len(bindings) != len(ACTUATORS):
        raise ContractError("all nine actuators require explicit mechanical bindings")
    for index, ((expected_name, expected_role), binding) in enumerate(
        zip(ACTUATORS, bindings, strict=True)
    ):
        label = f"actuator_bindings[{index}]"
        exact_keys(binding, {"actuator_name", "parameter_set_id", "role"}, label)
        if binding["actuator_name"] != expected_name or binding["role"] != expected_role:
            raise ContractError(f"{label} identity/order differs from the pinned model")
        parameter_set = set_by_id.get(binding["parameter_set_id"])
        if parameter_set is None:
            raise ContractError(f"{label} references an unknown parameter set")
        if parameter_set["role"] != expected_role:
            raise ContractError(f"{label} crosses actuator roles")


def evidence_enum(value: str) -> str:
    return {
        "upstream_approximation": "MechanicalEvidenceClass::UpstreamApproximation",
        "engineering_estimate": "MechanicalEvidenceClass::EngineeringEstimate",
        "calibrated": "MechanicalEvidenceClass::Calibrated",
    }[value]


def role_enum(value: str) -> str:
    return {
        "body_yaw": "ActuatorRole::BodyYaw",
        "stewart": "ActuatorRole::Stewart",
        "antenna": "ActuatorRole::Antenna",
    }[value]


def scalar_cpp(value: dict[str, Any]) -> str:
    return (
        f'MechanicalScalar{{{float(value["value"]):.17g}, '
        f'{evidence_enum(value["evidence_class"])}, "{value["evidence_id"]}"}}'
    )


def generate(data: dict[str, Any]) -> str:
    lines = [
        "#ifndef REACHY_MECHANICAL_BASELINE_GENERATED_HPP",
        "#define REACHY_MECHANICAL_BASELINE_GENERATED_HPP",
        "",
        '#include "reachy_mechanical_servo_model.hpp"',
        "",
        "#include <array>",
        "",
        "namespace reachy::servo::generated {",
        "",
        "inline constexpr std::array<MechanicalEffectsParameters, 3> kMechanicalBaselines{{",
    ]
    for entry in data["parameter_sets"]:
        lines.extend(
            [
                "    {",
                f'        "{entry["id"]}",',
                f"        {role_enum(entry['role'])},",
                f"        {evidence_enum(entry['overall_evidence_class'])},",
                f'        "{entry["source_evidence_id"]}",',
            ]
        )
        for field in FIELDS:
            lines.append(f"        {scalar_cpp(entry['parameters'][field])},")
        lines.append("    },")
    lines.extend(
        [
            "}};",
            "",
            "inline constexpr std::array<ServoActuatorBinding, 9> kMechanicalBindings{{",
        ]
    )
    for binding in data["actuator_bindings"]:
        lines.append(
            f'    {{"{binding["actuator_name"]}", "{binding["parameter_set_id"]}", '
            f'{role_enum(binding["role"])} }},'
        )
    lines.extend(
        [
            "}};",
            "",
            "}  // namespace reachy::servo::generated",
            "",
            "#endif",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--profile",
        type=Path,
        default=Path("models/reachy-mini/mechanical-effects-baseline.json"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("native/reachy_sim/src/reachy_mechanical_baseline.generated.hpp"),
    )
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        data = json.loads(args.profile.read_text(encoding="utf-8"))
        validate_contract(data)
        rendered = generate(data)
        if args.check:
            if not args.output.is_file() or args.output.read_text(encoding="utf-8") != rendered:
                raise ContractError("generated mechanical baseline header is stale")
            print(f"RMA-063 mechanical baseline is current: {data['contract_id']}")
            return 0
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Generated {args.output}")
        return 0
    except (OSError, json.JSONDecodeError, ContractError) as error:
        print(f"Mechanical baseline generation failed: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
