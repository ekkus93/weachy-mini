"""RMA-073 deterministic parameter fitting, held-out validation, and signed profiles."""

from __future__ import annotations

import copy
import importlib.util
import sys
from pathlib import Path
from typing import Any


# --- Sibling-module bootstrap -------------------------------------------------
#
# This file is loaded two different ways across the codebase:
#   1. `import calibration_fitting` (from fit_calibration_profile.py,
#      verify_calibration_profile.py) -- scripts/ is on sys.path.
#   2. `importlib.util.spec_from_file_location(...)` by explicit path (from
#      scripts/tests/test_calibration_fitting.py,
#      scripts/generate_rma073_synthetic_data.py,
#      scripts/calibration_profile_approval.py) -- scripts/ is NOT
#      necessarily on sys.path in this case.
#
# Because of (2), a plain `import calibration_fitting_x` at this module's own
# top level would not reliably resolve. Instead, `_load_sibling` below loads
# each calibration_fitting_* submodule by a path relative to *this* file
# (`Path(__file__).with_name(...)`, never relying on scripts/ being on
# sys.path) and registers it into `sys.modules` under its plain module name.
# Submodules must be loaded here in dependency order, so that any submodule
# which itself does a plain `import calibration_fitting_y` for an
# already-loaded sibling resolves it from the sys.modules cache instead of
# needing its own bootstrap. After loading, every public name the submodule
# used to define directly is re-exported here by binding it at module level,
# so the rest of calibration_fitting.py (and external consumers) can keep
# referencing it unqualified.
#
# Dependency order so far (extend this list as later extraction steps land):
#   1. calibration_fitting_validation (pure stdlib, zero internal deps)
#   2. calibration_fitting_numerics (needs only calibration_fitting_validation)
#   3. calibration_fitting_jsonio (needs only calibration_fitting_validation;
#      owns the calibration_data sibling-loading bootstrap)
#   4. calibration_fitting_contracts (needs calibration_fitting_validation and
#      calibration_fitting_jsonio)
#   5. calibration_fitting_datasets (needs calibration_fitting_validation,
#      calibration_fitting_jsonio, and calibration_fitting_contracts)
#   6. calibration_fitting_estimators (needs calibration_fitting_numerics and
#      calibration_fitting_contracts)
#   7. calibration_fitting_heldout (needs calibration_fitting_numerics,
#      calibration_fitting_contracts, and calibration_fitting_estimators)
#   8. calibration_fitting_profile (needs calibration_fitting_validation and
#      calibration_fitting_contracts)
def _load_sibling(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


calibration_fitting_validation = _load_sibling(
    "calibration_fitting_validation",
    Path(__file__).with_name("calibration_fitting_validation.py"),
)

FittingValidationError = calibration_fitting_validation.FittingValidationError
ID_PATTERN = calibration_fitting_validation.ID_PATTERN
SHA256_PATTERN = calibration_fitting_validation.SHA256_PATTERN
GIT_COMMIT_PATTERN = calibration_fitting_validation.GIT_COMMIT_PATTERN
ImportLimits = calibration_fitting_validation.ImportLimits
DEFAULT_LIMITS = calibration_fitting_validation.DEFAULT_LIMITS
_error = calibration_fitting_validation._error
_require_dict = calibration_fitting_validation._require_dict
_require_list = calibration_fitting_validation._require_list
_require_exact_keys = calibration_fitting_validation._require_exact_keys
_require_string = calibration_fitting_validation._require_string
_require_id = calibration_fitting_validation._require_id
_require_integer = calibration_fitting_validation._require_integer
_require_number = calibration_fitting_validation._require_number
_require_hash = calibration_fitting_validation._require_hash
_require_git_commit = calibration_fitting_validation._require_git_commit
_validate_utc = calibration_fitting_validation._validate_utc
canonical_json_bytes = calibration_fitting_validation.canonical_json_bytes

calibration_fitting_numerics = _load_sibling(
    "calibration_fitting_numerics",
    Path(__file__).with_name("calibration_fitting_numerics.py"),
)

_streams = calibration_fitting_numerics._streams
_samples = calibration_fitting_numerics._samples
_nearest_by_timestamp = calibration_fitting_numerics._nearest_by_timestamp
_solve_linear = calibration_fitting_numerics._solve_linear
_confidence = calibration_fitting_numerics._confidence
_jackknife_sensitivity = calibration_fitting_numerics._jackknife_sensitivity
_unsupported = calibration_fitting_numerics._unsupported

calibration_fitting_jsonio = _load_sibling(
    "calibration_fitting_jsonio",
    Path(__file__).with_name("calibration_fitting_jsonio.py"),
)

calibration_data = calibration_fitting_jsonio.calibration_data
strict_json_loads = calibration_fitting_jsonio.strict_json_loads
load_json_file = calibration_fitting_jsonio.load_json_file

calibration_fitting_contracts = _load_sibling(
    "calibration_fitting_contracts",
    Path(__file__).with_name("calibration_fitting_contracts.py"),
)

FIT_PLAN_CONTRACT_ID = calibration_fitting_contracts.FIT_PLAN_CONTRACT_ID
FIT_PLAN_SCHEMA_VERSION = calibration_fitting_contracts.FIT_PLAN_SCHEMA_VERSION
FIT_PLAN_SCHEMA_SHA256 = calibration_fitting_contracts.FIT_PLAN_SCHEMA_SHA256
PROFILE_CONTRACT_ID = calibration_fitting_contracts.PROFILE_CONTRACT_ID
PROFILE_SCHEMA_VERSION = calibration_fitting_contracts.PROFILE_SCHEMA_VERSION
PROFILE_SCHEMA_SHA256 = calibration_fitting_contracts.PROFILE_SCHEMA_SHA256
HASH_ALGORITHM = calibration_fitting_contracts.HASH_ALGORITHM
PROFILE_KIND = calibration_fitting_contracts.PROFILE_KIND
APPROVAL_STATE = calibration_fitting_contracts.APPROVAL_STATE
PARAMETER_FAMILIES = calibration_fitting_contracts.PARAMETER_FAMILIES
schema_descriptor = calibration_fitting_contracts.schema_descriptor
compute_fit_plan_sha256 = calibration_fitting_contracts.compute_fit_plan_sha256
finalize_fit_plan = calibration_fitting_contracts.finalize_fit_plan
_validate_compatibility = calibration_fitting_contracts._validate_compatibility
validate_fit_plan = calibration_fitting_contracts.validate_fit_plan
load_fit_plan = calibration_fitting_contracts.load_fit_plan

calibration_fitting_datasets = _load_sibling(
    "calibration_fitting_datasets",
    Path(__file__).with_name("calibration_fitting_datasets.py"),
)

_resolve_dataset_path = calibration_fitting_datasets._resolve_dataset_path
load_datasets = calibration_fitting_datasets.load_datasets

calibration_fitting_estimators = _load_sibling(
    "calibration_fitting_estimators",
    Path(__file__).with_name("calibration_fitting_estimators.py"),
)

_fit_thermal = calibration_fitting_estimators._fit_thermal
_FITTERS = calibration_fitting_estimators._FITTERS
fit_parameters = calibration_fitting_estimators.fit_parameters

calibration_fitting_heldout = _load_sibling(
    "calibration_fitting_heldout",
    Path(__file__).with_name("calibration_fitting_heldout.py"),
)

validate_heldout = calibration_fitting_heldout.validate_heldout

calibration_fitting_profile = _load_sibling(
    "calibration_fitting_profile",
    Path(__file__).with_name("calibration_fitting_profile.py"),
)

SignatureError = calibration_fitting_profile.SignatureError
SIGNATURE_ALGORITHM = calibration_fitting_profile.SIGNATURE_ALGORITHM
GENERATOR_VERSION = calibration_fitting_profile.GENERATOR_VERSION
build_profile_manifest = calibration_fitting_profile.build_profile_manifest
_profile_hash_candidate = calibration_fitting_profile._profile_hash_candidate
compute_profile_sha256 = calibration_fitting_profile.compute_profile_sha256
_signature_payload = calibration_fitting_profile._signature_payload
public_key_sha256 = calibration_fitting_profile.public_key_sha256
_run_openssl = calibration_fitting_profile._run_openssl
sign_profile = calibration_fitting_profile.sign_profile
validate_profile_structure = calibration_fitting_profile.validate_profile_structure
verify_profile = calibration_fitting_profile.verify_profile


def fit_profile(
    plan: dict[str, Any],
    *,
    dataset_root: Path,
    created_utc: str,
    private_key_path: Path,
    public_key_path: Path,
    public_key_id: str,
) -> tuple[dict[str, Any], dict[str, Any]]:
    datasets = load_datasets(plan, dataset_root)
    parameter_results = fit_parameters(datasets["fitting"], plan)
    frozen_results = copy.deepcopy(parameter_results)
    heldout_validation = validate_heldout(frozen_results, datasets["heldout"], plan)
    manifest = build_profile_manifest(
        plan,
        frozen_results,
        heldout_validation,
        created_utc=created_utc,
    )
    signed = sign_profile(
        manifest,
        private_key_path=private_key_path,
        public_key_path=public_key_path,
        public_key_id=public_key_id,
    )
    verification = verify_profile(
        signed,
        public_key_path=public_key_path,
        expected_compatibility=plan["compatibility"],
    )
    return signed, verification


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))
