"""Versioned, fail-closed calibration experiment planning and execution."""

from __future__ import annotations

import contextlib
import copy
import hashlib
import json
import math
import re
import time
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Protocol

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


@dataclass(frozen=True)
class ScheduledAction:
    time_ns: int
    action: str
    experiment_id: str
    actuator_id: str | None
    payload: dict[str, Any]

    def to_document(self) -> dict[str, Any]:
        value: dict[str, Any] = {
            "time_ns": self.time_ns,
            "action": self.action,
            "experiment_id": self.experiment_id,
            "payload": copy.deepcopy(self.payload),
        }
        if self.actuator_id is not None:
            value["actuator_id"] = self.actuator_id
        return value


@dataclass(frozen=True)
class CompiledSchedule:
    plan_id: str
    plan_sha256: str
    primary_clock_id: str
    duration_ns: int
    actions: tuple[ScheduledAction, ...]
    experiment_types: tuple[str, ...]
    schedule_sha256: str

    def manifest(self, *, created_utc: str, physical_execution: bool) -> dict[str, Any]:
        _validate_utc(created_utc, "created_utc", DEFAULT_IMPORT_LIMITS)
        return {
            "contract_id": RUN_MANIFEST_CONTRACT_ID,
            "plan_id": self.plan_id,
            "plan_sha256": self.plan_sha256,
            "schedule_sha256": self.schedule_sha256,
            "primary_clock_id": self.primary_clock_id,
            "created_utc": created_utc,
            "duration_ns": self.duration_ns,
            "action_count": len(self.actions),
            "experiment_types": list(self.experiment_types),
            "physical_execution": physical_execution,
        }


@dataclass(frozen=True)
class ExecutionAuthorization:
    plan_sha256: str
    robot_id: str
    acknowledgement: str
    operator_present: bool
    emergency_stop_verified: bool
    workspace_clear: bool
    allow_physical_motion: bool


@dataclass(frozen=True)
class SafetyState:
    bus_voltage_v: float
    maximum_temperature_c: float
    total_current_a: float
    emergency_stop_available: bool
    faulted: bool


class ExperimentAdapter(Protocol):
    """Physical adapter boundary. RMA-074 supplies a real robot implementation."""

    def robot_id(self) -> str: ...

    def begin_run(self, manifest: dict[str, Any]) -> None: ...

    def read_safety_state(self) -> SafetyState: ...

    def record_marker(self, marker: str, experiment_id: str) -> None: ...

    def set_torque(self, actuator_id: str, enabled: bool) -> None: ...

    def submit_command(self, actuator_id: str, command: dict[str, Any]) -> None: ...

    def emergency_stop(self, reason: str) -> None: ...

    def end_run(self, outcome: str) -> None: ...


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


def load_plan_file(path: Path, *, limits: ImportLimits = DEFAULT_IMPORT_LIMITS) -> dict[str, Any]:
    size = path.stat().st_size
    if size > limits.maximum_file_bytes:
        raise _error(str(path), f"file size {size} exceeds {limits.maximum_file_bytes}")
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise ExperimentValidationError(f"{path}: file is not valid UTF-8") from exc
    value = strict_json_loads(text)
    if not isinstance(value, dict):
        raise _error("plan", "must be an object")
    validate_plan(value, limits=limits)
    return value


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


def _validate_experiment(
    raw: Any,
    index: int,
    *,
    actuators: dict[str, dict[str, float]],
    maximum_concurrent: int,
    timing_rate_hz: float,
    limits: ImportLimits,
) -> tuple[str, str]:
    path = f"experiments[{index}]"
    value = _require_dict(raw, path)
    experiment_id = _require_id(value.get("experiment_id"), f"{path}.experiment_id", limits)
    experiment_type = _require_string(value.get("type"), f"{path}.type", limits)
    if experiment_type not in EXPERIMENT_TYPES:
        raise _error(f"{path}.type", f"must be one of {sorted(EXPERIMENT_TYPES)}")

    common = {"experiment_id", "type"}
    if experiment_type == "unloaded_sweep":
        required = common | {
            "actuator_id",
            "start_position_rad",
            "end_position_rad",
            "sweep_seconds",
            "repetitions",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        start = _position(
            value["start_position_rad"], f"{path}.start_position_rad", actuator, actuators
        )
        end = _position(value["end_position_rad"], f"{path}.end_position_rad", actuator, actuators)
        if start == end:
            raise _error(path, "sweep endpoints must differ")
        _positive_duration(value["sweep_seconds"], f"{path}.sweep_seconds")
        _require_integer(value["repetitions"], f"{path}.repetitions", minimum=1, maximum=1000)
    elif experiment_type == "gravity_static_pose":
        required = common | {"positions_rad", "hold_seconds", "gravity_loaded"}
        _require_exact_keys(value, required, set(), path)
        if _require_bool(value["gravity_loaded"], f"{path}.gravity_loaded") is not True:
            raise _error(f"{path}.gravity_loaded", "must be true for this experiment type")
        positions = _require_dict(value["positions_rad"], f"{path}.positions_rad")
        if not positions:
            raise _error(f"{path}.positions_rad", "must not be empty")
        if len(positions) > maximum_concurrent:
            raise _error(f"{path}.positions_rad", "exceeds maximum concurrent actuators")
        for actuator, position in positions.items():
            _require_id(actuator, f"{path}.positions_rad key", limits)
            if actuator not in actuators:
                raise _error(f"{path}.positions_rad.{actuator}", "undeclared actuator")
            _position(position, f"{path}.positions_rad.{actuator}", actuator, actuators)
        _positive_duration(value["hold_seconds"], f"{path}.hold_seconds")
    elif experiment_type == "step_response":
        required = common | {
            "actuator_id",
            "initial_position_rad",
            "target_position_rad",
            "pre_hold_seconds",
            "post_hold_seconds",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        initial = _position(
            value["initial_position_rad"], f"{path}.initial_position_rad", actuator, actuators
        )
        target = _position(
            value["target_position_rad"], f"{path}.target_position_rad", actuator, actuators
        )
        if initial == target:
            raise _error(path, "step endpoints must differ")
        _positive_duration(value["pre_hold_seconds"], f"{path}.pre_hold_seconds")
        _positive_duration(value["post_hold_seconds"], f"{path}.post_hold_seconds")
    elif experiment_type == "frequency_response":
        required = common | {
            "actuator_id",
            "center_position_rad",
            "amplitude_rad",
            "frequencies_hz",
            "cycles_per_frequency",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        center = _position(
            value["center_position_rad"], f"{path}.center_position_rad", actuator, actuators
        )
        amplitude = _require_number(value["amplitude_rad"], f"{path}.amplitude_rad", minimum=1e-9)
        _position(center - amplitude, f"{path}.center_position_rad-amplitude", actuator, actuators)
        _position(center + amplitude, f"{path}.center_position_rad+amplitude", actuator, actuators)
        frequencies = _require_list(value["frequencies_hz"], f"{path}.frequencies_hz")
        if not frequencies:
            raise _error(f"{path}.frequencies_hz", "must not be empty")
        if len(frequencies) > 128:
            raise _error(f"{path}.frequencies_hz", "contains too many frequencies")
        previous = 0.0
        for frequency_index, raw_frequency in enumerate(frequencies):
            frequency = _require_number(
                raw_frequency,
                f"{path}.frequencies_hz[{frequency_index}]",
                minimum=0.001,
                maximum=timing_rate_hz / 4.0,
            )
            if frequency <= previous:
                raise _error(f"{path}.frequencies_hz", "must increase strictly")
            previous = frequency
        _require_integer(
            value["cycles_per_frequency"],
            f"{path}.cycles_per_frequency",
            minimum=1,
            maximum=10000,
        )
    elif experiment_type == "backlash_reversal":
        required = common | {
            "actuator_id",
            "center_position_rad",
            "amplitude_rad",
            "dwell_seconds",
            "repetitions",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        center = _position(
            value["center_position_rad"], f"{path}.center_position_rad", actuator, actuators
        )
        amplitude = _require_number(value["amplitude_rad"], f"{path}.amplitude_rad", minimum=1e-9)
        _position(center - amplitude, f"{path}.center_position_rad-amplitude", actuator, actuators)
        _position(center + amplitude, f"{path}.center_position_rad+amplitude", actuator, actuators)
        _positive_duration(value["dwell_seconds"], f"{path}.dwell_seconds")
        _require_integer(value["repetitions"], f"{path}.repetitions", minimum=1, maximum=10000)
    elif experiment_type == "free_decay":
        required = common | {
            "actuator_id",
            "initial_position_rad",
            "settle_seconds",
            "observe_seconds",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        _position(
            value["initial_position_rad"], f"{path}.initial_position_rad", actuator, actuators
        )
        _positive_duration(value["settle_seconds"], f"{path}.settle_seconds")
        _positive_duration(value["observe_seconds"], f"{path}.observe_seconds")
    elif experiment_type == "multi_actuator":
        required = common | {"positions_rad", "hold_seconds"}
        _require_exact_keys(value, required, set(), path)
        positions = _require_dict(value["positions_rad"], f"{path}.positions_rad")
        if len(positions) < 2:
            raise _error(f"{path}.positions_rad", "must contain at least two actuators")
        if len(positions) > maximum_concurrent:
            raise _error(f"{path}.positions_rad", "exceeds maximum concurrent actuators")
        for actuator, position in positions.items():
            _require_id(actuator, f"{path}.positions_rad key", limits)
            if actuator not in actuators:
                raise _error(f"{path}.positions_rad.{actuator}", "undeclared actuator")
            _position(position, f"{path}.positions_rad.{actuator}", actuator, actuators)
        _positive_duration(value["hold_seconds"], f"{path}.hold_seconds")
    else:
        required = common | {
            "actuator_id",
            "cold_hold_seconds",
            "lower_position_rad",
            "upper_position_rad",
            "dwell_seconds",
            "warm_cycles",
            "cooldown_seconds",
        }
        _require_exact_keys(value, required, set(), path)
        actuator = _require_id(value["actuator_id"], f"{path}.actuator_id", limits)
        if actuator not in actuators:
            raise _error(f"{path}.actuator_id", "does not identify a declared actuator")
        lower = _position(
            value["lower_position_rad"], f"{path}.lower_position_rad", actuator, actuators
        )
        upper = _position(
            value["upper_position_rad"], f"{path}.upper_position_rad", actuator, actuators
        )
        if lower == upper:
            raise _error(path, "thermal-cycle endpoints must differ")
        _positive_duration(value["cold_hold_seconds"], f"{path}.cold_hold_seconds")
        _positive_duration(value["dwell_seconds"], f"{path}.dwell_seconds")
        _positive_duration(value["cooldown_seconds"], f"{path}.cooldown_seconds")
        _require_integer(value["warm_cycles"], f"{path}.warm_cycles", minimum=1, maximum=10000)

    return experiment_id, experiment_type


def validate_plan(
    plan: Any,
    *,
    limits: ImportLimits = DEFAULT_IMPORT_LIMITS,
    verify_integrity: bool = True,
) -> dict[str, Any]:
    value = _require_dict(plan, "plan")
    _require_exact_keys(
        value,
        {
            "schema",
            "plan_id",
            "description",
            "robot",
            "timing",
            "safety_limits",
            "actuators",
            "experiments",
            "integrity",
        },
        set(),
        "plan",
    )

    schema = _require_dict(value["schema"], "schema")
    _require_exact_keys(
        schema,
        {"contract_id", "schema_version", "schema_sha256"},
        set(),
        "schema",
    )
    if schema["contract_id"] != PLAN_CONTRACT_ID:
        raise _error("schema.contract_id", f"must equal {PLAN_CONTRACT_ID}")
    if schema["schema_version"] != PLAN_SCHEMA_VERSION:
        raise _error("schema.schema_version", f"must equal {PLAN_SCHEMA_VERSION}")
    if _validate_hash(schema["schema_sha256"], "schema.schema_sha256") != PLAN_SCHEMA_SHA256:
        raise _error("schema.schema_sha256", "does not match the pinned version-1 schema")

    plan_id = _require_id(value["plan_id"], "plan.plan_id", limits)
    _require_string(value["description"], "plan.description", limits)

    robot = _require_dict(value["robot"], "robot")
    _require_exact_keys(
        robot,
        {"expected_robot_id", "hardware_revision", "firmware_constraint"},
        set(),
        "robot",
    )
    _require_id(robot["expected_robot_id"], "robot.expected_robot_id", limits)
    _require_string(robot["hardware_revision"], "robot.hardware_revision", limits)
    _require_string(robot["firmware_constraint"], "robot.firmware_constraint", limits)

    timing = _require_dict(value["timing"], "timing")
    _require_exact_keys(timing, {"command_rate_hz", "primary_clock_id"}, set(), "timing")
    command_rate_hz = _require_number(
        timing["command_rate_hz"], "timing.command_rate_hz", minimum=1.0, maximum=2000.0
    )
    _require_id(timing["primary_clock_id"], "timing.primary_clock_id", limits)

    safety = _require_dict(value["safety_limits"], "safety_limits")
    _require_exact_keys(
        safety,
        {
            "maximum_duration_seconds",
            "maximum_schedule_actions",
            "maximum_temperature_c",
            "minimum_bus_voltage_v",
            "maximum_total_current_a",
            "maximum_concurrent_actuators",
        },
        set(),
        "safety_limits",
    )
    maximum_duration_seconds = _require_number(
        safety["maximum_duration_seconds"],
        "safety_limits.maximum_duration_seconds",
        minimum=0.1,
        maximum=limits.maximum_duration_seconds,
    )
    maximum_schedule_actions = _require_integer(
        safety["maximum_schedule_actions"],
        "safety_limits.maximum_schedule_actions",
        minimum=1,
        maximum=limits.maximum_schedule_actions,
    )
    _require_number(
        safety["maximum_temperature_c"],
        "safety_limits.maximum_temperature_c",
        minimum=-273.15,
        maximum=200.0,
    )
    _require_number(
        safety["minimum_bus_voltage_v"],
        "safety_limits.minimum_bus_voltage_v",
        minimum=0.0,
        maximum=1000.0,
    )
    _require_number(
        safety["maximum_total_current_a"],
        "safety_limits.maximum_total_current_a",
        minimum=0.001,
        maximum=1000.0,
    )
    maximum_concurrent = _require_integer(
        safety["maximum_concurrent_actuators"],
        "safety_limits.maximum_concurrent_actuators",
        minimum=1,
        maximum=limits.maximum_actuators,
    )

    raw_actuators = _require_dict(value["actuators"], "actuators")
    if not raw_actuators:
        raise _error("actuators", "must not be empty")
    if len(raw_actuators) > limits.maximum_actuators:
        raise _error("actuators", "contains too many actuators")
    actuators: dict[str, dict[str, float]] = {}
    for actuator_id, raw_actuator in raw_actuators.items():
        _require_id(actuator_id, f"actuators key {actuator_id!r}", limits)
        actuator = _require_dict(raw_actuator, f"actuators.{actuator_id}")
        _require_exact_keys(
            actuator,
            {
                "minimum_position_rad",
                "maximum_position_rad",
                "maximum_velocity_rad_s",
            },
            set(),
            f"actuators.{actuator_id}",
        )
        minimum = _require_number(
            actuator["minimum_position_rad"],
            f"actuators.{actuator_id}.minimum_position_rad",
            minimum=-100.0,
            maximum=100.0,
        )
        maximum = _require_number(
            actuator["maximum_position_rad"],
            f"actuators.{actuator_id}.maximum_position_rad",
            minimum=-100.0,
            maximum=100.0,
        )
        if minimum >= maximum:
            raise _error(f"actuators.{actuator_id}", "minimum must be less than maximum")
        velocity = _require_number(
            actuator["maximum_velocity_rad_s"],
            f"actuators.{actuator_id}.maximum_velocity_rad_s",
            minimum=1e-6,
            maximum=1000.0,
        )
        actuators[actuator_id] = {
            "minimum_position_rad": minimum,
            "maximum_position_rad": maximum,
            "maximum_velocity_rad_s": velocity,
        }

    raw_experiments = _require_list(value["experiments"], "experiments")
    if not raw_experiments:
        raise _error("experiments", "must not be empty")
    if len(raw_experiments) > limits.maximum_experiments:
        raise _error("experiments", "contains too many experiments")
    experiment_ids: set[str] = set()
    experiment_types: set[str] = set()
    for index, raw in enumerate(raw_experiments):
        experiment_id, experiment_type = _validate_experiment(
            raw,
            index,
            actuators=actuators,
            maximum_concurrent=maximum_concurrent,
            timing_rate_hz=command_rate_hz,
            limits=limits,
        )
        if experiment_id in experiment_ids:
            raise _error(f"experiments[{index}].experiment_id", "is duplicated")
        experiment_ids.add(experiment_id)
        experiment_types.add(experiment_type)

    integrity = _require_dict(value["integrity"], "integrity")
    _require_exact_keys(integrity, {"algorithm", "plan_sha256"}, set(), "integrity")
    if integrity["algorithm"] != PLAN_HASH_ALGORITHM:
        raise _error("integrity.algorithm", f"must equal {PLAN_HASH_ALGORITHM}")
    expected_hash = _validate_hash(integrity["plan_sha256"], "integrity.plan_sha256")
    actual_hash = compute_plan_sha256(value)
    if verify_integrity and expected_hash != actual_hash:
        raise _error("integrity.plan_sha256", "does not match canonical plan content")

    compiled = compile_plan(value, validate=False)
    if compiled.duration_ns > round(maximum_duration_seconds * 1e9):
        raise _error("experiments", "compiled duration exceeds safety limit")
    if len(compiled.actions) > maximum_schedule_actions:
        raise _error("experiments", "compiled action count exceeds safety limit")

    return {
        "contract_id": PLAN_CONTRACT_ID,
        "plan_id": plan_id,
        "plan_sha256": actual_hash,
        "experiment_count": len(raw_experiments),
        "experiment_types": sorted(experiment_types),
        "action_count": len(compiled.actions),
        "duration_ns": compiled.duration_ns,
        "status": "ok",
    }


class _ScheduleBuilder:
    def __init__(self, plan: dict[str, Any]) -> None:
        self.plan = plan
        self.cursor_ns = 0
        self.actions: list[ScheduledAction] = []
        self.command_rate_hz = float(plan["timing"]["command_rate_hz"])
        self.used_actuators: set[str] = set()

    def marker(self, experiment_id: str, marker: str) -> None:
        self.actions.append(
            ScheduledAction(
                time_ns=self.cursor_ns,
                action="marker",
                experiment_id=experiment_id,
                actuator_id=None,
                payload={"marker": marker},
            )
        )

    def torque(self, experiment_id: str, actuator_id: str, enabled: bool) -> None:
        self.used_actuators.add(actuator_id)
        self.actions.append(
            ScheduledAction(
                time_ns=self.cursor_ns,
                action="torque",
                experiment_id=experiment_id,
                actuator_id=actuator_id,
                payload={"enabled": enabled},
            )
        )

    def command(self, experiment_id: str, actuator_id: str, position_rad: float) -> None:
        self.used_actuators.add(actuator_id)
        self.actions.append(
            ScheduledAction(
                time_ns=self.cursor_ns,
                action="command",
                experiment_id=experiment_id,
                actuator_id=actuator_id,
                payload={
                    "mode": "position",
                    "torque_enabled": True,
                    "target_position_rad": position_rad,
                    "profile_velocity_rad_s": self.plan["actuators"][actuator_id][
                        "maximum_velocity_rad_s"
                    ],
                },
            )
        )

    def advance(self, seconds: float) -> None:
        self.cursor_ns += round(seconds * 1e9)

    def line(
        self, experiment_id: str, actuator_id: str, start: float, end: float, seconds: float
    ) -> None:
        steps = max(1, math.ceil(seconds * self.command_rate_hz))
        start_ns = self.cursor_ns
        duration_ns = round(seconds * 1e9)
        for index in range(steps + 1):
            self.cursor_ns = start_ns + round(duration_ns * index / steps)
            alpha = index / steps
            self.command(experiment_id, actuator_id, start + (end - start) * alpha)
        self.cursor_ns = start_ns + duration_ns

    def sine(
        self,
        experiment_id: str,
        actuator_id: str,
        center: float,
        amplitude: float,
        frequency_hz: float,
        cycles: int,
    ) -> None:
        seconds = cycles / frequency_hz
        steps = max(4 * cycles, math.ceil(seconds * self.command_rate_hz))
        start_ns = self.cursor_ns
        duration_ns = round(seconds * 1e9)
        for index in range(steps + 1):
            self.cursor_ns = start_ns + round(duration_ns * index / steps)
            phase = 2.0 * math.pi * cycles * index / steps
            self.command(
                experiment_id,
                actuator_id,
                center + amplitude * math.sin(phase),
            )
        self.cursor_ns = start_ns + duration_ns


def compile_plan(plan: dict[str, Any], *, validate: bool = True) -> CompiledSchedule:
    if validate:
        validate_plan(plan)
    builder = _ScheduleBuilder(plan)
    experiment_types: list[str] = []

    for experiment in plan["experiments"]:
        experiment_id = experiment["experiment_id"]
        experiment_type = experiment["type"]
        experiment_types.append(experiment_type)
        builder.marker(experiment_id, "experiment_start")

        if experiment_type == "unloaded_sweep":
            actuator = experiment["actuator_id"]
            builder.torque(experiment_id, actuator, True)
            for _ in range(experiment["repetitions"]):
                builder.line(
                    experiment_id,
                    actuator,
                    experiment["start_position_rad"],
                    experiment["end_position_rad"],
                    experiment["sweep_seconds"],
                )
                builder.line(
                    experiment_id,
                    actuator,
                    experiment["end_position_rad"],
                    experiment["start_position_rad"],
                    experiment["sweep_seconds"],
                )
        elif experiment_type == "gravity_static_pose":
            for actuator in sorted(experiment["positions_rad"]):
                builder.torque(experiment_id, actuator, True)
                builder.command(experiment_id, actuator, experiment["positions_rad"][actuator])
            builder.advance(experiment["hold_seconds"])
        elif experiment_type == "step_response":
            actuator = experiment["actuator_id"]
            builder.torque(experiment_id, actuator, True)
            builder.command(experiment_id, actuator, experiment["initial_position_rad"])
            builder.advance(experiment["pre_hold_seconds"])
            builder.command(experiment_id, actuator, experiment["target_position_rad"])
            builder.advance(experiment["post_hold_seconds"])
        elif experiment_type == "frequency_response":
            actuator = experiment["actuator_id"]
            builder.torque(experiment_id, actuator, True)
            for frequency in experiment["frequencies_hz"]:
                builder.marker(experiment_id, f"frequency_{frequency:g}_hz")
                builder.sine(
                    experiment_id,
                    actuator,
                    experiment["center_position_rad"],
                    experiment["amplitude_rad"],
                    frequency,
                    experiment["cycles_per_frequency"],
                )
        elif experiment_type == "backlash_reversal":
            actuator = experiment["actuator_id"]
            center = experiment["center_position_rad"]
            amplitude = experiment["amplitude_rad"]
            builder.torque(experiment_id, actuator, True)
            builder.command(experiment_id, actuator, center)
            for _ in range(experiment["repetitions"]):
                builder.command(experiment_id, actuator, center - amplitude)
                builder.advance(experiment["dwell_seconds"])
                builder.command(experiment_id, actuator, center + amplitude)
                builder.advance(experiment["dwell_seconds"])
        elif experiment_type == "free_decay":
            actuator = experiment["actuator_id"]
            builder.torque(experiment_id, actuator, True)
            builder.command(experiment_id, actuator, experiment["initial_position_rad"])
            builder.advance(experiment["settle_seconds"])
            builder.marker(experiment_id, "release")
            builder.torque(experiment_id, actuator, False)
            builder.advance(experiment["observe_seconds"])
        elif experiment_type == "multi_actuator":
            for actuator in sorted(experiment["positions_rad"]):
                builder.torque(experiment_id, actuator, True)
                builder.command(experiment_id, actuator, experiment["positions_rad"][actuator])
            builder.advance(experiment["hold_seconds"])
        else:
            actuator = experiment["actuator_id"]
            builder.torque(experiment_id, actuator, True)
            builder.marker(experiment_id, "cold_baseline")
            builder.command(experiment_id, actuator, experiment["lower_position_rad"])
            builder.advance(experiment["cold_hold_seconds"])
            builder.marker(experiment_id, "warm_sequence")
            for _ in range(experiment["warm_cycles"]):
                builder.command(experiment_id, actuator, experiment["upper_position_rad"])
                builder.advance(experiment["dwell_seconds"])
                builder.command(experiment_id, actuator, experiment["lower_position_rad"])
                builder.advance(experiment["dwell_seconds"])
            builder.marker(experiment_id, "cooldown")
            builder.advance(experiment["cooldown_seconds"])

        builder.marker(experiment_id, "experiment_end")

    builder.marker("run", "safe_shutdown")
    for actuator in sorted(builder.used_actuators):
        builder.torque("run", actuator, False)
    builder.marker("run", "run_complete")

    documents = [action.to_document() for action in builder.actions]
    schedule_sha256 = hashlib.sha256(canonical_json_bytes(documents)).hexdigest()
    return CompiledSchedule(
        plan_id=plan["plan_id"],
        plan_sha256=compute_plan_sha256(plan),
        primary_clock_id=plan["timing"]["primary_clock_id"],
        duration_ns=builder.cursor_ns,
        actions=tuple(builder.actions),
        experiment_types=tuple(experiment_types),
        schedule_sha256=schedule_sha256,
    )


def command_jsonl_bytes(schedule: CompiledSchedule) -> bytes:
    lines: list[bytes] = []
    sequence = 0
    for action in schedule.actions:
        if action.action == "command":
            sequence += 1
            sample = {
                "timestamp_ns": action.time_ns,
                "sequence": sequence,
                "actuator_id": action.actuator_id,
                **action.payload,
            }
        elif action.action == "torque" and action.payload["enabled"] is False:
            sequence += 1
            sample = {
                "timestamp_ns": action.time_ns,
                "sequence": sequence,
                "actuator_id": action.actuator_id,
                "mode": "disabled",
                "torque_enabled": False,
            }
        else:
            continue
        record = {
            "stream_id": "experiment_commands",
            "sample_type": "command",
            "clock_id": schedule.primary_clock_id,
            "description": "RMA-072 deterministic experiment command schedule",
            "sample": sample,
        }
        lines.append(canonical_json_bytes(record))
    return b"".join(lines)


def schedule_json_bytes(schedule: CompiledSchedule) -> bytes:
    return canonical_json_bytes([action.to_document() for action in schedule.actions])


def _validate_authorization(
    schedule: CompiledSchedule,
    plan: dict[str, Any],
    adapter: ExperimentAdapter,
    authorization: ExecutionAuthorization,
) -> None:
    if not authorization.allow_physical_motion:
        raise ExperimentExecutionError("physical motion authorization is false")
    if authorization.acknowledgement != EXECUTION_ACKNOWLEDGEMENT:
        raise ExperimentExecutionError("operator acknowledgement text does not match")
    if not authorization.operator_present:
        raise ExperimentExecutionError("operator presence is required")
    if not authorization.emergency_stop_verified:
        raise ExperimentExecutionError("emergency stop must be verified")
    if not authorization.workspace_clear:
        raise ExperimentExecutionError("workspace clearance must be confirmed")
    if authorization.plan_sha256 != schedule.plan_sha256:
        raise ExperimentExecutionError("authorization plan hash does not match")
    expected_robot_id = plan["robot"]["expected_robot_id"]
    adapter_robot_id = adapter.robot_id()
    if authorization.robot_id != expected_robot_id or adapter_robot_id != expected_robot_id:
        raise ExperimentExecutionError("authorized, expected, and connected robot IDs must match")


def _check_safety_state(state: SafetyState, plan: dict[str, Any]) -> None:
    values = (
        state.bus_voltage_v,
        state.maximum_temperature_c,
        state.total_current_a,
    )
    if not all(math.isfinite(value) for value in values):
        raise ExperimentExecutionError("adapter returned a non-finite safety value")
    limits = plan["safety_limits"]
    if not state.emergency_stop_available:
        raise ExperimentExecutionError("emergency stop became unavailable")
    if state.faulted:
        raise ExperimentExecutionError("robot reported a fault")
    if state.bus_voltage_v < limits["minimum_bus_voltage_v"]:
        raise ExperimentExecutionError("bus voltage fell below the plan limit")
    if state.maximum_temperature_c > limits["maximum_temperature_c"]:
        raise ExperimentExecutionError("temperature exceeded the plan limit")
    if state.total_current_a > limits["maximum_total_current_a"]:
        raise ExperimentExecutionError("current exceeded the plan limit")


def execute_schedule(
    plan: dict[str, Any],
    schedule: CompiledSchedule,
    adapter: ExperimentAdapter,
    authorization: ExecutionAuthorization,
    *,
    now_ns: Callable[[], int] = time.monotonic_ns,
    sleep_until_ns: Callable[[int], None] | None = None,
    created_utc: str,
) -> None:
    """Execute a validated schedule through an explicit physical adapter boundary."""

    validate_plan(plan)
    expected = compile_plan(plan)
    if expected.schedule_sha256 != schedule.schedule_sha256:
        raise ExperimentExecutionError("compiled schedule does not match the validated plan")
    _validate_authorization(schedule, plan, adapter, authorization)
    manifest = schedule.manifest(created_utc=created_utc, physical_execution=True)
    started = False
    ended = False

    if sleep_until_ns is None:

        def default_sleep_until(deadline_ns: int) -> None:
            while True:
                remaining = deadline_ns - now_ns()
                if remaining <= 0:
                    return
                time.sleep(min(remaining / 1e9, 0.05))

        sleep_until_ns = default_sleep_until

    try:
        adapter.begin_run(manifest)
        started = True
        start_ns = now_ns()
        for action in schedule.actions:
            sleep_until_ns(start_ns + action.time_ns)
            _check_safety_state(adapter.read_safety_state(), plan)
            if action.action == "marker":
                adapter.record_marker(action.payload["marker"], action.experiment_id)
            elif action.action == "torque":
                if action.actuator_id is None:
                    raise ExperimentExecutionError("torque action lacks actuator identity")
                adapter.set_torque(action.actuator_id, bool(action.payload["enabled"]))
            elif action.action == "command":
                if action.actuator_id is None:
                    raise ExperimentExecutionError("command action lacks actuator identity")
                adapter.submit_command(action.actuator_id, copy.deepcopy(action.payload))
            else:
                raise ExperimentExecutionError(f"unsupported compiled action {action.action!r}")
        adapter.end_run("completed")
        ended = True
    except BaseException as exc:
        reason = f"{type(exc).__name__}: {exc}"
        try:
            adapter.emergency_stop(reason)
        finally:
            if started and not ended:
                with contextlib.suppress(BaseException):
                    adapter.end_run("aborted")
        if isinstance(exc, ExperimentExecutionError):
            raise
        raise ExperimentExecutionError(reason) from exc
