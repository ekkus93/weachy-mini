#!/usr/bin/env python3
"""Generate deterministic RMA-070 training/held-out fixtures and an RMA-073 fit plan."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]


def _load_sibling(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_data = _load_sibling("calibration_data", ROOT / "scripts/calibration_data.py")
calibration_fitting = _load_sibling("calibration_fitting", ROOT / "scripts/calibration_fitting.py")

DT_NS = 10_000_000
ACTUATOR_ID = "yaw_body"
WINDOWS = {
    "friction": {"start_ns": 100_000_000, "end_ns": 1_090_000_000},
    "backlash": {"start_ns": 1_200_000_000, "end_ns": 2_190_000_000},
    "latency": {"start_ns": 2_300_000_000, "end_ns": 3_290_000_000},
    "controller": {"start_ns": 3_400_000_000, "end_ns": 4_390_000_000},
    "voltage": {"start_ns": 4_500_000_000, "end_ns": 5_490_000_000},
    "compliance": {"start_ns": 5_600_000_000, "end_ns": 6_590_000_000},
    "thermal": {"start_ns": 6_700_000_000, "end_ns": 10_690_000_000},
}
TRUTH = {
    "coulomb_friction_nm": 0.04,
    "viscous_friction_nm_s_per_rad": 0.006,
    "backlash_half_width_rad": 0.003,
    "command_latency_s": 0.03,
    "position_gain_nm_per_rad": 2.4,
    "velocity_gain_nm_s_per_rad": 0.18,
    "open_circuit_voltage_v": 5.1,
    "source_impedance_ohm": 0.12,
    "compliance_stiffness_nm_per_rad": 30.0,
    "heating_c_per_a2_s": 0.08,
    "cooling_per_s": 0.015,
}
COMPATIBILITY = {
    "reachy_source_commit": "a739a6e461eb6d722901f1cfc225265ffc85c28d",
    "model_sha256": "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46",
    "mujoco_version": "3.9.0",
    "simulator_abi_version": 2,
    "servo_contracts": {
        "servo": "rma061_servo_model_v1",
        "electrical": "rma062_electrical_controller_v1",
        "mechanical": "rma063_mechanical_effects_v1",
        "power_thermal": "rma064_power_thermal_v1",
    },
}


def _noise(index: int, phase: float, scale: float) -> float:
    return scale * math.sin(index * 1.61803398875 + phase)


def _base_dataset(dataset_id: str, created_utc: str, split: str) -> dict[str, Any]:
    registers = {"baud_rate": 1_000_000, "operating_mode": "position", "status_return_level": 2}
    register_hash = hashlib.sha256(calibration_data.canonical_json_bytes(registers)).hexdigest()
    return {
        "schema": calibration_data.schema_descriptor(ROOT / "calibration/schemas"),
        "dataset_id": dataset_id,
        "created_utc": created_utc,
        "robot": {
            "robot_id": "reachy-rma073-synthetic-001",
            "hardware_revision": "reachy-mini-synthetic-fixture",
            "firmware_version": "synthetic-1.0.0",
            "serial_number": None,
            "register_configuration": registers,
            "register_configuration_sha256": register_hash,
        },
        "environment": {
            "ambient_temperature_c": 22.5,
            "relative_humidity_percent": 45.0,
            "pressure_kpa": 101.3,
            "notes": f"Synthetic RMA-073 {split} fixture; not physical calibration evidence.",
        },
        "capture": {
            "tool": "rma073_synthetic_generator",
            "tool_version": "1.0.0",
            "primary_clock_id": "reachy_clock",
            "synchronization_state": "synchronized",
            "operator_notes": (
                "Deterministic synthetic data for fitting infrastructure validation only."
            ),
        },
        "clocks": [
            {
                "clock_id": "reachy_clock",
                "clock_type": "device_monotonic",
                "tick_unit": "nanosecond",
                "epoch_description": "Synthetic monotonic epoch",
                "source": "RMA-073 deterministic generator",
                "nominal_hz": 100.0,
            }
        ],
        "clock_alignments": [],
        "streams": [],
        "source_files": [],
        "integrity": {"algorithm": "sha256"},
    }


def _command(timestamp: int, sequence: int, target: float, velocity: float) -> dict[str, Any]:
    return {
        "timestamp_ns": timestamp,
        "sequence": sequence,
        "actuator_id": ACTUATOR_ID,
        "mode": "position",
        "torque_enabled": True,
        "target_position_rad": target,
        "target_velocity_rad_s": velocity,
        "profile_velocity_rad_s": 1.0,
        "profile_acceleration_rad_s2": 4.0,
        "feedforward_torque_nm": 0.0,
    }


def _joint(
    timestamp: int, sequence: int, position: float, velocity: float, torque: float
) -> dict[str, Any]:
    return {
        "timestamp_ns": timestamp,
        "sequence": sequence,
        "actuator_id": ACTUATOR_ID,
        "position_rad": position,
        "velocity_rad_s": velocity,
        "applied_torque_nm": torque,
        "fault_flags": 0,
    }


def _current(timestamp: int, sequence: int, current: float, load: float) -> dict[str, Any]:
    return {
        "timestamp_ns": timestamp,
        "sequence": sequence,
        "actuator_id": ACTUATOR_ID,
        "current_a": current,
        "estimated_load_nm": load,
    }


def _build_dataset(split: str) -> dict[str, Any]:
    phase = 0.0 if split == "training" else 0.73
    created = "2026-07-31T21:00:00Z" if split == "training" else "2026-07-31T21:01:00Z"
    dataset = _base_dataset(f"rma073-synthetic-{split}-v1", created, split)
    command_samples: list[dict[str, Any]] = []
    joint_samples: list[dict[str, Any]] = []
    current_samples: list[dict[str, Any]] = []
    voltage_samples: list[dict[str, Any]] = []
    temperature_samples: list[dict[str, Any]] = []
    command_sequence = joint_sequence = current_sequence = voltage_sequence = (
        temperature_sequence
    ) = 0

    # Friction: torque = Coulomb*sign(v) + viscous*v.
    window = WINDOWS["friction"]
    for index, timestamp in enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)):
        joint_sequence += 1
        velocity = (0.08 + 0.006 * (index % 20)) * (1.0 if (index // 10) % 2 == 0 else -1.0)
        torque = (
            TRUTH["coulomb_friction_nm"] * math.copysign(1.0, velocity)
            + TRUTH["viscous_friction_nm_s_per_rad"] * velocity
            + _noise(index, phase, 2e-5)
        )
        joint_samples.append(
            _joint(timestamp, joint_sequence, 0.1 * math.sin(index / 10), velocity, torque)
        )

    # Backlash: bidirectional target-position residual.
    window = WINDOWS["backlash"]
    for index, timestamp in enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)):
        direction = 1.0 if (index // 20) % 2 == 0 else -1.0
        local = index % 20
        target = -0.2 + 0.02 * local if direction > 0 else 0.2 - 0.02 * local
        command_sequence += 1
        joint_sequence += 1
        command_samples.append(_command(timestamp, command_sequence, target, direction * 0.2))
        position = (
            target - direction * TRUTH["backlash_half_width_rad"] + _noise(index, phase, 1e-5)
        )
        joint_samples.append(
            _joint(timestamp, joint_sequence, position, direction * 0.2, 0.01 * direction)
        )

    # Latency: 100 ms command steps with a three-sample response delay.
    window = WINDOWS["latency"]
    targets: list[float] = []
    for index, timestamp in enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)):
        target = 0.2 if (index // 10) % 2 else -0.2
        targets.append(target)
        delayed = targets[max(0, index - 3)]
        command_sequence += 1
        joint_sequence += 1
        command_samples.append(_command(timestamp, command_sequence, target, 0.0))
        joint_samples.append(_joint(timestamp, joint_sequence, delayed, 0.0, 0.0))

    # Controller: torque = kp*error - kd*velocity.
    window = WINDOWS["controller"]
    for index, timestamp in enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)):
        target = 0.25 * math.sin(index * 0.17 + phase)
        velocity = 0.35 * math.cos(index * 0.13 + phase)
        error = 0.03 * math.sin(index * 0.31 + 0.4 + phase)
        position = target - error
        torque = (
            TRUTH["position_gain_nm_per_rad"] * error
            - TRUTH["velocity_gain_nm_s_per_rad"] * velocity
            + _noise(index, phase, 4e-5)
        )
        command_sequence += 1
        joint_sequence += 1
        command_samples.append(_command(timestamp, command_sequence, target, 0.0))
        joint_samples.append(_joint(timestamp, joint_sequence, position, velocity, torque))

    # Shared supply voltage sag.
    window = WINDOWS["voltage"]
    voltage_sequence_start = voltage_sequence + 1
    for voltage_sequence, (index, timestamp) in enumerate(
        enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)),
        start=voltage_sequence_start,
    ):
        current = 0.15 + 1.3 * ((index % 25) / 24.0)
        voltage = (
            TRUTH["open_circuit_voltage_v"]
            - TRUTH["source_impedance_ohm"] * current
            + _noise(index, phase, 2e-5)
        )
        current_sequence += 1
        current_samples.append(
            _current(timestamp, current_sequence, current, 0.02 + 0.01 * current)
        )
        voltage_samples.append(
            {
                "timestamp_ns": timestamp,
                "sequence": voltage_sequence,
                "source_id": "servo_bus",
                "voltage_v": voltage,
            }
        )

    # Compliance: load / target-position deflection.
    window = WINDOWS["compliance"]
    for index, timestamp in enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)):
        load = (0.03 + 0.003 * (index % 20)) * (1.0 if (index // 20) % 2 == 0 else -1.0)
        deflection = load / TRUTH["compliance_stiffness_nm_per_rad"]
        target = 0.1 * math.sin(index * 0.1 + phase)
        position = target - deflection + _noise(index, phase, 2e-6)
        command_sequence += 1
        joint_sequence += 1
        current_sequence += 1
        command_samples.append(_command(timestamp, command_sequence, target, 0.0))
        joint_samples.append(_joint(timestamp, joint_sequence, position, 0.0, load))
        current_samples.append(_current(timestamp, current_sequence, 0.2 + abs(load), load))

    # Thermal: dT/dt = h*I^2 - c*(T-ambient).
    window = WINDOWS["thermal"]
    temperature = 22.5
    previous_timestamp: int | None = None
    temperature_sequence_start = temperature_sequence + 1
    for temperature_sequence, (index, timestamp) in enumerate(
        enumerate(range(window["start_ns"], window["end_ns"] + 1, DT_NS)),
        start=temperature_sequence_start,
    ):
        current = (
            0.25 + (1.2 if (index // 50) % 2 == 0 else 0.35) + 0.1 * math.sin(index * 0.07 + phase)
        )
        if previous_timestamp is not None:
            dt = (timestamp - previous_timestamp) / 1e9
            derivative = TRUTH["heating_c_per_a2_s"] * current * current - TRUTH[
                "cooling_per_s"
            ] * (temperature - 22.5)
            temperature += derivative * dt
        previous_timestamp = timestamp
        current_sequence += 1
        current_samples.append(_current(timestamp, current_sequence, current, 0.01 * current))
        temperature_samples.append(
            {
                "timestamp_ns": timestamp,
                "sequence": temperature_sequence,
                "sensor_id": ACTUATOR_ID,
                "temperature_c": temperature,
            }
        )

    dataset["streams"] = [
        {
            "stream_id": "commands",
            "sample_type": "command",
            "clock_id": "reachy_clock",
            "description": f"Synthetic {split} command evidence",
            "samples": command_samples,
        },
        {
            "stream_id": "joints",
            "sample_type": "joint",
            "clock_id": "reachy_clock",
            "description": f"Synthetic {split} joint evidence",
            "samples": joint_samples,
        },
        {
            "stream_id": "current_load",
            "sample_type": "current_load",
            "clock_id": "reachy_clock",
            "description": f"Synthetic {split} current/load evidence",
            "samples": current_samples,
        },
        {
            "stream_id": "voltage",
            "sample_type": "voltage",
            "clock_id": "reachy_clock",
            "description": f"Synthetic {split} voltage evidence",
            "samples": voltage_samples,
        },
        {
            "stream_id": "temperature",
            "sample_type": "temperature",
            "clock_id": "reachy_clock",
            "description": f"Synthetic {split} thermal evidence",
            "samples": temperature_samples,
        },
    ]
    return calibration_data.finalize_dataset(dataset)


def generate_bundle(output_dir: Path) -> dict[str, Any]:
    output_dir.mkdir(parents=True, exist_ok=True)
    training = _build_dataset("training")
    heldout = _build_dataset("heldout")
    calibration_data.validate_dataset(training)
    calibration_data.validate_dataset(heldout)
    calibration_fitting.write_json(output_dir / "training.json", training)
    calibration_fitting.write_json(output_dir / "heldout.json", heldout)
    plan = {
        "contract_id": calibration_fitting.FIT_PLAN_CONTRACT_ID,
        "schema_version": calibration_fitting.FIT_PLAN_SCHEMA_VERSION,
        "schema_sha256": calibration_fitting.FIT_PLAN_SCHEMA_SHA256,
        "plan_id": "rma073-synthetic-fit-plan-v1",
        "created_utc": "2026-07-31T21:02:00Z",
        "profile_id": "rma073-synthetic-fit-candidate-v1",
        "profile_kind": calibration_fitting.PROFILE_KIND,
        "compatibility": COMPATIBILITY,
        "datasets": [
            {
                "dataset_id": training["dataset_id"],
                "role": "fitting",
                "path": "training.json",
                "dataset_sha256": training["integrity"]["dataset_sha256"],
            },
            {
                "dataset_id": heldout["dataset_id"],
                "role": "heldout",
                "path": "heldout.json",
                "dataset_sha256": heldout["integrity"]["dataset_sha256"],
            },
        ],
        "actuator_id": ACTUATOR_ID,
        "windows": WINDOWS,
        "validation_thresholds": {
            "friction": 0.0002,
            "backlash": 0.02,
            "latency": 0.02,
            "controller": 0.0005,
            "voltage": 0.0002,
            "compliance": 0.02,
            "thermal": 0.02,
        },
        "integrity": {"algorithm": "sha256"},
    }
    plan = calibration_fitting.finalize_fit_plan(plan)
    calibration_fitting.validate_fit_plan(plan)
    calibration_fitting.write_json(output_dir / "fit-plan.json", plan)
    truth = {"parameters": TRUTH, "compatibility": COMPATIBILITY, "windows": WINDOWS}
    calibration_fitting.write_json(output_dir / "synthetic-truth.json", truth)
    return {
        "training_dataset_sha256": training["integrity"]["dataset_sha256"],
        "heldout_dataset_sha256": heldout["integrity"]["dataset_sha256"],
        "fit_plan_sha256": plan["integrity"]["plan_sha256"],
        "training_sample_count": calibration_data.validate_dataset(training)["sample_count"],
        "heldout_sample_count": calibration_data.validate_dataset(heldout)["sample_count"],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path)
    args = parser.parse_args()
    summary = generate_bundle(args.output_dir)
    if args.summary_output:
        calibration_fitting.write_json(args.summary_output, summary)
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
