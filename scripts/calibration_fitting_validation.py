"""RMA-073 generic validation primitives: pure stdlib, no calibration_fitting_* deps."""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass
from datetime import datetime
from typing import Any

ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
GIT_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}(?:[0-9a-f]{24})?$")


class FittingValidationError(ValueError):
    """Raised when untrusted fitting input violates the contract."""


@dataclass(frozen=True)
class ImportLimits:
    maximum_file_bytes: int = 64 * 1024 * 1024
    maximum_string_length: int = 4096
    maximum_datasets: int = 64
    maximum_samples_per_window: int = 1_000_000


DEFAULT_LIMITS = ImportLimits()


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


def _error(path: str, message: str) -> FittingValidationError:
    return FittingValidationError(f"{path}: {message}")


def _require_dict(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise _error(path, "must be an object")
    return value


def _require_list(value: Any, path: str) -> list[Any]:
    if not isinstance(value, list):
        raise _error(path, "must be an array")
    return value


def _require_exact_keys(
    value: dict[str, Any], required: set[str], optional: set[str], path: str
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


def _require_hash(value: Any, path: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase SHA-256 digest")
    return value


def _require_git_commit(value: Any, path: str) -> str:
    if not isinstance(value, str) or GIT_COMMIT_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase 40- or 64-hex Git commit")
    return value


def _validate_utc(value: Any, path: str, limits: ImportLimits) -> str:
    text = _require_string(value, path, limits)
    if not text.endswith("Z"):
        raise _error(path, "must be an RFC 3339 UTC timestamp ending in Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise _error(path, "is not a valid RFC 3339 timestamp") from exc
    if parsed.utcoffset() is None or parsed.utcoffset().total_seconds() != 0:
        raise _error(path, "must use UTC")
    return text
