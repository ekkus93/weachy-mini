"""RMA-074 approval contracts: schema constants and untrusted-JSON primitives."""

from __future__ import annotations

import json
import math
import re
from datetime import datetime
from pathlib import Path
from typing import Any

CONTRACT_ID = "rma074_unit_calibrated_profile_v1"
SCHEMA_VERSION = 1
HASH_ALGORITHM = "sha256"
SIGNATURE_ALGORITHM = "ed25519"
PROFILE_KIND = "unit_calibrated"
APPROVAL_STATE = "approved"
APPROVAL_POLICY = "rma074_physical_calibration_gate_v1"
MAX_FILE_BYTES = 64 * 1024 * 1024
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
REQUIRED_METRICS = {
    "joint_position",
    "head_position",
    "head_orientation",
    "settling",
    "overshoot",
    "current",
    "free_decay",
    "contact",
}
CORE_APPROVAL_METRICS = {
    "joint_position",
    "head_position",
    "head_orientation",
    "settling",
    "overshoot",
    "free_decay",
}
METRIC_STATUSES = {"passed", "failed", "unsupported"}
BLOCKED_APPROVAL_PUBLIC_KEY_SHA256 = {
    "9ba232b4c60858fe77ef79bf14d1392d089de797d8236ec737c46d494bfdc75c",
}


class ApprovalValidationError(ValueError):
    """Raised when physical calibration approval evidence fails closed."""


def canonical_json_bytes(value: Any) -> bytes:
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


def _error(path: str, message: str) -> ApprovalValidationError:
    return ApprovalValidationError(f"{path}: {message}")


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


def _require_string(value: Any, path: str, maximum: int = 4096) -> str:
    if not isinstance(value, str) or not value:
        raise _error(path, "must be a non-empty string")
    if len(value) > maximum:
        raise _error(path, f"exceeds {maximum} characters")
    return value


def _require_id(value: Any, path: str) -> str:
    text = _require_string(value, path, 128)
    if ID_PATTERN.fullmatch(text) is None:
        raise _error(path, "contains unsupported characters")
    return text


def _require_hash(value: Any, path: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase SHA-256 digest")
    return value


def _require_bool(value: Any, path: str) -> bool:
    if not isinstance(value, bool):
        raise _error(path, "must be boolean")
    return value


def _require_int(
    value: Any,
    path: str,
    *,
    minimum: int = 0,
    maximum: int = 2**63 - 1,
) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise _error(path, "must be an integer")
    if value < minimum or value > maximum:
        raise _error(path, f"must be between {minimum} and {maximum}")
    return value


def _require_number(value: Any, path: str) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise _error(path, "must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise _error(path, "must be finite")
    return number


def _validate_utc(value: Any, path: str) -> str:
    text = _require_string(value, path)
    if not text.endswith("Z"):
        raise _error(path, "must be an RFC 3339 UTC timestamp ending in Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise _error(path, "is not a valid RFC 3339 timestamp") from exc
    if parsed.utcoffset() is None or parsed.utcoffset().total_seconds() != 0:
        raise _error(path, "must use UTC")
    return text


def _strict_object_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ApprovalValidationError(f"JSON object contains duplicate key {key!r}")
        result[key] = value
    return result


def strict_json_loads(text: str) -> Any:
    def reject_constant(token: str) -> None:
        raise ApprovalValidationError(f"JSON contains non-finite constant {token}")

    try:
        return json.loads(
            text,
            parse_constant=reject_constant,
            object_pairs_hook=_strict_object_pairs,
        )
    except json.JSONDecodeError as exc:
        raise ApprovalValidationError(
            f"JSON parse error at line {exc.lineno}, column {exc.colno}: {exc.msg}"
        ) from exc


def load_json_file(path: Path) -> Any:
    raw = path.read_bytes()
    if len(raw) > MAX_FILE_BYTES:
        raise _error(str(path), f"file exceeds {MAX_FILE_BYTES} bytes")
    try:
        text = raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise ApprovalValidationError(f"{path}: file is not valid UTF-8") from exc
    return strict_json_loads(text)
