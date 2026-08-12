"""RMA-070 calibration dataset contracts: schema constants and untrusted-JSON primitives."""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass
from datetime import datetime
from typing import Any

CONTRACT_ID = "rma070_calibration_dataset_v1"
SCHEMA_VERSION = 1
COLUMN_MANIFEST_ID = "rma070_calibration_columns_v1"
HASH_ALGORITHM = "sha256"
EXPECTED_SCHEMA_SHA256 = "5268d353bf98f26df840bf3950e02e0dfdd420b575f1595c26c4fcc602548a28"
EXPECTED_COLUMN_MANIFEST_SHA256 = "f3f851734455f2e79408825d58ff641a8469e0dbf1f5ae41c4866b5d6a4f4dc9"
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
    maximum_clocks: int = 64
    maximum_clock_alignments: int = 63
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
