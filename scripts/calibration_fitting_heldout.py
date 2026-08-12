"""RMA-073 held-out validation: prediction-error scoring against frozen fit results."""

from __future__ import annotations

import importlib.util
import math
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_fitting_numerics (for `_samples`/
# `_nearest_by_timestamp`), calibration_fitting_contracts (for
# `PARAMETER_FAMILIES` and `validate_fit_plan`), and calibration_fitting_estimators
# (for `_FITTERS`/`_fit_thermal`) -- and estimators itself depends on numerics
# and contracts, so numerics and contracts must be loaded before estimators.
# It is loaded either as part of the calibration_fitting.py facade's ordered
# bootstrap (in which case all siblings are already in sys.modules) or
# standalone / directly by path, in which case scripts/ is not necessarily on
# sys.path. To be self-sufficient in both cases, check sys.modules first and
# only fall back to loading each sibling by a path relative to this file if it
# isn't already registered.
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

_samples = calibration_fitting_numerics._samples
_nearest_by_timestamp = calibration_fitting_numerics._nearest_by_timestamp

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

if "calibration_fitting_estimators" in sys.modules:
    calibration_fitting_estimators = sys.modules["calibration_fitting_estimators"]
else:
    _estimators_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_estimators",
        Path(__file__).with_name("calibration_fitting_estimators.py"),
    )
    if _estimators_spec is None or _estimators_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_estimators.py")
    calibration_fitting_estimators = importlib.util.module_from_spec(_estimators_spec)
    sys.modules["calibration_fitting_estimators"] = calibration_fitting_estimators
    _estimators_spec.loader.exec_module(calibration_fitting_estimators)

_fit_thermal = calibration_fitting_estimators._fit_thermal
_FITTERS = calibration_fitting_estimators._FITTERS


def _prediction_error(
    family: str,
    result: dict[str, Any],
    datasets: list[dict[str, Any]],
    window: dict[str, int],
    actuator: str,
) -> tuple[float | None, int, str]:
    if result["status"] != "fitted":
        return None, 0, "parameter_not_fitted"
    values = result["values"]
    if family == "friction":
        joints = _samples(datasets, "joint", **window, actuator_id=actuator)
        observations = [sample for sample in joints if abs(float(sample["velocity_rad_s"])) >= 0.03]
        errors = []
        for sample in observations:
            velocity = float(sample["velocity_rad_s"])
            prediction = (
                values["coulomb_friction_nm"] * math.copysign(1.0, velocity)
                + values["viscous_friction_nm_s_per_rad"] * velocity
            )
            errors.append(float(sample["applied_torque_nm"]) - prediction)
        return _rmse(errors), len(errors), "rmse_nm"
    if family == "controller":
        commands = _samples(datasets, "command", **window, actuator_id=actuator)
        joints = _samples(datasets, "joint", **window, actuator_id=actuator)
        pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
        errors = []
        for command, joint in pairs:
            target = command.get("target_position_rad")
            if target is None:
                continue
            prediction = values["position_gain_nm_per_rad"] * (
                float(target) - float(joint["position_rad"])
            ) - values["velocity_gain_nm_s_per_rad"] * float(joint["velocity_rad_s"])
            errors.append(float(joint["applied_torque_nm"]) - prediction)
        return _rmse(errors), len(errors), "rmse_nm"
    if family == "voltage":
        currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
        voltages = _samples(datasets, "voltage", **window)
        pairs = _nearest_by_timestamp(currents, voltages, tolerance_ns=2_000_000)
        errors = [
            float(voltage["voltage_v"])
            - (
                values["open_circuit_voltage_v"]
                - values["source_impedance_ohm"] * float(current["current_a"])
            )
            for current, voltage in pairs
        ]
        return _rmse(errors), len(errors), "rmse_v"
    if family == "thermal":
        temporary = _fit_thermal(datasets, window, actuator)
        if temporary["status"] != "fitted":
            return None, 0, "heldout_estimate_unavailable"
        expected = temporary["values"]
        relative = max(
            abs(values[key] - expected[key]) / max(abs(expected[key]), 1e-12)
            for key in ("heating_c_per_a2_s", "cooling_per_s")
        )
        return (
            relative,
            temporary["metrics"]["observation_count"],
            "maximum_relative_parameter_error",
        )
    temporary = _FITTERS[family](datasets, window, actuator)
    if temporary["status"] != "fitted":
        return None, 0, "heldout_estimate_unavailable"
    key = next(iter(values))
    expected = temporary["values"][key]
    relative = abs(values[key] - expected) / max(abs(expected), 1e-12)
    return relative, temporary["metrics"]["observation_count"], "relative_parameter_error"


def _rmse(errors: list[float]) -> float | None:
    if not errors:
        return None
    return math.sqrt(sum(value * value for value in errors) / len(errors))


def validate_heldout(
    parameter_results: dict[str, dict[str, Any]],
    heldout_datasets: list[dict[str, Any]],
    plan: dict[str, Any],
) -> dict[str, Any]:
    summary = validate_fit_plan(plan)
    family_results: dict[str, dict[str, Any]] = {}
    all_supported_passed = True
    supported_count = 0
    for family in PARAMETER_FAMILIES:
        error, count, metric = _prediction_error(
            family,
            parameter_results[family],
            heldout_datasets,
            summary["windows"][family],
            summary["actuator_id"],
        )
        threshold = summary["thresholds"][family]
        if error is None:
            status = "unsupported"
            passed = None
        else:
            supported_count += 1
            passed = error <= threshold
            status = "passed" if passed else "failed"
            all_supported_passed = all_supported_passed and passed
        family_results[family] = {
            "status": status,
            "metric": metric,
            "value": error,
            "threshold": threshold,
            "observation_count": count,
            "passed": passed,
        }
    return {
        "split_policy": "heldout datasets are loaded only after fitting results are frozen",
        "supported_family_count": supported_count,
        "all_supported_passed": all_supported_passed and supported_count > 0,
        "families": family_results,
    }
