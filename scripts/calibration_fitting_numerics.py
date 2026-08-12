"""RMA-073 numeric helpers: sample selection, linear least squares, sensitivity."""

from __future__ import annotations

import importlib.util
import math
import statistics
import sys
from collections.abc import Iterable
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends only on `FittingValidationError` from
# calibration_fitting_validation. It is loaded either as part of the
# calibration_fitting.py facade's ordered bootstrap (in which case
# calibration_fitting_validation is already in sys.modules) or standalone /
# directly by path (e.g. future tooling importing this module on its own), in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient in
# both cases, check sys.modules first and only fall back to loading the
# sibling by a path relative to this file if it isn't already registered.
if "calibration_fitting_validation" in sys.modules:
    calibration_fitting_validation = sys.modules["calibration_fitting_validation"]
else:
    _spec = importlib.util.spec_from_file_location(
        "calibration_fitting_validation",
        Path(__file__).with_name("calibration_fitting_validation.py"),
    )
    if _spec is None or _spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_validation.py")
    calibration_fitting_validation = importlib.util.module_from_spec(_spec)
    sys.modules["calibration_fitting_validation"] = calibration_fitting_validation
    _spec.loader.exec_module(calibration_fitting_validation)

FittingValidationError = calibration_fitting_validation.FittingValidationError


def _streams(dataset: dict[str, Any], sample_type: str) -> list[dict[str, Any]]:
    return [stream for stream in dataset["streams"] if stream["sample_type"] == sample_type]


def _samples(
    datasets: Iterable[dict[str, Any]],
    sample_type: str,
    start_ns: int,
    end_ns: int,
    *,
    actuator_id: str | None = None,
) -> list[dict[str, Any]]:
    values: list[dict[str, Any]] = []
    for dataset in datasets:
        for stream in _streams(dataset, sample_type):
            for sample in stream["samples"]:
                timestamp = sample["timestamp_ns"]
                if start_ns <= timestamp <= end_ns:
                    if actuator_id is not None:
                        sample_actuator = sample.get("actuator_id") or sample.get("sensor_id")
                        if sample_actuator != actuator_id:
                            continue
                    values.append(sample)
    values.sort(key=lambda sample: (sample["timestamp_ns"], sample["sequence"]))
    return values


def _nearest_by_timestamp(
    left: list[dict[str, Any]], right: list[dict[str, Any]], *, tolerance_ns: int
) -> list[tuple[dict[str, Any], dict[str, Any]]]:
    if not left or not right:
        return []
    pairs: list[tuple[dict[str, Any], dict[str, Any]]] = []
    j = 0
    for first in left:
        while j + 1 < len(right) and abs(
            right[j + 1]["timestamp_ns"] - first["timestamp_ns"]
        ) <= abs(right[j]["timestamp_ns"] - first["timestamp_ns"]):
            j += 1
        if abs(right[j]["timestamp_ns"] - first["timestamp_ns"]) <= tolerance_ns:
            pairs.append((first, right[j]))
    return pairs


def _solve_linear(
    features: list[list[float]], targets: list[float]
) -> tuple[list[float], dict[str, Any]]:
    if not features or len(features) != len(targets):
        raise FittingValidationError("linear fit requires non-empty aligned observations")
    width = len(features[0])
    if width == 0 or any(len(row) != width for row in features):
        raise FittingValidationError("linear fit feature width is inconsistent")
    if len(features) <= width:
        raise FittingValidationError("linear fit is underdetermined")
    matrix = [[0.0 for _ in range(width + 1)] for _ in range(width)]
    for row, target in zip(features, targets, strict=True):
        for i in range(width):
            for j in range(width):
                matrix[i][j] += row[i] * row[j]
            matrix[i][width] += row[i] * target
    pivots: list[float] = []
    for col in range(width):
        pivot = max(range(col, width), key=lambda row: abs(matrix[row][col]))
        if abs(matrix[pivot][col]) < 1e-14:
            raise FittingValidationError("linear fit design matrix is singular")
        matrix[col], matrix[pivot] = matrix[pivot], matrix[col]
        pivot_value = matrix[col][col]
        pivots.append(abs(pivot_value))
        for entry in range(col, width + 1):
            matrix[col][entry] /= pivot_value
        for row in range(width):
            if row == col:
                continue
            factor = matrix[row][col]
            for entry in range(col, width + 1):
                matrix[row][entry] -= factor * matrix[col][entry]
    coefficients = [matrix[index][width] for index in range(width)]
    predictions = [sum(c * x for c, x in zip(coefficients, row, strict=True)) for row in features]
    residuals = [
        target - prediction for target, prediction in zip(targets, predictions, strict=True)
    ]
    rmse = math.sqrt(sum(value * value for value in residuals) / len(residuals))
    mean_target = statistics.fmean(targets)
    total = sum((value - mean_target) ** 2 for value in targets)
    unexplained = sum(value * value for value in residuals)
    r2 = 1.0 - unexplained / total if total > 0 else (1.0 if unexplained == 0 else 0.0)
    condition_proxy = max(pivots) / min(pivots)
    return coefficients, {
        "observation_count": len(targets),
        "training_rmse": rmse,
        "training_r2": r2,
        "condition_proxy": condition_proxy,
    }


def _confidence(metrics: dict[str, Any], sensitivity: float) -> str:
    count = metrics.get("observation_count", 0)
    r2 = metrics.get("training_r2", 0.0)
    if count >= 40 and r2 >= 0.98 and sensitivity <= 0.05:
        return "high_for_supplied_dataset"
    if count >= 15 and r2 >= 0.9 and sensitivity <= 0.2:
        return "medium_for_supplied_dataset"
    return "low_for_supplied_dataset"


def _jackknife_sensitivity(
    features: list[list[float]], targets: list[float], baseline: list[float]
) -> float:
    if len(features) < max(8, len(baseline) + 3):
        return 1.0
    indexes = sorted({round(i * (len(features) - 1) / 15) for i in range(16)})
    maximum = 0.0
    for omitted in indexes:
        reduced_features = [row for index, row in enumerate(features) if index != omitted]
        reduced_targets = [value for index, value in enumerate(targets) if index != omitted]
        coefficients, _ = _solve_linear(reduced_features, reduced_targets)
        for original, changed in zip(baseline, coefficients, strict=True):
            denominator = max(abs(original), 1e-12)
            maximum = max(maximum, abs(changed - original) / denominator)
    return maximum


def _unsupported(reason: str, required_streams: list[str]) -> dict[str, Any]:
    return {
        "status": "unsupported",
        "reason": reason,
        "required_streams": required_streams,
        "values": {},
        "confidence": "not_estimated",
        "sensitivity": None,
    }
