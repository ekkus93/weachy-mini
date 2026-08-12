"""RMA-074 physical calibration approval, verification, and UI label resolution."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any


# --- Sibling-module bootstrap -------------------------------------------------
#
# This file is loaded two different ways across the codebase:
#   1. `from calibration_profile_approval import (...)` (from
#      approve_calibration_profile.py, resolve_calibration_mode.py) --
#      scripts/ is on sys.path.
#   2. `importlib.util.spec_from_file_location(...)` by explicit path (from
#      scripts/tests/test_rma074_calibration_profile_approval.py) -- scripts/
#      is NOT necessarily on sys.path in this case.
#
# Because of (2), a plain `import calibration_profile_approval_x` at this
# module's own top level would not reliably resolve. Instead, `_load_sibling`
# below loads each calibration_profile_approval_* submodule by a path
# relative to *this* file (`Path(__file__).with_name(...)`, never relying on
# scripts/ being on sys.path) and registers it into `sys.modules` under its
# plain module name. Submodules must be loaded here in dependency order, so
# that any submodule which itself does a sys.modules-first sibling load for
# an already-loaded sibling resolves it from the cache instead of needing its
# own bootstrap. After loading, every public name the submodule used to
# define directly is re-exported here by binding it at module level, so the
# rest of calibration_profile_approval.py (and external consumers) can keep
# referencing it unqualified.
#
# Dependency order:
#   1. calibration_profile_approval_validation (pure stdlib, zero internal deps)
#   2. calibration_profile_approval_evidence (needs only ..._validation)
#   3. calibration_profile_approval_signing (needs only ..._validation; safe
#      in either order relative to evidence, no cross-call between them)
#   4. calibration_profile_approval_core (needs validation, evidence, and
#      signing -- create_approval/verify_approval/_verify_candidate_default,
#      kept together deliberately since create_approval calls verify_approval
#      as a one-way, non-cyclic self-check)
#   5. calibration_profile_approval_labeling (needs only ..._core, for
#      verify_approval)
#
# This file must keep this exact filename (scripts/calibration_profile_approval.py)
# -- the test file hardcodes this path, and the rma074-approval-contract.yml
# workflow's paths: trigger and compileall step both reference it.
def _load_sibling(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_profile_approval_validation = _load_sibling(
    "calibration_profile_approval_validation",
    Path(__file__).with_name("calibration_profile_approval_validation.py"),
)

CONTRACT_ID = calibration_profile_approval_validation.CONTRACT_ID
SCHEMA_VERSION = calibration_profile_approval_validation.SCHEMA_VERSION
HASH_ALGORITHM = calibration_profile_approval_validation.HASH_ALGORITHM
SIGNATURE_ALGORITHM = calibration_profile_approval_validation.SIGNATURE_ALGORITHM
PROFILE_KIND = calibration_profile_approval_validation.PROFILE_KIND
APPROVAL_STATE = calibration_profile_approval_validation.APPROVAL_STATE
APPROVAL_POLICY = calibration_profile_approval_validation.APPROVAL_POLICY
MAX_FILE_BYTES = calibration_profile_approval_validation.MAX_FILE_BYTES
SHA256_PATTERN = calibration_profile_approval_validation.SHA256_PATTERN
ID_PATTERN = calibration_profile_approval_validation.ID_PATTERN
REQUIRED_METRICS = calibration_profile_approval_validation.REQUIRED_METRICS
CORE_APPROVAL_METRICS = calibration_profile_approval_validation.CORE_APPROVAL_METRICS
METRIC_STATUSES = calibration_profile_approval_validation.METRIC_STATUSES
BLOCKED_APPROVAL_PUBLIC_KEY_SHA256 = (
    calibration_profile_approval_validation.BLOCKED_APPROVAL_PUBLIC_KEY_SHA256
)
ApprovalValidationError = calibration_profile_approval_validation.ApprovalValidationError
canonical_json_bytes = calibration_profile_approval_validation.canonical_json_bytes
_error = calibration_profile_approval_validation._error
_require_dict = calibration_profile_approval_validation._require_dict
_require_list = calibration_profile_approval_validation._require_list
_require_exact_keys = calibration_profile_approval_validation._require_exact_keys
_require_string = calibration_profile_approval_validation._require_string
_require_id = calibration_profile_approval_validation._require_id
_require_hash = calibration_profile_approval_validation._require_hash
_require_bool = calibration_profile_approval_validation._require_bool
_require_int = calibration_profile_approval_validation._require_int
_require_number = calibration_profile_approval_validation._require_number
_validate_utc = calibration_profile_approval_validation._validate_utc
_strict_object_pairs = calibration_profile_approval_validation._strict_object_pairs
strict_json_loads = calibration_profile_approval_validation.strict_json_loads
load_json_file = calibration_profile_approval_validation.load_json_file

calibration_profile_approval_evidence = _load_sibling(
    "calibration_profile_approval_evidence",
    Path(__file__).with_name("calibration_profile_approval_evidence.py"),
)

_candidate_hash = calibration_profile_approval_evidence._candidate_hash
_candidate_datasets = calibration_profile_approval_evidence._candidate_datasets
_validate_preflight = calibration_profile_approval_evidence._validate_preflight
_validate_dataset_evidence = calibration_profile_approval_evidence._validate_dataset_evidence
_validate_metric = calibration_profile_approval_evidence._validate_metric
_validate_heldout_report = calibration_profile_approval_evidence._validate_heldout_report

calibration_profile_approval_signing = _load_sibling(
    "calibration_profile_approval_signing",
    Path(__file__).with_name("calibration_profile_approval_signing.py"),
)

compute_approval_sha256 = calibration_profile_approval_signing.compute_approval_sha256
signature_payload_bytes = calibration_profile_approval_signing.signature_payload_bytes
_openssl_sign = calibration_profile_approval_signing._openssl_sign
_openssl_verify = calibration_profile_approval_signing._openssl_verify

calibration_profile_approval_core = _load_sibling(
    "calibration_profile_approval_core",
    Path(__file__).with_name("calibration_profile_approval_core.py"),
)

_verify_candidate_default = calibration_profile_approval_core._verify_candidate_default
create_approval = calibration_profile_approval_core.create_approval
verify_approval = calibration_profile_approval_core.verify_approval

calibration_profile_approval_labeling = _load_sibling(
    "calibration_profile_approval_labeling",
    Path(__file__).with_name("calibration_profile_approval_labeling.py"),
)

LabelResolution = calibration_profile_approval_labeling.LabelResolution
resolve_calibration_label = calibration_profile_approval_labeling.resolve_calibration_label
