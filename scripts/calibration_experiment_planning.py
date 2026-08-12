"""RMA-072 experiment plan validation and deterministic schedule compilation.

`validate_plan` and `compile_plan` form a genuine two-way call cycle:
`validate_plan` calls `compile_plan(value, validate=False)` to cross-check the
compiled action count/duration against the plan's own safety limits, and
`compile_plan` calls `validate_plan(plan)` by default unless told not to.
`_validate_experiment` and `_ScheduleBuilder` are private helpers used only by
this cycle. All four are kept together in this one module deliberately --
splitting them further would require fragile circular imports between
sibling files for no real size benefit.
"""

from __future__ import annotations

import hashlib
import importlib.util
import math
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_experiment_contracts (for the plan
# constants, `ExperimentValidationError`, the `_require_*`/`_validate_*`
# primitives, `compute_plan_sha256`, and `canonical_json_bytes`) and on
# calibration_experiment_model (for `ScheduledAction` and `CompiledSchedule`)
# -- and model itself depends on contracts, so contracts must be loaded
# first. It is loaded either as part of the calibration_experiment.py
# facade's ordered bootstrap (in which case both siblings are already in
# sys.modules) or standalone / directly by path, in which case scripts/ is
# not necessarily on sys.path. To be self-sufficient in both cases, check
# sys.modules first and only fall back to loading each sibling by a path
# relative to this file if it isn't already registered.
if "calibration_experiment_contracts" in sys.modules:
    calibration_experiment_contracts = sys.modules["calibration_experiment_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_experiment_contracts",
        Path(__file__).with_name("calibration_experiment_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_experiment_contracts.py")
    calibration_experiment_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_experiment_contracts"] = calibration_experiment_contracts
    _contracts_spec.loader.exec_module(calibration_experiment_contracts)

PLAN_CONTRACT_ID = calibration_experiment_contracts.PLAN_CONTRACT_ID
PLAN_SCHEMA_VERSION = calibration_experiment_contracts.PLAN_SCHEMA_VERSION
PLAN_SCHEMA_SHA256 = calibration_experiment_contracts.PLAN_SCHEMA_SHA256
PLAN_HASH_ALGORITHM = calibration_experiment_contracts.PLAN_HASH_ALGORITHM
EXPERIMENT_TYPES = calibration_experiment_contracts.EXPERIMENT_TYPES
ExperimentValidationError = calibration_experiment_contracts.ExperimentValidationError
ImportLimits = calibration_experiment_contracts.ImportLimits
DEFAULT_IMPORT_LIMITS = calibration_experiment_contracts.DEFAULT_IMPORT_LIMITS
canonical_json_bytes = calibration_experiment_contracts.canonical_json_bytes
_error = calibration_experiment_contracts._error
strict_json_loads = calibration_experiment_contracts.strict_json_loads
_require_dict = calibration_experiment_contracts._require_dict
_require_list = calibration_experiment_contracts._require_list
_require_exact_keys = calibration_experiment_contracts._require_exact_keys
_require_string = calibration_experiment_contracts._require_string
_require_id = calibration_experiment_contracts._require_id
_require_bool = calibration_experiment_contracts._require_bool
_require_integer = calibration_experiment_contracts._require_integer
_require_number = calibration_experiment_contracts._require_number
_validate_hash = calibration_experiment_contracts._validate_hash
_position = calibration_experiment_contracts._position
_positive_duration = calibration_experiment_contracts._positive_duration
compute_plan_sha256 = calibration_experiment_contracts.compute_plan_sha256

if "calibration_experiment_model" in sys.modules:
    calibration_experiment_model = sys.modules["calibration_experiment_model"]
else:
    _model_spec = importlib.util.spec_from_file_location(
        "calibration_experiment_model",
        Path(__file__).with_name("calibration_experiment_model.py"),
    )
    if _model_spec is None or _model_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_experiment_model.py")
    calibration_experiment_model = importlib.util.module_from_spec(_model_spec)
    sys.modules["calibration_experiment_model"] = calibration_experiment_model
    _model_spec.loader.exec_module(calibration_experiment_model)

ScheduledAction = calibration_experiment_model.ScheduledAction
CompiledSchedule = calibration_experiment_model.CompiledSchedule


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
