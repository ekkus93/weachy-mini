"""Versioned calibration dataset validation and canonical hashing."""

from __future__ import annotations

import copy
import hashlib
import json
import math
import re
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

CONTRACT_ID = "rma070_calibration_dataset_v1"
SCHEMA_VERSION = 1
COLUMN_MANIFEST_ID = "rma070_calibration_columns_v1"
HASH_ALGORITHM = "sha256"
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
SAMPLE_TYPES = {
    "command",
    "joint",
    "current_load",
    "voltage",
    "imu",
    "external_pose",
    "force_torque",
    "temperature",
}
CLOCK_TYPES = {
    "host_monotonic",
    "device_monotonic",
    "camera_monotonic",
    "sensor_monotonic",
}
ALIGNMENT_METHODS = {
    "shared_monotonic_clock",
    "hardware_trigger",
    "paired_events_median",
    "manual",
    "unsynchronized",
}
SYNC_STATES = {"synchronized", "partially_synchronized", "unsynchronized"}
MODES = {"disabled", "position", "velocity", "torque"}

TOP_LEVEL_KEYS = {
    "schema",
    "dataset_id",
    "created_utc",
    "robot",
    "environment",
    "capture",
    "clocks",
    "clock_alignments",
    "streams",
    "source_files",
    "integrity",
}


class CalibrationValidationError(ValueError):
    """Raised when untrusted calibration data violates the contract."""


@dataclass(frozen=True)
class ImportLimits:
    """Fail-closed resource limits for untrusted calibration imports."""

    maximum_file_bytes: int = 256 * 1024 * 1024
    maximum_streams: int = 64
    maximum_samples_per_stream: int = 1_000_000
    maximum_total_samples: int = 2_000_000
    maximum_source_files: int = 256
    maximum_register_entries: int = 4096
    maximum_string_length: int = 4096


DEFAULT_LIMITS = ImportLimits()


def _error(path: str, message: str) -> CalibrationValidationError:
    return CalibrationValidationError(f"{path}: {message}")


def _require_dict(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise _error(path, "must be an object")
    return value


def _require_list(value: Any, path: str) -> list[Any]:
    if not isinstance(value, list):
        raise _error(path, "must be an array")
    return value


def _require_exact_keys(
    value: dict[str, Any],
    required: set[str],
    optional: set[str],
    path: str,
) -> None:
    missing = required - value.keys()
    if missing:
        raise _error(path, f"missing keys: {sorted(missing)}")
    unexpected = value.keys() - required - optional
    if unexpected:
        raise _error(path, f"unexpected keys: {sorted(unexpected)}")


def _require_string(value: Any, path: str, limits: ImportLimits) -> str:
    if not isinstance(value, str) or not value:
        raise _error(path, "must be a non-empty string")
    if len(value) > limits.maximum_string_length:
        raise _error(path, "exceeds maximum string length")
    return value


def _require_id(value: Any, path: str, limits: ImportLimits) -> str:
    text = _require_string(value, path, limits)
    if ID_PATTERN.fullmatch(text) is None:
        raise _error(path, "contains unsupported characters or is too long")
    return text


def _require_bool(value: Any, path: str) -> bool:
    if not isinstance(value, bool):
        raise _error(path, "must be boolean")
    return value


def _require_integer(
    value: Any,
    path: str,
    *,
    minimum: int | None = None,
    maximum: int | None = None,
) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise _error(path, "must be an integer")
    if minimum is not None and value < minimum:
        raise _error(path, f"must be >= {minimum}")
    if maximum is not None and value > maximum:
        raise _error(path, f"must be <= {maximum}")
    return value


def _require_number(
    value: Any,
    path: str,
    *,
    minimum: float | None = None,
    maximum: float | None = None,
) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise _error(path, "must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise _error(path, "must be finite")
    if minimum is not None and number < minimum:
        raise _error(path, f"must be >= {minimum}")
    if maximum is not None and number > maximum:
        raise _error(path, f"must be <= {maximum}")
    return number


def _require_nullable_number(
    value: Any,
    path: str,
    *,
    minimum: float | None = None,
    maximum: float | None = None,
) -> float | None:
    if value is None:
        return None
    return _require_number(value, path, minimum=minimum, maximum=maximum)


def _require_vector(
    value: Any,
    path: str,
    length: int,
    *,
    maximum_absolute: float,
) -> list[float]:
    items = _require_list(value, path)
    if len(items) != length:
        raise _error(path, f"must have exactly {length} elements")
    return [
        _require_number(
            item,
            f"{path}[{index}]",
            minimum=-maximum_absolute,
            maximum=maximum_absolute,
        )
        for index, item in enumerate(items)
    ]


def _validate_iso_utc(value: Any, path: str, limits: ImportLimits) -> None:
    text = _require_string(value, path, limits)
    if not text.endswith("Z"):
        raise _error(path, "must be an RFC 3339 UTC timestamp ending in Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise _error(path, "is not a valid RFC 3339 timestamp") from exc
    if parsed.utcoffset() is None or parsed.utcoffset().total_seconds() != 0:
        raise _error(path, "must use UTC")


def _validate_hash(value: Any, path: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase SHA-256 hex digest")
    return value


def _validate_schema(schema: Any, limits: ImportLimits) -> None:
    value = _require_dict(schema, "schema")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "schema_version",
            "schema_sha256",
            "column_manifest_id",
            "column_manifest_sha256",
        },
        set(),
        "schema",
    )
    if _require_string(value["contract_id"], "schema.contract_id", limits) != CONTRACT_ID:
        raise _error("schema.contract_id", f"must equal {CONTRACT_ID}")
    if _require_integer(value["schema_version"], "schema.schema_version") != SCHEMA_VERSION:
        raise _error("schema.schema_version", f"must equal {SCHEMA_VERSION}")
    _validate_hash(value["schema_sha256"], "schema.schema_sha256")
    if (
        _require_string(value["column_manifest_id"], "schema.column_manifest_id", limits)
        != COLUMN_MANIFEST_ID
    ):
        raise _error("schema.column_manifest_id", f"must equal {COLUMN_MANIFEST_ID}")
    _validate_hash(value["column_manifest_sha256"], "schema.column_manifest_sha256")


def _validate_register_value(value: Any, path: str, limits: ImportLimits) -> None:
    if isinstance(value, bool) or value is None:
        return
    if isinstance(value, int):
        return
    if isinstance(value, float):
        _require_number(value, path)
        return
    if isinstance(value, str):
        _require_string(value, path, limits)
        return
    raise _error(path, "must be null, boolean, integer, finite number, or string")


def _validate_robot(robot: Any, limits: ImportLimits) -> None:
    value = _require_dict(robot, "robot")
    _require_exact_keys(
        value,
        {
            "robot_id",
            "hardware_revision",
            "firmware_version",
            "register_configuration",
            "register_configuration_sha256",
        },
        {"serial_number"},
        "robot",
    )
    _require_id(value["robot_id"], "robot.robot_id", limits)
    _require_string(value["hardware_revision"], "robot.hardware_revision", limits)
    _require_string(value["firmware_version"], "robot.firmware_version", limits)
    if "serial_number" in value and value["serial_number"] is not None:
        _require_string(value["serial_number"], "robot.serial_number", limits)
    registers = _require_dict(value["register_configuration"], "robot.register_configuration")
    if len(registers) > limits.maximum_register_entries:
        raise _error("robot.register_configuration", "contains too many entries")
    for key, entry in registers.items():
        _require_id(key, f"robot.register_configuration key {key!r}", limits)
        _validate_register_value(entry, f"robot.register_configuration.{key}", limits)
    expected = _validate_hash(
        value["register_configuration_sha256"],
        "robot.register_configuration_sha256",
    )
    actual = hashlib.sha256(canonical_json_bytes(registers)).hexdigest()
    if expected != actual:
        raise _error("robot.register_configuration_sha256", "does not match register_configuration")


def _validate_environment(environment: Any) -> None:
    value = _require_dict(environment, "environment")
    _require_exact_keys(
        value,
        set(),
        {"ambient_temperature_c", "relative_humidity_percent", "pressure_kpa", "notes"},
        "environment",
    )
    if "ambient_temperature_c" in value:
        _require_nullable_number(
            value["ambient_temperature_c"],
            "environment.ambient_temperature_c",
            minimum=-100.0,
            maximum=200.0,
        )
    if "relative_humidity_percent" in value:
        _require_nullable_number(
            value["relative_humidity_percent"],
            "environment.relative_humidity_percent",
            minimum=0.0,
            maximum=100.0,
        )
    if "pressure_kpa" in value:
        _require_nullable_number(
            value["pressure_kpa"],
            "environment.pressure_kpa",
            minimum=0.0,
            maximum=200.0,
        )
    if "notes" in value and value["notes"] is not None and not isinstance(value["notes"], str):
        raise _error("environment.notes", "must be a string or null")


def _validate_capture(capture: Any, clock_ids: set[str], limits: ImportLimits) -> tuple[str, str]:
    value = _require_dict(capture, "capture")
    _require_exact_keys(
        value,
        {"tool", "tool_version", "primary_clock_id", "synchronization_state"},
        {"operator_notes"},
        "capture",
    )
    _require_id(value["tool"], "capture.tool", limits)
    _require_string(value["tool_version"], "capture.tool_version", limits)
    primary = _require_id(value["primary_clock_id"], "capture.primary_clock_id", limits)
    if primary not in clock_ids:
        raise _error("capture.primary_clock_id", "does not identify a declared clock")
    sync_state = _require_string(
        value["synchronization_state"], "capture.synchronization_state", limits
    )
    if sync_state not in SYNC_STATES:
        raise _error("capture.synchronization_state", f"must be one of {sorted(SYNC_STATES)}")
    if "operator_notes" in value and value["operator_notes"] is not None:
        _require_string(value["operator_notes"], "capture.operator_notes", limits)
    return primary, sync_state


def _validate_clocks(clocks: Any, limits: ImportLimits) -> set[str]:
    values = _require_list(clocks, "clocks")
    if not values:
        raise _error("clocks", "must contain at least one clock")
    clock_ids: set[str] = set()
    for index, raw in enumerate(values):
        path = f"clocks[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {"clock_id", "clock_type", "tick_unit", "epoch_description", "source"},
            {"nominal_hz"},
            path,
        )
        clock_id = _require_id(value["clock_id"], f"{path}.clock_id", limits)
        if clock_id in clock_ids:
            raise _error(f"{path}.clock_id", "is duplicated")
        clock_ids.add(clock_id)
        clock_type = _require_string(value["clock_type"], f"{path}.clock_type", limits)
        if clock_type not in CLOCK_TYPES:
            raise _error(f"{path}.clock_type", f"must be one of {sorted(CLOCK_TYPES)}")
        if value["tick_unit"] != "nanosecond":
            raise _error(f"{path}.tick_unit", "must equal nanosecond")
        _require_string(value["epoch_description"], f"{path}.epoch_description", limits)
        _require_string(value["source"], f"{path}.source", limits)
        if "nominal_hz" in value and value["nominal_hz"] is not None:
            _require_number(
                value["nominal_hz"], f"{path}.nominal_hz", minimum=0.000001, maximum=1e12
            )
    return clock_ids


def _validate_alignments(
    alignments: Any,
    clock_ids: set[str],
    primary_clock_id: str,
    limits: ImportLimits,
) -> tuple[dict[str, dict[str, Any]], str]:
    values = _require_list(alignments, "clock_alignments")
    by_source: dict[str, dict[str, Any]] = {}
    synchronized_count = 0
    unsynchronized_count = 0
    for index, raw in enumerate(values):
        path = f"clock_alignments[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {
                "from_clock_id",
                "to_clock_id",
                "offset_ns",
                "uncertainty_ns",
                "method",
                "sample_count",
                "synchronized",
            },
            set(),
            path,
        )
        source = _require_id(value["from_clock_id"], f"{path}.from_clock_id", limits)
        target = _require_id(value["to_clock_id"], f"{path}.to_clock_id", limits)
        if source not in clock_ids or target not in clock_ids:
            raise _error(path, "references an undeclared clock")
        if source == target:
            raise _error(path, "cannot align a clock to itself")
        if target != primary_clock_id:
            raise _error(f"{path}.to_clock_id", "must target the primary clock")
        if source in by_source:
            raise _error(f"{path}.from_clock_id", "has more than one alignment")
        _require_integer(value["offset_ns"], f"{path}.offset_ns", minimum=-(10**18), maximum=10**18)
        uncertainty = _require_integer(
            value["uncertainty_ns"],
            f"{path}.uncertainty_ns",
            minimum=0,
            maximum=10**18,
        )
        method = _require_string(value["method"], f"{path}.method", limits)
        if method not in ALIGNMENT_METHODS:
            raise _error(f"{path}.method", f"must be one of {sorted(ALIGNMENT_METHODS)}")
        _require_integer(value["sample_count"], f"{path}.sample_count", minimum=0, maximum=10**9)
        synchronized = _require_bool(value["synchronized"], f"{path}.synchronized")
        if method == "unsynchronized" and synchronized:
            raise _error(path, "unsynchronized method cannot claim synchronization")
        if not synchronized and uncertainty == 0:
            raise _error(path, "unsynchronized alignment must report non-zero uncertainty")
        if synchronized:
            synchronized_count += 1
        else:
            unsynchronized_count += 1
        by_source[source] = value
    non_primary = clock_ids - {primary_clock_id}
    missing = non_primary - by_source.keys()
    if missing:
        raise _error(
            "clock_alignments", f"missing alignment to primary clock for {sorted(missing)}"
        )
    if unsynchronized_count and synchronized_count:
        derived = "partially_synchronized"
    elif unsynchronized_count:
        derived = "unsynchronized"
    else:
        derived = "synchronized"
    return by_source, derived


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


def canonical_json_bytes(value: Any) -> bytes:
    """Return deterministic UTF-8 JSON bytes suitable for hashing."""

    return (
        json.dumps(
            value,
            ensure_ascii=False,
            allow_nan=False,
            sort_keys=True,
            separators=(",", ":"),
        )
        + "\n"
    ).encode("utf-8")


def compute_dataset_sha256(dataset: dict[str, Any]) -> str:
    """Hash the complete dataset with the self-referential digest removed."""

    candidate = copy.deepcopy(dataset)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("dataset_sha256", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def finalize_dataset(dataset: dict[str, Any]) -> dict[str, Any]:
    """Return a deep-copied dataset with its canonical SHA-256 populated."""

    finalized = copy.deepcopy(dataset)
    integrity = finalized.setdefault("integrity", {})
    if not isinstance(integrity, dict):
        raise _error("integrity", "must be an object")
    integrity["algorithm"] = HASH_ALGORITHM
    integrity["dataset_sha256"] = compute_dataset_sha256(finalized)
    return finalized


def validate_dataset(
    dataset: Any,
    *,
    limits: ImportLimits = DEFAULT_LIMITS,
    verify_integrity: bool = True,
) -> dict[str, Any]:
    """Validate an untrusted dataset and return a compact deterministic summary."""

    value = _require_dict(dataset, "dataset")
    _require_exact_keys(value, TOP_LEVEL_KEYS, set(), "dataset")
    _validate_schema(value["schema"], limits)
    _require_id(value["dataset_id"], "dataset.dataset_id", limits)
    _validate_iso_utc(value["created_utc"], "dataset.created_utc", limits)
    _validate_robot(value["robot"], limits)
    _validate_environment(value["environment"])
    clock_ids = _validate_clocks(value["clocks"], limits)
    primary_clock_id, declared_sync_state = _validate_capture(value["capture"], clock_ids, limits)
    alignments, derived_sync_state = _validate_alignments(
        value["clock_alignments"],
        clock_ids,
        primary_clock_id,
        limits,
    )
    if declared_sync_state != derived_sync_state:
        raise _error(
            "capture.synchronization_state",
            f"declares {declared_sync_state!r} but alignments derive {derived_sync_state!r}",
        )
    total_samples, type_counts = _validate_streams(
        value["streams"],
        clock_ids,
        primary_clock_id,
        alignments,
        limits,
    )
    _validate_source_files(value["source_files"], limits)
    integrity = _require_dict(value["integrity"], "integrity")
    _require_exact_keys(integrity, {"algorithm", "dataset_sha256"}, set(), "integrity")
    if integrity["algorithm"] != HASH_ALGORITHM:
        raise _error("integrity.algorithm", f"must equal {HASH_ALGORITHM}")
    expected_hash = _validate_hash(integrity["dataset_sha256"], "integrity.dataset_sha256")
    actual_hash = compute_dataset_sha256(value)
    if verify_integrity and expected_hash != actual_hash:
        raise _error("integrity.dataset_sha256", "does not match canonical dataset content")
    return {
        "contract_id": CONTRACT_ID,
        "dataset_id": value["dataset_id"],
        "dataset_sha256": actual_hash,
        "stream_count": len(value["streams"]),
        "sample_count": total_samples,
        "sample_type_counts": dict(sorted(type_counts.items())),
        "clock_count": len(clock_ids),
        "synchronization_state": derived_sync_state,
        "status": "ok",
    }


def _reject_json_constant(value: str) -> None:
    raise CalibrationValidationError(f"JSON contains non-finite constant {value}")


def load_json_file(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> Any:
    """Load bounded strict JSON, rejecting NaN and Infinity."""

    size = path.stat().st_size
    if size > limits.maximum_file_bytes:
        raise _error(str(path), f"file size {size} exceeds {limits.maximum_file_bytes}")
    return json.loads(path.read_text(encoding="utf-8"), parse_constant=_reject_json_constant)


def schema_descriptor(schema_root: Path) -> dict[str, Any]:
    """Return the exact schema identifiers and hashes embedded in datasets."""

    schema_path = schema_root / "calibration-dataset-v1.schema.json"
    columns_path = schema_root / "calibration-stream-columns-v1.json"
    return {
        "contract_id": CONTRACT_ID,
        "schema_version": SCHEMA_VERSION,
        "schema_sha256": hashlib.sha256(schema_path.read_bytes()).hexdigest(),
        "column_manifest_id": COLUMN_MANIFEST_ID,
        "column_manifest_sha256": hashlib.sha256(columns_path.read_bytes()).hexdigest(),
    }
