"""RMA-072 safety-gated physical execution of a validated, compiled schedule."""

from __future__ import annotations

import contextlib
import copy
import importlib.util
import math
import sys
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_experiment_contracts (for
# `ExperimentExecutionError` and `EXECUTION_ACKNOWLEDGEMENT`),
# calibration_experiment_model (for `CompiledSchedule`, `ExperimentAdapter`,
# `ExecutionAuthorization`, and `SafetyState`), and
# calibration_experiment_planning (for `validate_plan` and `compile_plan`) --
# and both model and planning themselves depend on contracts, so contracts
# must be loaded first. It is loaded either as part of the
# calibration_experiment.py facade's ordered bootstrap (in which case all
# three siblings are already in sys.modules) or standalone / directly by
# path, in which case scripts/ is not necessarily on sys.path. To be
# self-sufficient in both cases, check sys.modules first and only fall back
# to loading each sibling by a path relative to this file if it isn't
# already registered.
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

ExperimentExecutionError = calibration_experiment_contracts.ExperimentExecutionError
EXECUTION_ACKNOWLEDGEMENT = calibration_experiment_contracts.EXECUTION_ACKNOWLEDGEMENT

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

CompiledSchedule = calibration_experiment_model.CompiledSchedule
ExperimentAdapter = calibration_experiment_model.ExperimentAdapter
ExecutionAuthorization = calibration_experiment_model.ExecutionAuthorization
SafetyState = calibration_experiment_model.SafetyState

if "calibration_experiment_planning" in sys.modules:
    calibration_experiment_planning = sys.modules["calibration_experiment_planning"]
else:
    _planning_spec = importlib.util.spec_from_file_location(
        "calibration_experiment_planning",
        Path(__file__).with_name("calibration_experiment_planning.py"),
    )
    if _planning_spec is None or _planning_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_experiment_planning.py")
    calibration_experiment_planning = importlib.util.module_from_spec(_planning_spec)
    sys.modules["calibration_experiment_planning"] = calibration_experiment_planning
    _planning_spec.loader.exec_module(calibration_experiment_planning)

validate_plan = calibration_experiment_planning.validate_plan
compile_plan = calibration_experiment_planning.compile_plan


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
