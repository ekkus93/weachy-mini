"""RMA-073 physical-parameter estimators: the 7 per-family fitters plus dispatch."""

from __future__ import annotations

import importlib.util
import math
import statistics
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_fitting_numerics (for `_streams`/
# `_samples`/`_nearest_by_timestamp`/`_solve_linear`/`_confidence`/
# `_jackknife_sensitivity`/`_unsupported`) and calibration_fitting_contracts
# (for `PARAMETER_FAMILIES` and `validate_fit_plan`). It is loaded either as
# part of the calibration_fitting.py facade's ordered bootstrap (in which case
# both siblings are already in sys.modules) or standalone / directly by path,
# in which case scripts/ is not necessarily on sys.path. To be self-sufficient
# in both cases, check sys.modules first and only fall back to loading each
# sibling by a path relative to this file if it isn't already registered.
if "calibration_fitting_numerics" in sys.modules:
    calibration_fitting_numerics = sys.modules["calibration_fitting_numerics"]
else:
    _numerics_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_numerics",
        Path(__file__).with_name("calibration_fitting_numerics.py"),
    )
    if _numerics_spec is None or _numerics_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_numerics.py")
    calibration_fitting_numerics = importlib.util.module_from_spec(_numerics_spec)
    sys.modules["calibration_fitting_numerics"] = calibration_fitting_numerics
    _numerics_spec.loader.exec_module(calibration_fitting_numerics)

_streams = calibration_fitting_numerics._streams
_samples = calibration_fitting_numerics._samples
_nearest_by_timestamp = calibration_fitting_numerics._nearest_by_timestamp
_solve_linear = calibration_fitting_numerics._solve_linear
_confidence = calibration_fitting_numerics._confidence
_jackknife_sensitivity = calibration_fitting_numerics._jackknife_sensitivity
_unsupported = calibration_fitting_numerics._unsupported

if "calibration_fitting_contracts" in sys.modules:
    calibration_fitting_contracts = sys.modules["calibration_fitting_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_contracts",
        Path(__file__).with_name("calibration_fitting_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_contracts.py")
    calibration_fitting_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_fitting_contracts"] = calibration_fitting_contracts
    _contracts_spec.loader.exec_module(calibration_fitting_contracts)

PARAMETER_FAMILIES = calibration_fitting_contracts.PARAMETER_FAMILIES
validate_fit_plan = calibration_fitting_contracts.validate_fit_plan


def _fit_friction(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    observations = [sample for sample in joints if abs(float(sample["velocity_rad_s"])) >= 0.03]
    if len(observations) < 12:
        return _unsupported("insufficient nonzero-velocity joint samples", ["joint"])
    features = [
        [math.copysign(1.0, sample["velocity_rad_s"]), sample["velocity_rad_s"]]
        for sample in observations
    ]
    targets = [sample["applied_torque_nm"] for sample in observations]
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_coulomb_plus_viscous",
        "values": {
            "coulomb_friction_nm": coefficients[0],
            "viscous_friction_nm_s_per_rad": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["joint"],
    }


def _estimate_backlash(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    positive: list[float] = []
    negative: list[float] = []
    for command, joint in pairs:
        velocity = command.get("target_velocity_rad_s")
        target = command.get("target_position_rad")
        if velocity is None or target is None or abs(velocity) < 1e-9:
            continue
        residual = float(target) - float(joint["position_rad"])
        (positive if velocity > 0 else negative).append(residual)
    if len(positive) < 5 or len(negative) < 5:
        return _unsupported("insufficient bidirectional reversal samples", ["command", "joint"])
    positive_median = statistics.median(positive)
    negative_median = statistics.median(negative)
    half_width = abs(positive_median - negative_median) / 2.0
    deviations = [abs(value - positive_median) for value in positive] + [
        abs(value - negative_median) for value in negative
    ]
    mad = statistics.median(deviations)
    sensitivity = mad / max(half_width, 1e-12)
    return {
        "status": "fitted",
        "method": "bidirectional_residual_median",
        "values": {"backlash_half_width_rad": half_width},
        "metrics": {
            "observation_count": len(positive) + len(negative),
            "direction_positive_count": len(positive),
            "direction_negative_count": len(negative),
            "median_absolute_deviation_rad": mad,
        },
        "confidence": "high_for_supplied_dataset"
        if sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _estimate_latency(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    if len(commands) < 8 or len(joints) < 8:
        return _unsupported("insufficient command and joint samples", ["command", "joint"])
    command_steps: list[tuple[int, float]] = []
    previous: float | None = None
    for sample in commands:
        target = sample.get("target_position_rad")
        if target is None:
            continue
        value = float(target)
        if previous is not None and abs(value - previous) >= 0.05:
            command_steps.append((sample["timestamp_ns"], value - previous))
        previous = value
    response_steps: list[tuple[int, float]] = []
    previous_position: float | None = None
    for sample in joints:
        position = float(sample["position_rad"])
        if previous_position is not None:
            delta = position - previous_position
            if abs(delta) >= 0.02:
                response_steps.append((sample["timestamp_ns"], delta))
        previous_position = position
    latencies: list[int] = []
    for command_time, command_delta in command_steps:
        for response_time, response_delta in response_steps:
            if response_time >= command_time and math.copysign(
                1.0, response_delta
            ) == math.copysign(1.0, command_delta):
                delta = response_time - command_time
                if delta <= 1_000_000_000:
                    latencies.append(delta)
                    break
    if len(latencies) < 3:
        return _unsupported(
            "insufficient matched command-response transitions", ["command", "joint"]
        )
    median_ns = int(statistics.median(latencies))
    mad_ns = statistics.median(abs(value - median_ns) for value in latencies)
    sensitivity = mad_ns / max(median_ns, 1)
    return {
        "status": "fitted",
        "method": "matched_step_transition_median",
        "values": {"command_latency_s": median_ns / 1e9},
        "metrics": {
            "observation_count": len(latencies),
            "median_latency_ns": median_ns,
            "mad_ns": mad_ns,
        },
        "confidence": "high_for_supplied_dataset"
        if len(latencies) >= 5 and sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _fit_controller(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    features: list[list[float]] = []
    targets: list[float] = []
    for command, joint in pairs:
        target_position = command.get("target_position_rad")
        if target_position is None:
            continue
        error = float(target_position) - float(joint["position_rad"])
        features.append([error, -float(joint["velocity_rad_s"])])
        targets.append(float(joint["applied_torque_nm"]))
    if len(features) < 12:
        return _unsupported("insufficient aligned command/joint observations", ["command", "joint"])
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_pd_controller",
        "values": {
            "position_gain_nm_per_rad": coefficients[0],
            "velocity_gain_nm_s_per_rad": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _fit_voltage(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
    voltages = _samples(datasets, "voltage", **window)
    pairs = _nearest_by_timestamp(currents, voltages, tolerance_ns=2_000_000)
    if len(pairs) < 12:
        return _unsupported(
            "insufficient aligned current and voltage observations", ["current_load", "voltage"]
        )
    features = [[1.0, -float(current["current_a"])] for current, _ in pairs]
    targets = [float(voltage["voltage_v"]) for _, voltage in pairs]
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_shared_bus_sag",
        "values": {
            "open_circuit_voltage_v": coefficients[0],
            "source_impedance_ohm": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["current_load", "voltage"],
    }


def _estimate_compliance(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    loads = _samples(datasets, "current_load", **window, actuator_id=actuator)
    command_joint = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    load_by_time = {sample["timestamp_ns"]: sample for sample in loads}
    stiffness_values: list[float] = []
    for command, joint in command_joint:
        target = command.get("target_position_rad")
        if target is None:
            continue
        load = load_by_time.get(command["timestamp_ns"])
        if load is None or load.get("estimated_load_nm") is None:
            continue
        deflection = float(target) - float(joint["position_rad"])
        torque = float(load["estimated_load_nm"])
        if abs(deflection) >= 1e-4 and abs(torque) >= 1e-4 and deflection * torque > 0:
            stiffness_values.append(torque / deflection)
    if len(stiffness_values) < 8:
        return _unsupported(
            "insufficient load/deflection observations", ["command", "joint", "current_load"]
        )
    estimate = statistics.median(stiffness_values)
    mad = statistics.median(abs(value - estimate) for value in stiffness_values)
    sensitivity = mad / max(abs(estimate), 1e-12)
    return {
        "status": "fitted",
        "method": "median_load_over_deflection",
        "values": {"compliance_stiffness_nm_per_rad": estimate},
        "metrics": {
            "observation_count": len(stiffness_values),
            "median_absolute_deviation_nm_per_rad": mad,
        },
        "confidence": "high_for_supplied_dataset"
        if sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint", "current_load"],
    }


def _fit_thermal(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
    temperatures = _samples(datasets, "temperature", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(temperatures, currents, tolerance_ns=2_000_000)
    if len(pairs) < 20:
        return _unsupported(
            "insufficient aligned thermal observations", ["temperature", "current_load"]
        )
    features: list[list[float]] = []
    targets: list[float] = []
    ambient_values: list[float] = []
    for dataset in datasets:
        ambient = dataset.get("environment", {}).get("ambient_temperature_c")
        if ambient is not None:
            ambient_values.append(float(ambient))
    if not ambient_values:
        return _unsupported("ambient temperature is missing", ["temperature", "current_load"])
    ambient = statistics.fmean(ambient_values)
    for index in range(1, len(pairs)):
        previous_temperature, _ = pairs[index - 1]
        temperature, current = pairs[index]
        dt = (temperature["timestamp_ns"] - previous_temperature["timestamp_ns"]) / 1e9
        if dt <= 0:
            continue
        derivative = (
            float(temperature["temperature_c"]) - float(previous_temperature["temperature_c"])
        ) / dt
        previous_value = float(previous_temperature["temperature_c"])
        features.append([float(current["current_a"]) ** 2, -(previous_value - ambient)])
        targets.append(derivative)
    if len(features) < 15:
        return _unsupported(
            "insufficient positive-duration thermal intervals", ["temperature", "current_load"]
        )
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_lumped_thermal",
        "values": {"heating_c_per_a2_s": coefficients[0], "cooling_per_s": coefficients[1]},
        "metrics": metrics | {"ambient_temperature_c": ambient},
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["temperature", "current_load"],
    }


_FITTERS = {
    "friction": _fit_friction,
    "backlash": _estimate_backlash,
    "latency": _estimate_latency,
    "controller": _fit_controller,
    "voltage": _fit_voltage,
    "compliance": _estimate_compliance,
    "thermal": _fit_thermal,
}


def fit_parameters(
    fitting_datasets: list[dict[str, Any]], plan: dict[str, Any]
) -> dict[str, dict[str, Any]]:
    summary = validate_fit_plan(plan)
    actuator = summary["actuator_id"]
    results: dict[str, dict[str, Any]] = {}
    for family in PARAMETER_FAMILIES:
        results[family] = _FITTERS[family](fitting_datasets, summary["windows"][family], actuator)
    return results
