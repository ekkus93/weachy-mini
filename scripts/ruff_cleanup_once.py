"""Apply the reviewed one-time Ruff cleanup transformations."""

from __future__ import annotations

from pathlib import Path


def replace_once(path_text: str, old: str, new: str) -> None:
    path = Path(path_text)
    content = path.read_text(encoding="utf-8")
    count = content.count(old)
    if count != 1:
        raise RuntimeError(
            f"expected exactly one replacement in {path_text}, found {count}"
        )
    path.write_text(content.replace(old, new), encoding="utf-8")
    print(f"updated {path_text}")


def main() -> None:
    replace_once(
        "scripts/calibration_fitting.py",
        '            raise RuntimeError("cannot load sibling calibration_data.py")',
        (
            '            raise RuntimeError("cannot load sibling '
            'calibration_data.py") from None'
        ),
    )

    mechanical_old = (
        '                f"cross-role mechanical parameter copy detected between '
        '{fingerprints[fingerprint]} and {identifier}"'
    )
    mechanical_new = (
        '                "cross-role mechanical parameter copy detected between "\n'
        '                f"{fingerprints[fingerprint]} and {identifier}"'
    )
    replace_once(
        "scripts/generate_reachy_mechanical_baseline.py",
        mechanical_old,
        mechanical_new,
    )

    power_path = "scripts/generate_reachy_power_thermal_baseline.py"
    budget_old = (
        '            "shared current budget must exceed one servo peak and remain '
        'below aggregate peak demand"'
    )
    budget_new = (
        '            "shared current budget must exceed one servo peak "\n'
        '            "and remain below aggregate peak demand"'
    )
    replace_once(power_path, budget_old, budget_new)

    scalar_old = (
        '    return f"PowerThermalScalar{{{value[\'value\']!r}, '
        '{EVIDENCE[value[\'evidence_class\']]}, '
        '{cpp_string(value[\'evidence_id\'])}}}"'
    )
    scalar_new = (
        "    return (\n"
        "        f\"PowerThermalScalar{{{value['value']!r}, \"\n"
        "        f\"{EVIDENCE[value['evidence_class']]}, \"\n"
        "        f\"{cpp_string(value['evidence_id'])}}}\"\n"
        "    )"
    )
    replace_once(power_path, scalar_old, scalar_new)

    bindings_decl_old = (
        '        "inline constexpr std::array<ServoActuatorBinding, '
        'kReachyPowerThermalActuatorCount> kPowerThermalBindings{{",'
    )
    bindings_decl_new = (
        "        (\n"
        '            "inline constexpr std::array<ServoActuatorBinding, "\n'
        '            "kReachyPowerThermalActuatorCount> kPowerThermalBindings{{"\n'
        "        ),"
    )
    replace_once(power_path, bindings_decl_old, bindings_decl_new)

    binding_old = (
        '            f"    ServoActuatorBinding{{'
        "{cpp_string(binding['actuator_name'])}, "
        "{cpp_string(binding['baseline_id'])}, "
        "{ROLES[binding['role']]}}},\""
    )
    binding_new = (
        '            "    ServoActuatorBinding{"\n'
        '            f"{cpp_string(binding[\'actuator_name\'])}, "\n'
        '            f"{cpp_string(binding[\'baseline_id\'])}, "\n'
        '            f"{ROLES[binding[\'role\']]}}},"'
    )
    replace_once(power_path, binding_old, binding_new)

    synthetic_path = "scripts/generate_rma073_synthetic_data.py"
    notes_old = (
        '            "operator_notes": "Deterministic synthetic data for fitting '
        'infrastructure validation only.",'
    )
    notes_new = (
        '            "operator_notes": (\n'
        '                "Deterministic synthetic data for fitting infrastructure "\n'
        '                "validation only."\n'
        "            ),"
    )
    replace_once(synthetic_path, notes_old, notes_new)

    voltage_header_old = (
        '    for index, timestamp in enumerate(range(window["start_ns"], '
        'window["end_ns"] + 1, DT_NS)):'
    )
    voltage_header_new = (
        "    voltage_sequence_start = voltage_sequence + 1\n"
        "    for voltage_sequence, (index, timestamp) in enumerate(\n"
        '        enumerate(range(window["start_ns"], '
        'window["end_ns"] + 1, DT_NS)),\n'
        "        start=voltage_sequence_start,\n"
        "    ):"
    )
    voltage_context_old = (
        '    # Shared supply voltage sag.\n    window = WINDOWS["voltage"]\n'
        + voltage_header_old
    )
    voltage_context_new = (
        '    # Shared supply voltage sag.\n    window = WINDOWS["voltage"]\n'
        + voltage_header_new
    )
    replace_once(synthetic_path, voltage_context_old, voltage_context_new)
    replace_once(
        synthetic_path,
        "        current_sequence += 1\n        voltage_sequence += 1\n",
        "        current_sequence += 1\n",
    )

    thermal_header_old = (
        '    for index, timestamp in enumerate(range(window["start_ns"], '
        'window["end_ns"] + 1, DT_NS)):'
    )
    thermal_header_new = (
        "    temperature_sequence_start = temperature_sequence + 1\n"
        "    for temperature_sequence, (index, timestamp) in enumerate(\n"
        '        enumerate(range(window["start_ns"], '
        'window["end_ns"] + 1, DT_NS)),\n'
        "        start=temperature_sequence_start,\n"
        "    ):"
    )
    thermal_context_old = (
        "    # Thermal: dT/dt = h*I^2 - c*(T-ambient).\n"
        '    window = WINDOWS["thermal"]\n'
        "    temperature = 22.5\n"
        "    previous_timestamp: int | None = None\n"
        + thermal_header_old
    )
    thermal_context_new = (
        "    # Thermal: dT/dt = h*I^2 - c*(T-ambient).\n"
        '    window = WINDOWS["thermal"]\n'
        "    temperature = 22.5\n"
        "    previous_timestamp: int | None = None\n"
        + thermal_header_new
    )
    replace_once(synthetic_path, thermal_context_old, thermal_context_new)
    replace_once(
        synthetic_path,
        "        current_sequence += 1\n        temperature_sequence += 1\n",
        "        current_sequence += 1\n",
    )

    collision_path = "scripts/tests/test_generate_reachy_collision_model.py"
    foot_old = (
        '        \'<body name="body_foot_3dprint"><geom name="visual_base" '
        'type="sphere" size="0.01" contype="0" conaffinity="0"/></body>\','
    )
    foot_new = (
        "        (\n"
        '            \'<body name="body_foot_3dprint">\'\n'
        '            \'<geom name="visual_base" type="sphere" size="0.01" \'\n'
        '            \'contype="0" conaffinity="0"/>\'\n'
        '            "</body>"\n'
        "        ),"
    )
    replace_once(collision_path, foot_old, foot_new)

    down_old = (
        '        \'<body name="body_down_3dprint"><joint name="yaw_body" '
        'type="hinge" range="-2.8 2.8"/><geom name="source_shell" '
        'type="sphere" size="0.02" class="collision"/></body>\','
    )
    down_new = (
        "        (\n"
        '            \'<body name="body_down_3dprint">\'\n'
        '            \'<joint name="yaw_body" type="hinge" '
        'range="-2.8 2.8"/>\'\n'
        '            \'<geom name="source_shell" type="sphere" size="0.02" \'\n'
        '            \'class="collision"/>\'\n'
        '            "</body>"\n'
        "        ),"
    )
    replace_once(collision_path, down_old, down_new)

    default_old = (
        '        \'<default><default class="collision"><geom contype="1" '
        'conaffinity="1"/></default></default>\''
    )
    default_new = (
        '        \'<default><default class="collision">\'\n'
        '        \'<geom contype="1" conaffinity="1"/>\'\n'
        '        "</default></default>"'
    )
    replace_once(collision_path, default_old, default_new)

    replace_once(
        "scripts/tests/test_rma065_report_verifier.py",
        "from verify_rma065_reports import Rma065ReportError, verify_reports",
        (
            "# The scripts directory must be inserted before importing this "
            "standalone verifier.\n"
            "from verify_rma065_reports import Rma065ReportError, "
            "verify_reports  # noqa: E402"
        ),
    )


if __name__ == "__main__":
    main()
