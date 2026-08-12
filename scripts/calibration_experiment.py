"""Versioned, fail-closed calibration experiment planning and execution."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any


# --- Sibling-module bootstrap -------------------------------------------------
#
# This file is loaded two different ways across the codebase:
#   1. `from calibration_experiment import (...)` (from
#      run_calibration_experiment.py) -- scripts/ is on sys.path.
#   2. `importlib.util.spec_from_file_location(...)` by explicit path (from
#      scripts/tests/test_calibration_experiment.py) -- scripts/ is NOT
#      necessarily on sys.path in this case.
#
# Because of (2), a plain `import calibration_experiment_x` at this module's
# own top level would not reliably resolve. Instead, `_load_sibling` below
# loads each calibration_experiment_* submodule by a path relative to *this*
# file (`Path(__file__).with_name(...)`, never relying on scripts/ being on
# sys.path) and registers it into `sys.modules` under its plain module name.
# Submodules must be loaded here in dependency order, so that any submodule
# which itself does a sys.modules-first sibling load for an already-loaded
# sibling resolves it from the cache instead of needing its own bootstrap.
# After loading, every public name the submodule used to define directly is
# re-exported here by binding it at module level, so the rest of
# calibration_experiment.py (and external consumers) can keep referencing it
# unqualified.
#
# Dependency order:
#   1. calibration_experiment_contracts (pure leaf, zero internal deps)
#   2. calibration_experiment_model (needs only calibration_experiment_contracts)
#   3. calibration_experiment_planning (needs calibration_experiment_contracts
#      and calibration_experiment_model; validate_plan/compile_plan/
#      _validate_experiment/_ScheduleBuilder are kept together here
#      deliberately -- validate_plan and compile_plan call each other)
#   4. calibration_experiment_execution (needs all three of the above)
def _load_sibling(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_experiment_contracts = _load_sibling(
    "calibration_experiment_contracts",
    Path(__file__).with_name("calibration_experiment_contracts.py"),
)

PLAN_CONTRACT_ID = calibration_experiment_contracts.PLAN_CONTRACT_ID
PLAN_SCHEMA_VERSION = calibration_experiment_contracts.PLAN_SCHEMA_VERSION
PLAN_HASH_ALGORITHM = calibration_experiment_contracts.PLAN_HASH_ALGORITHM
PLAN_SCHEMA_SHA256 = calibration_experiment_contracts.PLAN_SCHEMA_SHA256
RUN_MANIFEST_CONTRACT_ID = calibration_experiment_contracts.RUN_MANIFEST_CONTRACT_ID
EXECUTION_ACKNOWLEDGEMENT = calibration_experiment_contracts.EXECUTION_ACKNOWLEDGEMENT
ID_PATTERN = calibration_experiment_contracts.ID_PATTERN
EXPERIMENT_TYPES = calibration_experiment_contracts.EXPERIMENT_TYPES
ACTION_TYPES = calibration_experiment_contracts.ACTION_TYPES
ExperimentValidationError = calibration_experiment_contracts.ExperimentValidationError
ExperimentExecutionError = calibration_experiment_contracts.ExperimentExecutionError
ImportLimits = calibration_experiment_contracts.ImportLimits
DEFAULT_IMPORT_LIMITS = calibration_experiment_contracts.DEFAULT_IMPORT_LIMITS
canonical_json_bytes = calibration_experiment_contracts.canonical_json_bytes
_error = calibration_experiment_contracts._error
_reject_constant = calibration_experiment_contracts._reject_constant
_reject_duplicate_pairs = calibration_experiment_contracts._reject_duplicate_pairs
strict_json_loads = calibration_experiment_contracts.strict_json_loads
_require_dict = calibration_experiment_contracts._require_dict
_require_list = calibration_experiment_contracts._require_list
_require_exact_keys = calibration_experiment_contracts._require_exact_keys
_require_string = calibration_experiment_contracts._require_string
_require_id = calibration_experiment_contracts._require_id
_require_bool = calibration_experiment_contracts._require_bool
_require_integer = calibration_experiment_contracts._require_integer
_require_number = calibration_experiment_contracts._require_number
_validate_utc = calibration_experiment_contracts._validate_utc
_validate_hash = calibration_experiment_contracts._validate_hash
_position = calibration_experiment_contracts._position
_positive_duration = calibration_experiment_contracts._positive_duration
compute_plan_sha256 = calibration_experiment_contracts.compute_plan_sha256
finalize_plan = calibration_experiment_contracts.finalize_plan
schema_descriptor = calibration_experiment_contracts.schema_descriptor

calibration_experiment_model = _load_sibling(
    "calibration_experiment_model",
    Path(__file__).with_name("calibration_experiment_model.py"),
)

ScheduledAction = calibration_experiment_model.ScheduledAction
CompiledSchedule = calibration_experiment_model.CompiledSchedule
ExecutionAuthorization = calibration_experiment_model.ExecutionAuthorization
SafetyState = calibration_experiment_model.SafetyState
ExperimentAdapter = calibration_experiment_model.ExperimentAdapter

calibration_experiment_planning = _load_sibling(
    "calibration_experiment_planning",
    Path(__file__).with_name("calibration_experiment_planning.py"),
)

load_plan_file = calibration_experiment_planning.load_plan_file
_validate_experiment = calibration_experiment_planning._validate_experiment
validate_plan = calibration_experiment_planning.validate_plan
compile_plan = calibration_experiment_planning.compile_plan
command_jsonl_bytes = calibration_experiment_planning.command_jsonl_bytes
schedule_json_bytes = calibration_experiment_planning.schedule_json_bytes

calibration_experiment_execution = _load_sibling(
    "calibration_experiment_execution",
    Path(__file__).with_name("calibration_experiment_execution.py"),
)

_validate_authorization = calibration_experiment_execution._validate_authorization
_check_safety_state = calibration_experiment_execution._check_safety_state
execute_schedule = calibration_experiment_execution.execute_schedule
