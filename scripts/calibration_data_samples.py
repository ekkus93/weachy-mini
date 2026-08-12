"""RMA-070 calibration dataset sample-stream validation: per-sample-type contracts."""

from __future__ import annotations

import importlib.util
import math
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_data_contracts (for `SAMPLE_TYPES`,
# `MODES`, and the `_require_*` primitives). It is loaded either as part of
# the calibration_data.py facade's ordered bootstrap (in which case the
# sibling is already in sys.modules) or standalone / directly by path, in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient
# in both cases, check sys.modules first and only fall back to loading the
# sibling by a path relative to this file if it isn't already registered.
if "calibration_data_contracts" in sys.modules:
    calibration_data_contracts = sys.modules["calibration_data_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_data_contracts",
        Path(__file__).with_name("calibration_data_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_data_contracts.py")
    calibration_data_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_data_contracts"] = calibration_data_contracts
    _contracts_spec.loader.exec_module(calibration_data_contracts)

SAMPLE_TYPES = calibration_data_contracts.SAMPLE_TYPES
MODES = calibration_data_contracts.MODES
ImportLimits = calibration_data_contracts.ImportLimits
_error = calibration_data_contracts._error
_require_dict = calibration_data_contracts._require_dict
_require_list = calibration_data_contracts._require_list
_require_exact_keys = calibration_data_contracts._require_exact_keys
_require_string = calibration_data_contracts._require_string
_require_id = calibration_data_contracts._require_id
_require_bool = calibration_data_contracts._require_bool
_require_integer = calibration_data_contracts._require_integer
_require_number = calibration_data_contracts._require_number
_require_nullable_number = calibration_data_contracts._require_nullable_number
_require_vector = calibration_data_contracts._require_vector
_validate_hash = calibration_data_contracts._validate_hash


def _validate_common_sample(
    sample: dict[str, Any],
    path: str,
    limits: ImportLimits,
) -> tuple[int, int]:
    timestamp = _require_integer(
        sample["timestamp_ns"], f"{path}.timestamp_ns", minimum=0, maximum=10**19
    )
    sequence = _require_integer(
        sample["sequence"], f"{path}.sequence", minimum=0, maximum=2**64 - 1
    )
    if "arrival_timestamp_ns" in sample and sample["arrival_timestamp_ns"] is not None:
        _require_integer(
            sample["arrival_timestamp_ns"],
            f"{path}.arrival_timestamp_ns",
            minimum=0,
            maximum=10**19,
        )
    return timestamp, sequence


def _validate_command(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    required = {"timestamp_ns", "sequence", "actuator_id", "mode", "torque_enabled"}
    optional = {
        "arrival_timestamp_ns",
        "target_position_rad",
        "target_velocity_rad_s",
        "profile_velocity_rad_s",
        "profile_acceleration_rad_s2",
        "feedforward_torque_nm",
    }
    _require_exact_keys(sample, required, optional, path)
    _require_id(sample["actuator_id"], f"{path}.actuator_id", limits)
    mode = _require_string(sample["mode"], f"{path}.mode", limits)
    if mode not in MODES:
        raise _error(f"{path}.mode", f"must be one of {sorted(MODES)}")
    _require_bool(sample["torque_enabled"], f"{path}.torque_enabled")
    bounds = {
        "target_position_rad": 100.0,
        "target_velocity_rad_s": 1000.0,
        "profile_velocity_rad_s": 1000.0,
        "profile_acceleration_rad_s2": 100000.0,
        "feedforward_torque_nm": 100000.0,
    }
    for key, maximum in bounds.items():
        if key in sample:
            _require_nullable_number(
                sample[key], f"{path}.{key}", minimum=-maximum, maximum=maximum
            )


def _validate_joint(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {
            "timestamp_ns",
            "sequence",
            "actuator_id",
            "position_rad",
            "velocity_rad_s",
            "applied_torque_nm",
            "fault_flags",
        },
        {"arrival_timestamp_ns"},
        path,
    )
    _require_id(sample["actuator_id"], f"{path}.actuator_id", limits)
    _require_number(sample["position_rad"], f"{path}.position_rad", minimum=-100.0, maximum=100.0)
    _require_number(
        sample["velocity_rad_s"], f"{path}.velocity_rad_s", minimum=-1000.0, maximum=1000.0
    )
    _require_number(
        sample["applied_torque_nm"],
        f"{path}.applied_torque_nm",
        minimum=-100000.0,
        maximum=100000.0,
    )
    _require_integer(sample["fault_flags"], f"{path}.fault_flags", minimum=0, maximum=2**64 - 1)


def _validate_current_load(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {"timestamp_ns", "sequence", "actuator_id", "current_a", "estimated_load_nm"},
        {"arrival_timestamp_ns"},
        path,
    )
    _require_id(sample["actuator_id"], f"{path}.actuator_id", limits)
    _require_number(sample["current_a"], f"{path}.current_a", minimum=-1000.0, maximum=1000.0)
    _require_number(
        sample["estimated_load_nm"],
        f"{path}.estimated_load_nm",
        minimum=-100000.0,
        maximum=100000.0,
    )


def _validate_voltage(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {"timestamp_ns", "sequence", "source_id", "voltage_v"},
        {"arrival_timestamp_ns"},
        path,
    )
    _require_id(sample["source_id"], f"{path}.source_id", limits)
    _require_number(sample["voltage_v"], f"{path}.voltage_v", minimum=0.0, maximum=1000.0)


def _validate_imu(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {"timestamp_ns", "sequence", "sensor_id", "acceleration_m_s2", "angular_velocity_rad_s"},
        {"arrival_timestamp_ns", "orientation_xyzw"},
        path,
    )
    _require_id(sample["sensor_id"], f"{path}.sensor_id", limits)
    _require_vector(
        sample["acceleration_m_s2"], f"{path}.acceleration_m_s2", 3, maximum_absolute=100000.0
    )
    _require_vector(
        sample["angular_velocity_rad_s"],
        f"{path}.angular_velocity_rad_s",
        3,
        maximum_absolute=100000.0,
    )
    if "orientation_xyzw" in sample and sample["orientation_xyzw"] is not None:
        quaternion = _require_vector(
            sample["orientation_xyzw"], f"{path}.orientation_xyzw", 4, maximum_absolute=2.0
        )
        norm = math.sqrt(sum(component * component for component in quaternion))
        if not 0.5 <= norm <= 1.5:
            raise _error(f"{path}.orientation_xyzw", "quaternion norm is outside import bounds")


def _validate_external_pose(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {
            "timestamp_ns",
            "sequence",
            "frame_id",
            "child_frame_id",
            "position_m",
            "orientation_xyzw",
        },
        {"arrival_timestamp_ns", "confidence"},
        path,
    )
    _require_id(sample["frame_id"], f"{path}.frame_id", limits)
    _require_id(sample["child_frame_id"], f"{path}.child_frame_id", limits)
    _require_vector(sample["position_m"], f"{path}.position_m", 3, maximum_absolute=100000.0)
    quaternion = _require_vector(
        sample["orientation_xyzw"], f"{path}.orientation_xyzw", 4, maximum_absolute=2.0
    )
    norm = math.sqrt(sum(component * component for component in quaternion))
    if not 0.5 <= norm <= 1.5:
        raise _error(f"{path}.orientation_xyzw", "quaternion norm is outside import bounds")
    if "confidence" in sample:
        _require_nullable_number(
            sample["confidence"], f"{path}.confidence", minimum=0.0, maximum=1.0
        )


def _validate_force_torque(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {"timestamp_ns", "sequence", "sensor_id", "force_n", "torque_nm"},
        {"arrival_timestamp_ns"},
        path,
    )
    _require_id(sample["sensor_id"], f"{path}.sensor_id", limits)
    _require_vector(sample["force_n"], f"{path}.force_n", 3, maximum_absolute=1_000_000.0)
    _require_vector(sample["torque_nm"], f"{path}.torque_nm", 3, maximum_absolute=1_000_000.0)


def _validate_temperature(sample: dict[str, Any], path: str, limits: ImportLimits) -> None:
    _require_exact_keys(
        sample,
        {"timestamp_ns", "sequence", "sensor_id", "temperature_c"},
        {"arrival_timestamp_ns"},
        path,
    )
    _require_id(sample["sensor_id"], f"{path}.sensor_id", limits)
    _require_number(
        sample["temperature_c"], f"{path}.temperature_c", minimum=-273.15, maximum=1000.0
    )


_SAMPLE_VALIDATORS: dict[str, Callable[[dict[str, Any], str, ImportLimits], None]] = {
    "command": _validate_command,
    "joint": _validate_joint,
    "current_load": _validate_current_load,
    "voltage": _validate_voltage,
    "imu": _validate_imu,
    "external_pose": _validate_external_pose,
    "force_torque": _validate_force_torque,
    "temperature": _validate_temperature,
}


def _validate_streams(
    streams: Any,
    clock_ids: set[str],
    primary_clock_id: str,
    alignments: dict[str, dict[str, Any]],
    limits: ImportLimits,
) -> tuple[int, dict[str, int]]:
    values = _require_list(streams, "streams")
    if not values:
        raise _error("streams", "must contain at least one stream")
    if len(values) > limits.maximum_streams:
        raise _error("streams", "contains too many streams")
    stream_ids: set[str] = set()
    total_samples = 0
    type_counts: dict[str, int] = {}
    for index, raw in enumerate(values):
        path = f"streams[{index}]"
        stream = _require_dict(raw, path)
        _require_exact_keys(
            stream,
            {"stream_id", "sample_type", "clock_id", "samples"},
            {"coordinate_frame", "description"},
            path,
        )
        stream_id = _require_id(stream["stream_id"], f"{path}.stream_id", limits)
        if stream_id in stream_ids:
            raise _error(f"{path}.stream_id", "is duplicated")
        stream_ids.add(stream_id)
        sample_type = _require_string(stream["sample_type"], f"{path}.sample_type", limits)
        if sample_type not in SAMPLE_TYPES:
            raise _error(f"{path}.sample_type", f"must be one of {sorted(SAMPLE_TYPES)}")
        clock_id = _require_id(stream["clock_id"], f"{path}.clock_id", limits)
        if clock_id not in clock_ids:
            raise _error(f"{path}.clock_id", "does not identify a declared clock")
        if clock_id != primary_clock_id and clock_id not in alignments:
            raise _error(path, "uses a non-primary clock without explicit alignment")
        if (
            sample_type in {"imu", "external_pose", "force_torque"}
            and "coordinate_frame" not in stream
        ):
            raise _error(path, "requires coordinate_frame")
        if "coordinate_frame" in stream and stream["coordinate_frame"] is not None:
            _require_id(stream["coordinate_frame"], f"{path}.coordinate_frame", limits)
        if "description" in stream and stream["description"] is not None:
            _require_string(stream["description"], f"{path}.description", limits)
        samples = _require_list(stream["samples"], f"{path}.samples")
        if len(samples) > limits.maximum_samples_per_stream:
            raise _error(f"{path}.samples", "contains too many samples")
        total_samples += len(samples)
        if total_samples > limits.maximum_total_samples:
            raise _error("streams", "contains too many total samples")
        previous_timestamp = -1
        previous_sequence = -1
        validator = _SAMPLE_VALIDATORS[sample_type]
        for sample_index, raw_sample in enumerate(samples):
            sample_path = f"{path}.samples[{sample_index}]"
            sample = _require_dict(raw_sample, sample_path)
            timestamp, sequence = _validate_common_sample(sample, sample_path, limits)
            if timestamp < previous_timestamp:
                raise _error(f"{sample_path}.timestamp_ns", "must be monotonic within its stream")
            if sequence <= previous_sequence:
                raise _error(f"{sample_path}.sequence", "must increase strictly within its stream")
            previous_timestamp = timestamp
            previous_sequence = sequence
            validator(sample, sample_path, limits)
        type_counts[sample_type] = type_counts.get(sample_type, 0) + len(samples)
    return total_samples, type_counts


def _validate_source_files(source_files: Any, limits: ImportLimits) -> None:
    values = _require_list(source_files, "source_files")
    if len(values) > limits.maximum_source_files:
        raise _error("source_files", "contains too many entries")
    names: set[str] = set()
    for index, raw in enumerate(values):
        path = f"source_files[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(value, {"name", "sha256", "media_type", "size_bytes"}, set(), path)
        name = _require_string(value["name"], f"{path}.name", limits)
        if "/" in name or "\\" in name or name in {".", ".."}:
            raise _error(f"{path}.name", "must be a basename without a path")
        if name in names:
            raise _error(f"{path}.name", "is duplicated")
        names.add(name)
        _validate_hash(value["sha256"], f"{path}.sha256")
        _require_string(value["media_type"], f"{path}.media_type", limits)
        _require_integer(
            value["size_bytes"], f"{path}.size_bytes", minimum=0, maximum=limits.maximum_file_bytes
        )
