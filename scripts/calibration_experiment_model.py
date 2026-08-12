"""RMA-072 experiment schedule/execution data model: schedules, safety, and adapters."""

from __future__ import annotations

import copy
import importlib.util
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_experiment_contracts (for
# `RUN_MANIFEST_CONTRACT_ID`, `_validate_utc`, and `DEFAULT_IMPORT_LIMITS`). It
# is loaded either as part of the calibration_experiment.py facade's ordered
# bootstrap (in which case the sibling is already in sys.modules) or
# standalone / directly by path, in which case scripts/ is not necessarily on
# sys.path. To be self-sufficient in both cases, check sys.modules first and
# only fall back to loading the sibling by a path relative to this file if it
# isn't already registered.
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

RUN_MANIFEST_CONTRACT_ID = calibration_experiment_contracts.RUN_MANIFEST_CONTRACT_ID
DEFAULT_IMPORT_LIMITS = calibration_experiment_contracts.DEFAULT_IMPORT_LIMITS
_validate_utc = calibration_experiment_contracts._validate_utc


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
