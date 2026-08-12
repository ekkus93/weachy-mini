"""RMA-072 experiment-plan contracts: schema constants and untrusted-JSON primitives."""

from __future__ import annotations

import copy
import hashlib
import json
import math
import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

PLAN_CONTRACT_ID = "rma072_calibration_experiment_plan_v1"
PLAN_SCHEMA_VERSION = 1
PLAN_HASH_ALGORITHM = "sha256"
PLAN_SCHEMA_SHA256 = "19d53c9b4a45559164d18af6a5ffbcef27375177c56681485c534fe2fbb35b71"
RUN_MANIFEST_CONTRACT_ID = "rma072_calibration_experiment_run_v1"
EXECUTION_ACKNOWLEDGEMENT = "RMA-072 PHYSICAL MOTION AUTHORIZED"
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
EXPERIMENT_TYPES = {
    "unloaded_sweep",
    "gravity_static_pose",
    "step_response",
    "frequency_response",
    "backlash_reversal",
    "free_decay",
    "multi_actuator",
    "thermal_cycle",
}
ACTION_TYPES = {"marker", "torque", "command"}


class ExperimentValidationError(ValueError):
    """Raised when an untrusted experiment plan violates the contract."""


class ExperimentExecutionError(RuntimeError):
    """Raised when execution cannot continue safely."""


@dataclass(frozen=True)
class ImportLimits:
    maximum_file_bytes: int = 8 * 1024 * 1024
    maximum_string_length: int = 4096
    maximum_actuators: int = 32
    maximum_experiments: int = 256
    maximum_schedule_actions: int = 250_000
    maximum_duration_seconds: float = 24 * 60 * 60


DEFAULT_IMPORT_LIMITS = ImportLimits()


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


def _error(path: str, message: str) -> ExperimentValidationError:
    return ExperimentValidationError(f"{path}: {message}")


def _reject_constant(value: str) -> None:
    raise ExperimentValidationError(f"JSON contains non-finite constant {value}")


def _reject_duplicate_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, entry in pairs:
        if key in value:
            raise ExperimentValidationError(f"JSON object contains duplicate key {key!r}")
        value[key] = entry
    return value


def strict_json_loads(text: str) -> Any:
    try:
        return json.loads(
            text,
            parse_constant=_reject_constant,
            object_pairs_hook=_reject_duplicate_pairs,
        )
    except UnicodeDecodeError as exc:
        raise ExperimentValidationError("JSON is not valid UTF-8") from exc
    except json.JSONDecodeError as exc:
        raise ExperimentValidationError(
            f"JSON parse error at line {exc.lineno}, column {exc.colno}: {exc.msg}"
        ) from exc


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


def _validate_utc(value: Any, path: str, limits: ImportLimits) -> None:
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
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
        raise _error(path, "must be a lowercase SHA-256 digest")
    return value


def _position(
    value: Any,
    path: str,
    actuator_id: str,
    actuators: dict[str, dict[str, float]],
) -> float:
    position = _require_number(value, path)
    limits = actuators[actuator_id]
    if position < limits["minimum_position_rad"] or position > limits["maximum_position_rad"]:
        raise _error(path, f"is outside soft limits for {actuator_id}")
    return position


def _positive_duration(value: Any, path: str) -> float:
    return _require_number(value, path, minimum=0.001, maximum=24 * 60 * 60)


def compute_plan_sha256(plan: dict[str, Any]) -> str:
    candidate = copy.deepcopy(plan)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("plan_sha256", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def finalize_plan(plan: dict[str, Any]) -> dict[str, Any]:
    finalized = copy.deepcopy(plan)
    integrity = finalized.setdefault("integrity", {})
    if not isinstance(integrity, dict):
        raise _error("integrity", "must be an object")
    integrity["algorithm"] = PLAN_HASH_ALGORITHM
    integrity["plan_sha256"] = compute_plan_sha256(finalized)
    return finalized


def schema_descriptor(schema_root: Path) -> dict[str, Any]:
    schema_path = schema_root / "calibration-experiment-plan-v1.schema.json"
    actual = hashlib.sha256(schema_path.read_bytes()).hexdigest()
    if actual != PLAN_SCHEMA_SHA256:
        raise ExperimentValidationError(
            "calibration experiment schema drifted without a contract version change"
        )
    return {
        "contract_id": PLAN_CONTRACT_ID,
        "schema_version": PLAN_SCHEMA_VERSION,
        "schema_sha256": actual,
    }
