#!/usr/bin/env python3
"""Validate and generate the RMA-062 electrical/controller baseline."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

CONTRACT_ID = "rma062_electrical_controller_v1"
QUALITY = "manufacturer_estimate"
ROLES = ["body_yaw", "stewart", "antenna"]
ACTUATORS = [
    ("yaw_body", "body_yaw"),
    *((f"stewart_{index}", "stewart") for index in range(1, 7)),
    ("right_antenna", "antenna"),
    ("left_antenna", "antenna"),
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
SERVO_FIELDS = [
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
CONTROLLER_FIELDS = [
    "target_position_quantum_radians",
    "target_velocity_quantum_radians_per_second",
    "position_gain_newton_metres_per_radian",
    "velocity_gain_newton_metre_seconds_per_radian",
    "performance_reference_voltage_volts",
]
SERVO_UNITS = {
    "command_sample_period_seconds": "s",
    "command_latency_seconds": "s",
    "encoder_position_quantum_radians": "rad",
    "encoder_velocity_quantum_radians_per_second": "rad/s",
    "continuous_current_limit_amperes": "A",
    "peak_current_limit_amperes": "A",
    "peak_current_duration_seconds": "s",
    "stall_torque_newton_metres": "N*m",
    "no_load_speed_radians_per_second": "rad/s",
    "nominal_voltage_volts": "V",
    "minimum_voltage_volts": "V",
    "maximum_voltage_volts": "V",
    "ambient_temperature_celsius": "degC",
    "warning_temperature_celsius": "degC",
    "shutdown_temperature_celsius": "degC",
}
CONTROLLER_UNITS = {
    "target_position_quantum_radians": "rad",
    "target_velocity_quantum_radians_per_second": "rad/s",
    "position_gain_newton_metres_per_radian": "N*m/rad",
    "velocity_gain_newton_metre_seconds_per_radian": "N*m*s/rad",
    "performance_reference_voltage_volts": "V",
}
EXPECTED_SOURCE = {
    "reachy_commit": "a739a6e461eb6d722901f1cfc225265ffc85c28d",
    "model_sha256": "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46",
    "hardware_document_path": "docs/source/platforms/reachy_mini/hardware.md",
    "hardware_document_sha": "6a41ef2edb40b4b87f655bb7fa8b745a14f60435",
    "hardware_config_path": "src/reachy_mini/assets/config/hardware_config.yaml",
    "hardware_config_sha": "f50fe2ea19c7bd37c00899ba7019960f15524e69",
}


class ContractError(ValueError):
    """Raised when the RMA-062 contract is invalid or overclaims fidelity."""


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


def finite_number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ContractError(f"{label} must be numeric")
    numeric = float(value)
    if not math.isfinite(numeric):
        raise ContractError(f"{label} must be finite")
    return numeric


def validate_scalar(value: Any, label: str, allow_zero: bool = False) -> float:
    if not isinstance(value, dict):
        raise ContractError(f"{label} must be an object")
    exact_keys(value, {"value", "quality", "evidence_id"}, label)
    if value["quality"] != QUALITY:
        raise ContractError(f"{label}.quality must be {QUALITY}")
    nonempty(value["evidence_id"], f"{label}.evidence_id")
    numeric = finite_number(value["value"], f"{label}.value")
    if numeric < 0.0 if allow_zero else numeric <= 0.0:
        qualifier = "non-negative" if allow_zero else "positive"
        raise ContractError(f"{label}.value must be {qualifier}")
    return numeric


def validate_contract(data: dict[str, Any]) -> None:
    exact_keys(
        data,
        {
            "schema_version",
            "contract_id",
            "overall_quality",
            "source",
            "unit_contract",
            "baseline_semantics",
            "fault_flags",
            "baselines",
            "actuator_bindings",
        },
        "root",
    )
    if data["schema_version"] != 1 or data["contract_id"] != CONTRACT_ID:
        raise ContractError("schema_version or contract_id is not the RMA-062 v1 contract")
    if data["overall_quality"] != QUALITY:
        raise ContractError("RMA-062 baseline must remain manufacturer_estimate")

    source = data["source"]
    exact_keys(
        source,
        set(EXPECTED_SOURCE) | {"robotis_manuals"},
        "source",
    )
    for key, expected in EXPECTED_SOURCE.items():
        if source[key] != expected:
            raise ContractError(f"source.{key} differs from the pinned evidence")
    manuals = source["robotis_manuals"]
    exact_keys(manuals, {"xc330_m288_t", "xl330_m288_t", "xl330_m077_t"}, "source.robotis_manuals")
    for key, value in manuals.items():
        url = nonempty(value, f"source.robotis_manuals.{key}")
        if not url.startswith("https://emanual.robotis.com/"):
            raise ContractError(f"source.robotis_manuals.{key} must use the ROBOTIS e-manual")

    units = data["unit_contract"]
    exact_keys(units, {"servo_parameters", "controller_parameters", "conversions"}, "unit_contract")
    if units["servo_parameters"] != SERVO_UNITS:
        raise ContractError("servo parameter units differ from the SI contract")
    if units["controller_parameters"] != CONTROLLER_UNITS:
        raise ContractError("controller parameter units differ from the SI contract")
    conversions = units["conversions"]
    exact_keys(
        conversions, {"encoder_position", "velocity", "no_load_speed"}, "unit_contract.conversions"
    )
    for key, value in conversions.items():
        nonempty(value, f"unit_contract.conversions.{key}")

    semantics = data["baseline_semantics"]
    exact_keys(
        semantics,
        {
            "physics_timestep_seconds",
            "command_update_hz",
            "latency_samples",
            "target_and_encoder_quantization",
            "position_controller",
            "torque_speed_curve",
            "voltage_scaling",
            "current_limit",
            "torque_disable",
            "thermal_scope",
        },
        "baseline_semantics",
    )
    if finite_number(semantics["physics_timestep_seconds"], "physics_timestep_seconds") != 0.002:
        raise ContractError("physics timestep must remain exactly 0.002 seconds")
    if finite_number(semantics["command_update_hz"], "command_update_hz") != 100.0:
        raise ContractError("command update rate must remain the pinned 100 Hz baseline")
    if semantics["latency_samples"] != 1:
        raise ContractError("latency_samples must remain the explicit one-sample estimate")
    for key in (
        "target_and_encoder_quantization",
        "position_controller",
        "torque_speed_curve",
        "voltage_scaling",
        "current_limit",
        "torque_disable",
        "thermal_scope",
    ):
        nonempty(semantics[key], f"baseline_semantics.{key}")

    actual_faults = [(entry.get("name"), entry.get("bit")) for entry in data["fault_flags"]]
    if actual_faults != FAULTS:
        raise ContractError("fault_flags differ from the native fault-bit contract")

    baselines = data["baselines"]
    if not isinstance(baselines, list) or len(baselines) != 3:
        raise ContractError("exactly three role-specific electrical baselines are required")
    by_id: dict[str, dict[str, Any]] = {}
    roles: list[str] = []
    expected_position_quantum = 2.0 * math.pi / 4096.0
    expected_velocity_quantum = 0.229 * 2.0 * math.pi / 60.0
    for index, entry in enumerate(baselines):
        label = f"baselines[{index}]"
        exact_keys(
            entry,
            {
                "id",
                "role",
                "overall_quality",
                "source_actuator_class",
                "source_evidence_id",
                "servo_parameters",
                "fault_model",
                "controller_parameters",
                "upstream_position_pid_raw_p",
                "limitations",
            },
            label,
        )
        identifier = nonempty(entry["id"], f"{label}.id")
        if identifier in by_id:
            raise ContractError(f"duplicate baseline id: {identifier}")
        if entry["role"] not in ROLES:
            raise ContractError(f"{label}.role is invalid")
        roles.append(entry["role"])
        if entry["overall_quality"] != QUALITY:
            raise ContractError(f"{label}.overall_quality must remain noncalibrated")
        nonempty(entry["source_actuator_class"], f"{label}.source_actuator_class")
        nonempty(entry["source_evidence_id"], f"{label}.source_evidence_id")
        if (
            not isinstance(entry["upstream_position_pid_raw_p"], int)
            or entry["upstream_position_pid_raw_p"] <= 0
        ):
            raise ContractError(f"{label}.upstream_position_pid_raw_p must be positive")
        limitations = entry["limitations"]
        if not isinstance(limitations, list) or not limitations:
            raise ContractError(f"{label}.limitations must be non-empty")
        for limitation_index, limitation in enumerate(limitations):
            nonempty(limitation, f"{label}.limitations[{limitation_index}]")

        servo = entry["servo_parameters"]
        exact_keys(servo, set(SERVO_FIELDS), f"{label}.servo_parameters")
        values: dict[str, float] = {}
        for field in SERVO_FIELDS:
            values[field] = validate_scalar(
                servo[field],
                f"{label}.servo_parameters.{field}",
                allow_zero=field == "command_latency_seconds",
            )
        if not (values["continuous_current_limit_amperes"] <= values["peak_current_limit_amperes"]):
            raise ContractError(f"{label} continuous current exceeds peak current")
        if not (
            values["minimum_voltage_volts"]
            <= values["nominal_voltage_volts"]
            <= values["maximum_voltage_volts"]
        ):
            raise ContractError(f"{label} voltage order is invalid")
        if not (
            values["ambient_temperature_celsius"]
            < values["warning_temperature_celsius"]
            < values["shutdown_temperature_celsius"]
        ):
            raise ContractError(f"{label} temperature order is invalid")
        if not math.isclose(
            values["encoder_position_quantum_radians"],
            expected_position_quantum,
            rel_tol=0.0,
            abs_tol=1.0e-15,
        ):
            raise ContractError(f"{label} encoder position conversion is inconsistent")
        if not math.isclose(
            values["encoder_velocity_quantum_radians_per_second"],
            expected_velocity_quantum,
            rel_tol=0.0,
            abs_tol=1.0e-15,
        ):
            raise ContractError(f"{label} encoder velocity conversion is inconsistent")

        fault = entry["fault_model"]
        exact_keys(
            fault,
            {"supported_flags", "latching_flags", "quality", "evidence_id"},
            f"{label}.fault_model",
        )
        known_faults = [name for name, _ in FAULTS]
        supported = fault["supported_flags"]
        latching = fault["latching_flags"]
        if supported != known_faults:
            raise ContractError(f"{label}.fault_model.supported_flags must preserve all flags")
        if (
            not isinstance(latching, list)
            or len(latching) != len(set(latching))
            or any(name not in supported for name in latching)
        ):
            raise ContractError(f"{label}.fault_model.latching_flags is invalid")
        if fault["quality"] != QUALITY:
            raise ContractError(f"{label}.fault_model.quality must be noncalibrated")
        nonempty(fault["evidence_id"], f"{label}.fault_model.evidence_id")

        controller = entry["controller_parameters"]
        exact_keys(controller, set(CONTROLLER_FIELDS), f"{label}.controller_parameters")
        controller_values = {
            field: validate_scalar(controller[field], f"{label}.controller_parameters.{field}")
            for field in CONTROLLER_FIELDS
        }
        if not math.isclose(
            controller_values["target_position_quantum_radians"],
            expected_position_quantum,
            rel_tol=0.0,
            abs_tol=1.0e-15,
        ):
            raise ContractError(f"{label} target position conversion is inconsistent")
        if not math.isclose(
            controller_values["target_velocity_quantum_radians_per_second"],
            expected_velocity_quantum,
            rel_tol=0.0,
            abs_tol=1.0e-15,
        ):
            raise ContractError(f"{label} target velocity conversion is inconsistent")
        reference_voltage = controller_values["performance_reference_voltage_volts"]
        if not (
            values["minimum_voltage_volts"] <= reference_voltage <= values["maximum_voltage_volts"]
        ):
            raise ContractError(
                f"{label} performance reference voltage is outside the accepted range"
            )
        by_id[identifier] = entry
    if roles != ROLES:
        raise ContractError(f"baseline roles must be {ROLES}, found {roles}")

    bindings = data["actuator_bindings"]
    if not isinstance(bindings, list) or len(bindings) != len(ACTUATORS):
        raise ContractError("all nine actuators require explicit electrical bindings")
    for index, ((expected_name, expected_role), binding) in enumerate(
        zip(ACTUATORS, bindings, strict=True)
    ):
        label = f"actuator_bindings[{index}]"
        exact_keys(binding, {"actuator_name", "baseline_id", "role"}, label)
        if binding["actuator_name"] != expected_name or binding["role"] != expected_role:
            raise ContractError(f"{label} identity/order differs from the pinned model")
        baseline = by_id.get(binding["baseline_id"])
        if baseline is None:
            raise ContractError(f"{label} references an unknown baseline")
        if baseline["role"] != expected_role:
            raise ContractError(f"{label} crosses actuator roles")


def role_cpp(role: str) -> str:
    return {
        "body_yaw": "ActuatorRole::BodyYaw",
        "stewart": "ActuatorRole::Stewart",
        "antenna": "ActuatorRole::Antenna",
    }[role]


def quality_cpp() -> str:
    return "ParameterQuality::ManufacturerEstimate"


def string_cpp(value: str) -> str:
    return json.dumps(value, ensure_ascii=True)


def scalar_cpp(value: dict[str, Any]) -> str:
    return (
        "QualifiedScalar{std::optional<double>{"
        f"{float(value['value']):.17g}"
        "}, ParameterQuality::ManufacturerEstimate, "
        f"{string_cpp(value['evidence_id'])}}}"
    )


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
    return " |\n                    ".join(f"ToMask({enum_values[name]})" for name in names)


def generate(data: dict[str, Any]) -> str:
    lines = [
        "#ifndef REACHY_ELECTRICAL_BASELINE_GENERATED_HPP",
        "#define REACHY_ELECTRICAL_BASELINE_GENERATED_HPP",
        "",
        '#include "reachy_electrical_servo_model.hpp"',
        "",
        "#include <array>",
        "#include <optional>",
        "",
        "namespace reachy::servo::generated {",
        "",
        "inline constexpr std::array<ElectricalServoBaseline, 3> kElectricalBaselines{{",
    ]
    for entry in data["baselines"]:
        servo = entry["servo_parameters"]
        fault = entry["fault_model"]
        controller = entry["controller_parameters"]
        lines.extend(
            [
                "    {",
                "        ServoParameterSet{",
                f"            {string_cpp(entry['id'])},",
                f"            {role_cpp(entry['role'])},",
                f"            {quality_cpp()},",
                f"            {string_cpp(entry['source_actuator_class'])},",
                f"            {string_cpp(entry['source_evidence_id'])},",
            ]
        )
        for field in SERVO_FIELDS:
            lines.append(f"            {scalar_cpp(servo[field])},")
        lines.extend(
            [
                f"            {mask_cpp(fault['supported_flags'])},",
                f"            {mask_cpp(fault['latching_flags'])},",
                f"            {quality_cpp()},",
                f"            {string_cpp(fault['evidence_id'])},",
                "        },",
                "        ElectricalControllerParameters{",
                f"            {string_cpp(entry['id'] + '_controller')},",
                f"            {role_cpp(entry['role'])},",
                f"            {quality_cpp()},",
                f"            {string_cpp(entry['source_evidence_id'])},",
            ]
        )
        for field in CONTROLLER_FIELDS:
            lines.append(f"            {scalar_cpp(controller[field])},")
        lines.extend(["        },", "    },"])
    lines.extend(
        [
            "}};",
            "",
            "inline constexpr std::array<ServoActuatorBinding, 9> kElectricalBindings{{",
        ]
    )
    for binding in data["actuator_bindings"]:
        lines.append(
            "    {"
            f"{string_cpp(binding['actuator_name'])}, "
            f"{string_cpp(binding['baseline_id'])}, "
            f"{role_cpp(binding['role'])}"
            "},"
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
        default=Path("models/reachy-mini/electrical-controller-baseline.json"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("native/reachy_sim/src/reachy_electrical_baseline.generated.hpp"),
    )
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        data = json.loads(args.profile.read_text(encoding="utf-8"))
        validate_contract(data)
        rendered = generate(data)
        if args.check:
            if not args.output.is_file() or args.output.read_text(encoding="utf-8") != rendered:
                raise ContractError("generated electrical baseline header is stale")
            print(f"RMA-062 electrical baseline is current: {data['contract_id']}")
            return 0
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Generated {args.output}")
        return 0
    except (OSError, json.JSONDecodeError, ContractError) as error:
        print(f"Electrical baseline generation failed: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
