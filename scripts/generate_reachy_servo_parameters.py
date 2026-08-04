#!/usr/bin/env python3
"""Validate and generate the RMA-061 native servo parameter registry."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

QUALITY = ["placeholder", "manufacturer_estimate", "calibrated"]
ROLES = ["body_yaw", "stewart", "antenna"]
MODES = ["disabled", "position", "velocity", "torque"]
ACTUATORS = [
    ("yaw_body", "body_yaw"),
    *((f"stewart_{index}", "stewart") for index in range(1, 7)),
    ("right_antenna", "antenna"),
    ("left_antenna", "antenna"),
]
COMMAND_FIELDS = [
    "sequence",
    "sample_time_seconds",
    "mode",
    "target_position_radians",
    "target_velocity_radians_per_second",
    "profile_velocity_radians_per_second",
    "profile_acceleration_radians_per_second_squared",
    "feedforward_torque_newton_metres",
    "torque_enabled",
]
OBSERVATION_FIELDS = [
    "sample_time_seconds",
    "position_radians",
    "velocity_radians_per_second",
    "applied_torque_newton_metres",
    "estimated_current_amperes",
    "supply_voltage_volts",
    "temperature_celsius",
    "fault_flags",
]
PARAMETER_FIELDS = [
    "command_sample_period_seconds",
    "command_latency_seconds",
    "encoder_position_quantum_radians",
    "encoder_velocity_quantum_radians_per_second",
    "continuous_current_limit_amperes",
    "peak_current_limit_amperes",
    "peak_current_duration_seconds",
    "stall_torque_newton_metres",
    "no_load_speed_radians_per_second",
    "nominal_voltage_volts",
    "minimum_voltage_volts",
    "maximum_voltage_volts",
    "ambient_temperature_celsius",
    "warning_temperature_celsius",
    "shutdown_temperature_celsius",
]
FAULTS = [
    ("over_current", 0),
    ("over_temperature", 1),
    ("under_voltage", 2),
    ("over_voltage", 3),
    ("encoder", 4),
    ("communication", 5),
    ("model_rejected", 6),
]


class ContractError(ValueError):
    """Raised when the servo contract is incomplete or overclaims evidence."""


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


def validate_scalar(value: Any, label: str) -> None:
    if not isinstance(value, dict):
        raise ContractError(f"{label} must be an object")
    exact_keys(value, {"value", "quality", "evidence_id"}, label)
    quality = value["quality"]
    if quality not in QUALITY:
        raise ContractError(f"{label}.quality is invalid: {quality!r}")
    nonempty(value["evidence_id"], f"{label}.evidence_id")
    numeric = value["value"]
    if numeric is None:
        if quality != "placeholder":
            raise ContractError(f"{label} may be null only when quality is placeholder")
        return
    if (
        isinstance(numeric, bool)
        or not isinstance(numeric, (int, float))
        or not math.isfinite(float(numeric))
    ):
        raise ContractError(f"{label}.value must be finite or null")


def validate_contract(data: dict[str, Any]) -> None:
    exact_keys(
        data,
        {
            "schema_version",
            "contract_id",
            "source",
            "quality_labels",
            "command_modes",
            "command_fields",
            "observation_fields",
            "fault_flags",
            "parameter_fields",
            "parameter_sets",
            "actuator_bindings",
        },
        "root",
    )
    if data["schema_version"] != 1 or data["contract_id"] != "rma061_servo_model_v1":
        raise ContractError("schema_version or contract_id is not the RMA-061 v1 contract")
    if data["quality_labels"] != QUALITY:
        raise ContractError(
            "quality_labels must preserve placeholder/manufacturer_estimate/calibrated order"
        )
    if data["command_modes"] != MODES:
        raise ContractError("command_modes differ from the native enum contract")
    if data["command_fields"] != COMMAND_FIELDS:
        raise ContractError("command_fields differ from ServoCommand")
    if data["observation_fields"] != OBSERVATION_FIELDS:
        raise ContractError("observation_fields differ from ServoObservation")
    if data["parameter_fields"] != PARAMETER_FIELDS:
        raise ContractError("parameter_fields differ from ServoParameterSet")
    actual_faults = [(entry.get("name"), entry.get("bit")) for entry in data["fault_flags"]]
    if actual_faults != FAULTS:
        raise ContractError("fault_flags differ from the native fault-bit contract")

    sets = data["parameter_sets"]
    if not isinstance(sets, list) or len(sets) != 3:
        raise ContractError("exactly three role-specific parameter sets are required")
    set_by_id: dict[str, dict[str, Any]] = {}
    roles: list[str] = []
    for index, entry in enumerate(sets):
        label = f"parameter_sets[{index}]"
        exact_keys(
            entry,
            {
                "id",
                "role",
                "overall_quality",
                "source_actuator_class",
                "source_evidence_id",
                "parameters",
                "fault_model",
            },
            label,
        )
        identifier = nonempty(entry["id"], f"{label}.id")
        if identifier in set_by_id:
            raise ContractError(f"duplicate parameter set id: {identifier}")
        if entry["role"] not in ROLES:
            raise ContractError(f"{label}.role is invalid")
        roles.append(entry["role"])
        if entry["overall_quality"] not in QUALITY:
            raise ContractError(f"{label}.overall_quality is invalid")
        nonempty(entry["source_actuator_class"], f"{label}.source_actuator_class")
        nonempty(entry["source_evidence_id"], f"{label}.source_evidence_id")
        parameters = entry["parameters"]
        if not isinstance(parameters, dict):
            raise ContractError(f"{label}.parameters must be an object")
        exact_keys(parameters, set(PARAMETER_FIELDS), f"{label}.parameters")
        for field in PARAMETER_FIELDS:
            validate_scalar(parameters[field], f"{label}.parameters.{field}")
        fault_model = entry["fault_model"]
        exact_keys(
            fault_model,
            {"supported_flags", "latching_flags", "quality", "evidence_id"},
            f"{label}.fault_model",
        )
        supported = fault_model["supported_flags"]
        latching = fault_model["latching_flags"]
        known_faults = [name for name, _ in FAULTS]
        if (
            not isinstance(supported, list)
            or len(supported) != len(set(supported))
            or any(name not in known_faults for name in supported)
        ):
            raise ContractError(f"{label}.fault_model.supported_flags is invalid")
        if (
            not isinstance(latching, list)
            or len(latching) != len(set(latching))
            or any(name not in supported for name in latching)
        ):
            raise ContractError(f"{label}.fault_model.latching_flags is invalid")
        if fault_model["quality"] not in QUALITY:
            raise ContractError(f"{label}.fault_model.quality is invalid")
        nonempty(fault_model["evidence_id"], f"{label}.fault_model.evidence_id")
        if entry["overall_quality"] == "calibrated":
            if fault_model["quality"] != "calibrated":
                raise ContractError(f"{label} calibrated set requires calibrated fault model")
            for field in PARAMETER_FIELDS:
                scalar = parameters[field]
                if scalar["value"] is None or scalar["quality"] != "calibrated":
                    raise ContractError(f"{label} calibrated set is incomplete at {field}")
        set_by_id[identifier] = entry
    if roles != ROLES:
        raise ContractError(f"parameter set roles must be {ROLES}, found {roles}")

    bindings = data["actuator_bindings"]
    if not isinstance(bindings, list) or len(bindings) != len(ACTUATORS):
        raise ContractError("all nine actuators require explicit bindings")
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


def enum_name(value: str) -> str:
    return {
        "placeholder": "ParameterQuality::Placeholder",
        "manufacturer_estimate": "ParameterQuality::ManufacturerEstimate",
        "calibrated": "ParameterQuality::Calibrated",
        "body_yaw": "ActuatorRole::BodyYaw",
        "stewart": "ActuatorRole::Stewart",
        "antenna": "ActuatorRole::Antenna",
    }[value]


def scalar_cpp(value: dict[str, Any]) -> str:
    numeric = value["value"]
    rendered = (
        "std::nullopt" if numeric is None else f"std::optional<double>{{{float(numeric):.17g}}}"
    )
    return f'QualifiedScalar{{{rendered}, {enum_name(value["quality"])}, "{value["evidence_id"]}"}}'


def mask_cpp(names: list[str]) -> str:
    if not names:
        return "0U"
    enum_values = {
        "over_current": "ServoFaultFlag::OverCurrent",
        "over_temperature": "ServoFaultFlag::OverTemperature",
        "under_voltage": "ServoFaultFlag::UnderVoltage",
        "over_voltage": "ServoFaultFlag::OverVoltage",
        "encoder": "ServoFaultFlag::Encoder",
        "communication": "ServoFaultFlag::Communication",
        "model_rejected": "ServoFaultFlag::ModelRejected",
    }
    return " |\n            ".join(f"ToMask({enum_values[name]})" for name in names)


def generate(data: dict[str, Any]) -> str:
    lines = [
        "#ifndef REACHY_SERVO_PARAMETERS_GENERATED_HPP",
        "#define REACHY_SERVO_PARAMETERS_GENERATED_HPP",
        "",
        '#include "reachy_servo_model.hpp"',
        "",
        "#include <array>",
        "#include <optional>",
        "",
        "namespace reachy::servo::generated {",
        "",
        "inline constexpr std::array<ServoParameterSet, 3> kParameterSets{{",
    ]
    for entry in data["parameter_sets"]:
        fault = entry["fault_model"]
        lines.extend(
            [
                "    {",
                f'        "{entry["id"]}",',
                f"        {enum_name(entry['role'])},",
                f"        {enum_name(entry['overall_quality'])},",
                f'        "{entry["source_actuator_class"]}",',
                f'        "{entry["source_evidence_id"]}",',
            ]
        )
        for field in PARAMETER_FIELDS:
            lines.append(f"        {scalar_cpp(entry['parameters'][field])},")
        lines.extend(
            [
                f"        {mask_cpp(fault['supported_flags'])},",
                f"        {mask_cpp(fault['latching_flags'])},",
                f"        {enum_name(fault['quality'])},",
                f'        "{fault["evidence_id"]}",',
                "    },",
            ]
        )
    lines.extend(
        [
            "}};",
            "",
            "inline constexpr std::array<ServoActuatorBinding, 9> kActuatorBindings{{",
        ]
    )
    for binding in data["actuator_bindings"]:
        lines.append(
            f'    {{"{binding["actuator_name"]}", '
            f'"{binding["parameter_set_id"]}", {enum_name(binding["role"])} }},'
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
        default=Path("models/reachy-mini/servo-model-parameters.json"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("native/reachy_sim/src/reachy_servo_parameters.generated.hpp"),
    )
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        data = json.loads(args.profile.read_text(encoding="utf-8"))
        validate_contract(data)
        rendered = generate(data)
        if args.check:
            if not args.output.is_file() or args.output.read_text(encoding="utf-8") != rendered:
                raise ContractError("generated servo parameter header is stale")
            print(f"RMA-061 servo contract is current: {data['contract_id']}")
            return 0
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Generated {args.output}")
        return 0
    except (OSError, json.JSONDecodeError, ContractError) as error:
        print(f"Servo parameter generation failed: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
