#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROFILE = ROOT / "models/reachy-mini/power-thermal-baseline.json"
ELECTRICAL = ROOT / "models/reachy-mini/electrical-controller-baseline.json"
OUTPUT = ROOT / "native/reachy_sim/src/reachy_power_thermal_baseline.generated.hpp"

ROLES = {
    "body_yaw": "ActuatorRole::BodyYaw",
    "stewart": "ActuatorRole::Stewart",
    "antenna": "ActuatorRole::Antenna",
}
EVIDENCE = {
    "manufacturer_derived": "PowerThermalEvidenceClass::ManufacturerDerived",
    "engineering_estimate": "PowerThermalEvidenceClass::EngineeringEstimate",
    "calibrated": "PowerThermalEvidenceClass::Calibrated",
}
EXPECTED_UNITS = {
    "shared_supply": {
        "open_circuit_voltage_volts": "V",
        "source_resistance_ohms": "ohm",
        "current_limit_amperes": "A",
        "minimum_bus_voltage_volts": "V",
    },
    "thermal": {
        "winding_resistance_ohms": "ohm",
        "thermal_resistance_celsius_per_watt": "degC/W",
        "thermal_capacitance_joules_per_celsius": "J/degC",
        "warning_temperature_celsius": "degC",
        "shutdown_temperature_celsius": "degC",
        "recovery_temperature_celsius": "degC",
    },
}
EXPECTED_ACTUATORS = [
    "yaw_body",
    *(f"stewart_{i}" for i in range(1, 7)),
    "right_antenna",
    "left_antenna",
]


def fail(message: str) -> None:
    raise ValueError(message)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def validate_scalar(value: dict, name: str) -> None:
    if value.get("evidence_class") == "calibrated":
        fail(f"{name}: calibrated claims are forbidden")
    if value.get("evidence_class") not in EVIDENCE:
        fail(f"{name}: invalid evidence class")
    if not value.get("evidence_id"):
        fail(f"{name}: missing evidence")
    number = value.get("value")
    if not isinstance(number, int | float) or not math.isfinite(number) or number <= 0:
        fail(f"{name}: value must be finite and positive")


def validate(profile: dict, electrical: dict) -> None:
    if (
        profile.get("schema_version") != 1
        or profile.get("contract_id") != "rma064_power_thermal_v1"
    ):
        fail("invalid contract identity")
    if profile.get("overall_evidence_class") == "calibrated":
        fail("calibrated contract claim")
    if profile.get("unit_contract") != EXPECTED_UNITS:
        fail("unit contract drift")
    source = profile.get("source", {})
    required_source = [
        "reachy_commit",
        "model_sha256",
        "hardware_document_path",
        "hardware_document_sha",
        "rma062_validated_commit",
        "rma063_validated_commit",
    ]
    if any(not source.get(key) for key in required_source):
        fail("missing pinned source identity")

    supply = profile.get("shared_supply", {})
    if not supply.get("id") or not supply.get("source_evidence_id"):
        fail("invalid supply identity")
    if supply.get("overall_evidence_class") == "calibrated":
        fail("calibrated supply claim")
    for key in EXPECTED_UNITS["shared_supply"]:
        validate_scalar(supply.get(key, {}), f"shared_supply.{key}")
    if (
        supply["minimum_bus_voltage_volts"]["value"]
        >= supply["open_circuit_voltage_volts"]["value"]
    ):
        fail("invalid supply voltage order")

    baselines = profile.get("thermal_baselines", [])
    if len(baselines) != 3:
        fail("exactly three thermal baselines are required")
    by_id: dict[str, dict] = {}
    fingerprints: set[tuple[float, ...]] = set()
    for baseline in baselines:
        if not baseline.get("id") or not baseline.get("source_evidence_id"):
            fail("invalid thermal baseline identity")
        if baseline.get("role") not in ROLES:
            fail("invalid thermal role")
        if baseline.get("overall_evidence_class") == "calibrated":
            fail("calibrated thermal claim")
        values = []
        for key in EXPECTED_UNITS["thermal"]:
            validate_scalar(baseline.get(key, {}), f"{baseline['id']}.{key}")
            values.append(float(baseline[key]["value"]))
        if not (
            baseline["recovery_temperature_celsius"]["value"]
            < baseline["warning_temperature_celsius"]["value"]
            < baseline["shutdown_temperature_celsius"]["value"]
        ):
            fail("invalid thermal temperature order")
        fingerprint = tuple(values)
        if fingerprint in fingerprints:
            fail("cross-role parameter copy detected")
        fingerprints.add(fingerprint)
        by_id[baseline["id"]] = baseline

    bindings = profile.get("actuator_bindings", [])
    if [item.get("actuator_name") for item in bindings] != EXPECTED_ACTUATORS:
        fail("actuator binding order or count drift")
    for binding in bindings:
        baseline = by_id.get(binding.get("baseline_id"))
        if baseline is None or baseline["role"] != binding.get("role"):
            fail("cross-role or missing actuator binding")

    electrical_by_id = {item["id"]: item for item in electrical.get("baselines", [])}
    electrical_bindings = electrical.get("actuator_bindings", [])
    aggregate_peak = 0.0
    for binding in electrical_bindings:
        aggregate_peak += electrical_by_id[binding["baseline_id"]]["servo_parameters"][
            "peak_current_limit_amperes"
        ]["value"]
    current_limit = supply["current_limit_amperes"]["value"]
    largest_peak = max(
        item["servo_parameters"]["peak_current_limit_amperes"]["value"]
        for item in electrical_by_id.values()
    )
    if not (largest_peak < current_limit < aggregate_peak):
        fail(
            "shared current budget must exceed one servo peak "
            "and remain below aggregate peak demand"
        )

    expected_resistance = {
        "body_yaw": 6.0 / 2.15,
        "stewart": 6.0 / 1.74,
        "antenna": 6.0 / 1.74,
    }
    for baseline in baselines:
        actual = baseline["winding_resistance_ohms"]["value"]
        if not math.isclose(
            actual, expected_resistance[baseline["role"]], rel_tol=0.0, abs_tol=1e-12
        ):
            fail("winding resistance derivation drift")


def cpp_string(value: str) -> str:
    return json.dumps(value)


def scalar(value: dict) -> str:
    return (
        f"PowerThermalScalar{{{value['value']!r}, "
        f"{EVIDENCE[value['evidence_class']]}, "
        f"{cpp_string(value['evidence_id'])}}}"
    )


def render(profile: dict) -> str:
    supply = profile["shared_supply"]
    lines = [
        "#ifndef REACHY_POWER_THERMAL_BASELINE_GENERATED_HPP",
        "#define REACHY_POWER_THERMAL_BASELINE_GENERATED_HPP",
        "",
        '#include "reachy_power_thermal_model.hpp"',
        "",
        "namespace reachy::servo::generated {",
        "",
        "inline constexpr SharedPowerSupplyParameters kSharedPowerSupply{",
        f"    {cpp_string(supply['id'])},",
        f"    {EVIDENCE[supply['overall_evidence_class']]},",
        f"    {cpp_string(supply['source_evidence_id'])},",
    ]
    for key in EXPECTED_UNITS["shared_supply"]:
        lines.append(f"    {scalar(supply[key])},")
    lines += [
        "};",
        "",
        "inline constexpr std::array<ServoThermalParameters, 3> kServoThermalBaselines{{",
    ]
    for baseline in profile["thermal_baselines"]:
        lines += [
            "    ServoThermalParameters{",
            f"        {cpp_string(baseline['id'])},",
            f"        {ROLES[baseline['role']]},",
            f"        {EVIDENCE[baseline['overall_evidence_class']]},",
            f"        {cpp_string(baseline['source_evidence_id'])},",
        ]
        for key in EXPECTED_UNITS["thermal"]:
            lines.append(f"        {scalar(baseline[key])},")
        lines.append("    },")
    lines += [
        "}};",
        "",
        (
            "inline constexpr std::array<ServoActuatorBinding, "
            "kReachyPowerThermalActuatorCount> kPowerThermalBindings{{"
        ),
    ]
    for binding in profile["actuator_bindings"]:
        lines.append(
            "    ServoActuatorBinding{"
            f"{cpp_string(binding['actuator_name'])}, "
            f"{cpp_string(binding['baseline_id'])}, "
            f"{ROLES[binding['role']]}}},"
        )
    lines += ["}};", "", "}  // namespace reachy::servo::generated", "", "#endif", ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    profile = load_json(PROFILE)
    electrical = load_json(ELECTRICAL)
    validate(profile, electrical)
    rendered = render(profile)
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != rendered:
            raise SystemExit("generated power/thermal baseline header is stale")
        print(f"RMA-064 power/thermal baseline is current: {profile['contract_id']}")
        return 0
    OUTPUT.write_text(rendered, encoding="utf-8", newline="\n")
    print(f"wrote {OUTPUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
