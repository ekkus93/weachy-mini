"""Versioned calibration dataset validation and canonical hashing."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any


# --- Sibling-module bootstrap -------------------------------------------------
#
# This file is loaded two different ways across the codebase:
#   1. `from calibration_data import (...)` (from capture_reachy_calibration.py,
#      validate_calibration_dataset.py, and calibration_fitting_jsonio.py's
#      plain-import attempt) -- scripts/ is on sys.path.
#   2. `importlib.util.spec_from_file_location(...)` by explicit path (from
#      scripts/tests/test_calibration_data.py, test_calibration_capture.py,
#      generate_rma073_synthetic_data.py, and calibration_fitting_jsonio.py's
#      fallback) -- scripts/ is NOT necessarily on sys.path in this case.
#
# Because of (2), a plain `import calibration_data_x` at this module's own
# top level would not reliably resolve. Instead, `_load_sibling` below loads
# each calibration_data_* submodule by a path relative to *this* file
# (`Path(__file__).with_name(...)`, never relying on scripts/ being on
# sys.path) and registers it into `sys.modules` under its plain module name.
# Submodules must be loaded here in dependency order, so that any submodule
# which itself does a sys.modules-first sibling load for an already-loaded
# sibling resolves it from the cache instead of needing its own bootstrap.
# After loading, every public name the submodule used to define directly is
# re-exported here by binding it at module level, so the rest of
# calibration_data.py (and external consumers) can keep referencing it
# unqualified.
#
# Dependency order:
#   1. calibration_data_contracts (pure leaf, zero internal deps)
#   2. calibration_data_metadata (needs only calibration_data_contracts)
#   3. calibration_data_samples (needs only calibration_data_contracts; safe
#      in either order relative to metadata, no cross-call between them)
#   4. calibration_data_integrity (needs contracts, metadata, and samples --
#      the validate_dataset orchestrator, highest fan-in)
#
# This file must keep this exact filename (scripts/calibration_data.py) --
# four call sites hardcode it by path, plus the rma070-calibration-data.yml
# workflow's paths: trigger.
def _load_sibling(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_data_contracts = _load_sibling(
    "calibration_data_contracts",
    Path(__file__).with_name("calibration_data_contracts.py"),
)

CONTRACT_ID = calibration_data_contracts.CONTRACT_ID
SCHEMA_VERSION = calibration_data_contracts.SCHEMA_VERSION
COLUMN_MANIFEST_ID = calibration_data_contracts.COLUMN_MANIFEST_ID
HASH_ALGORITHM = calibration_data_contracts.HASH_ALGORITHM
EXPECTED_SCHEMA_SHA256 = calibration_data_contracts.EXPECTED_SCHEMA_SHA256
EXPECTED_COLUMN_MANIFEST_SHA256 = calibration_data_contracts.EXPECTED_COLUMN_MANIFEST_SHA256
ID_PATTERN = calibration_data_contracts.ID_PATTERN
SHA256_PATTERN = calibration_data_contracts.SHA256_PATTERN
SAMPLE_TYPES = calibration_data_contracts.SAMPLE_TYPES
CLOCK_TYPES = calibration_data_contracts.CLOCK_TYPES
ALIGNMENT_METHODS = calibration_data_contracts.ALIGNMENT_METHODS
SYNC_STATES = calibration_data_contracts.SYNC_STATES
MODES = calibration_data_contracts.MODES
TOP_LEVEL_KEYS = calibration_data_contracts.TOP_LEVEL_KEYS
CalibrationValidationError = calibration_data_contracts.CalibrationValidationError
ImportLimits = calibration_data_contracts.ImportLimits
DEFAULT_LIMITS = calibration_data_contracts.DEFAULT_LIMITS
_error = calibration_data_contracts._error
_require_dict = calibration_data_contracts._require_dict
_require_list = calibration_data_contracts._require_list
_require_exact_keys = calibration_data_contracts._require_exact_keys
_require_string = calibration_data_contracts._require_string
_require_id = calibration_data_contracts._require_id
_require_bool = calibration_data_contracts._require_bool
_require_integer = calibration_data_contracts._require_integer
_require_number = calibration_data_contracts._require_number
_require_nullable_number = calibration_data_contracts._require_nullable_number
_require_vector = calibration_data_contracts._require_vector
_validate_iso_utc = calibration_data_contracts._validate_iso_utc
_validate_hash = calibration_data_contracts._validate_hash
canonical_json_bytes = calibration_data_contracts.canonical_json_bytes

calibration_data_metadata = _load_sibling(
    "calibration_data_metadata",
    Path(__file__).with_name("calibration_data_metadata.py"),
)

_validate_schema = calibration_data_metadata._validate_schema
_validate_register_value = calibration_data_metadata._validate_register_value
_validate_robot = calibration_data_metadata._validate_robot
_validate_environment = calibration_data_metadata._validate_environment
_validate_capture = calibration_data_metadata._validate_capture
_validate_clocks = calibration_data_metadata._validate_clocks
_validate_alignments = calibration_data_metadata._validate_alignments

calibration_data_samples = _load_sibling(
    "calibration_data_samples",
    Path(__file__).with_name("calibration_data_samples.py"),
)

_validate_common_sample = calibration_data_samples._validate_common_sample
_validate_command = calibration_data_samples._validate_command
_validate_joint = calibration_data_samples._validate_joint
_validate_current_load = calibration_data_samples._validate_current_load
_validate_voltage = calibration_data_samples._validate_voltage
_validate_imu = calibration_data_samples._validate_imu
_validate_external_pose = calibration_data_samples._validate_external_pose
_validate_force_torque = calibration_data_samples._validate_force_torque
_validate_temperature = calibration_data_samples._validate_temperature
_SAMPLE_VALIDATORS = calibration_data_samples._SAMPLE_VALIDATORS
_validate_streams = calibration_data_samples._validate_streams
_validate_source_files = calibration_data_samples._validate_source_files

calibration_data_integrity = _load_sibling(
    "calibration_data_integrity",
    Path(__file__).with_name("calibration_data_integrity.py"),
)

compute_dataset_sha256 = calibration_data_integrity.compute_dataset_sha256
finalize_dataset = calibration_data_integrity.finalize_dataset
validate_dataset = calibration_data_integrity.validate_dataset
load_json_text = calibration_data_integrity.load_json_text
load_json_file = calibration_data_integrity.load_json_file
schema_descriptor = calibration_data_integrity.schema_descriptor
