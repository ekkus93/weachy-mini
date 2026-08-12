"""RMA-073 fit-plan and profile contracts: schema constants and fit-plan validation."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_fitting_validation (for the `_require_*`
# helpers, `FittingValidationError`, and `canonical_json_bytes`) and on
# calibration_fitting_jsonio (for `load_json_file`) -- and jsonio itself
# depends on validation, so validation must be loaded first. It is loaded
# either as part of the calibration_fitting.py facade's ordered bootstrap (in
# which case both siblings are already in sys.modules) or standalone /
# directly by path, in which case scripts/ is not necessarily on sys.path. To
# be self-sufficient in both cases, check sys.modules first and only fall back
# to loading each sibling by a path relative to this file if it isn't already
# registered.
if "calibration_fitting_validation" in sys.modules:
    calibration_fitting_validation = sys.modules["calibration_fitting_validation"]
else:
    _validation_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_validation",
        Path(__file__).with_name("calibration_fitting_validation.py"),
    )
    if _validation_spec is None or _validation_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_validation.py")
    calibration_fitting_validation = importlib.util.module_from_spec(_validation_spec)
    sys.modules["calibration_fitting_validation"] = calibration_fitting_validation
    _validation_spec.loader.exec_module(calibration_fitting_validation)

FittingValidationError = calibration_fitting_validation.FittingValidationError
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

if "calibration_fitting_jsonio" in sys.modules:
    calibration_fitting_jsonio = sys.modules["calibration_fitting_jsonio"]
else:
    _jsonio_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_jsonio",
        Path(__file__).with_name("calibration_fitting_jsonio.py"),
    )
    if _jsonio_spec is None or _jsonio_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_jsonio.py")
    calibration_fitting_jsonio = importlib.util.module_from_spec(_jsonio_spec)
    sys.modules["calibration_fitting_jsonio"] = calibration_fitting_jsonio
    _jsonio_spec.loader.exec_module(calibration_fitting_jsonio)

load_json_file = calibration_fitting_jsonio.load_json_file

FIT_PLAN_CONTRACT_ID = "rma073_calibration_fit_plan_v1"
FIT_PLAN_SCHEMA_VERSION = 1
FIT_PLAN_SCHEMA_SHA256 = "7460af9bbb1be0fd091d90ad2cee30c7601257ce804dfdcfe12fa77286caed69"
PROFILE_CONTRACT_ID = "rma073_calibration_profile_manifest_v1"
PROFILE_SCHEMA_VERSION = 1
PROFILE_SCHEMA_SHA256 = "a3a7f9e1b193997419a100890780b9f5730625bb83f235529d95cacae8cd1bd4"
HASH_ALGORITHM = "sha256"
PROFILE_KIND = "fit_candidate_unapproved"
APPROVAL_STATE = "unapproved_fit_candidate"
PARAMETER_FAMILIES = (
    "friction",
    "backlash",
    "latency",
    "controller",
    "voltage",
    "compliance",
    "thermal",
)


def schema_descriptor(schema_root: Path) -> dict[str, Any]:
    fit_path = schema_root / "calibration-fit-plan-v1.schema.json"
    profile_path = schema_root / "calibration-profile-manifest-v1.schema.json"
    fit_hash = hashlib.sha256(fit_path.read_bytes()).hexdigest()
    profile_hash = hashlib.sha256(profile_path.read_bytes()).hexdigest()
    if fit_hash != FIT_PLAN_SCHEMA_SHA256:
        raise FittingValidationError("calibration fit-plan schema drifted without version change")
    if profile_hash != PROFILE_SCHEMA_SHA256:
        raise FittingValidationError("calibration profile schema drifted without version change")
    return {
        "fit_plan": {
            "contract_id": FIT_PLAN_CONTRACT_ID,
            "schema_version": FIT_PLAN_SCHEMA_VERSION,
            "schema_sha256": fit_hash,
        },
        "profile": {
            "contract_id": PROFILE_CONTRACT_ID,
            "schema_version": PROFILE_SCHEMA_VERSION,
            "schema_sha256": profile_hash,
        },
    }


def compute_fit_plan_sha256(plan: dict[str, Any]) -> str:
    candidate = copy.deepcopy(plan)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("plan_sha256", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def finalize_fit_plan(plan: dict[str, Any]) -> dict[str, Any]:
    finalized = copy.deepcopy(plan)
    integrity = finalized.setdefault("integrity", {})
    if not isinstance(integrity, dict):
        raise _error("integrity", "must be an object")
    integrity["algorithm"] = HASH_ALGORITHM
    integrity["plan_sha256"] = compute_fit_plan_sha256(finalized)
    return finalized


def _validate_compatibility(value: Any, path: str, limits: ImportLimits) -> dict[str, Any]:
    compatibility = _require_dict(value, path)
    _require_exact_keys(
        compatibility,
        {
            "reachy_source_commit",
            "model_sha256",
            "mujoco_version",
            "simulator_abi_version",
            "servo_contracts",
        },
        set(),
        path,
    )
    reachy_commit = _require_git_commit(
        compatibility["reachy_source_commit"], f"{path}.reachy_source_commit"
    )
    model_hash = _require_hash(compatibility["model_sha256"], f"{path}.model_sha256")
    mujoco_version = _require_string(
        compatibility["mujoco_version"], f"{path}.mujoco_version", limits
    )
    abi = _require_integer(
        compatibility["simulator_abi_version"],
        f"{path}.simulator_abi_version",
        minimum=1,
        maximum=2**31 - 1,
    )
    contracts = _require_dict(compatibility["servo_contracts"], f"{path}.servo_contracts")
    required_contracts = {"servo", "electrical", "mechanical", "power_thermal"}
    _require_exact_keys(contracts, required_contracts, set(), f"{path}.servo_contracts")
    normalized_contracts = {
        key: _require_id(contracts[key], f"{path}.servo_contracts.{key}", limits)
        for key in sorted(required_contracts)
    }
    return {
        "reachy_source_commit": reachy_commit,
        "model_sha256": model_hash,
        "mujoco_version": mujoco_version,
        "simulator_abi_version": abi,
        "servo_contracts": normalized_contracts,
    }


def validate_fit_plan(plan: Any, *, limits: ImportLimits = DEFAULT_LIMITS) -> dict[str, Any]:
    value = _require_dict(plan, "plan")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "schema_version",
            "schema_sha256",
            "plan_id",
            "created_utc",
            "profile_id",
            "profile_kind",
            "compatibility",
            "datasets",
            "actuator_id",
            "windows",
            "validation_thresholds",
            "integrity",
        },
        set(),
        "plan",
    )
    if value["contract_id"] != FIT_PLAN_CONTRACT_ID:
        raise _error("plan.contract_id", f"must equal {FIT_PLAN_CONTRACT_ID}")
    if value["schema_version"] != FIT_PLAN_SCHEMA_VERSION:
        raise _error("plan.schema_version", f"must equal {FIT_PLAN_SCHEMA_VERSION}")
    if _require_hash(value["schema_sha256"], "plan.schema_sha256") != FIT_PLAN_SCHEMA_SHA256:
        raise _error("plan.schema_sha256", "does not match pinned v1 fit-plan schema")
    plan_id = _require_id(value["plan_id"], "plan.plan_id", limits)
    _validate_utc(value["created_utc"], "plan.created_utc", limits)
    profile_id = _require_id(value["profile_id"], "plan.profile_id", limits)
    if value["profile_kind"] != PROFILE_KIND:
        raise _error("plan.profile_kind", f"must equal {PROFILE_KIND}")
    compatibility = _validate_compatibility(value["compatibility"], "plan.compatibility", limits)
    actuator_id = _require_id(value["actuator_id"], "plan.actuator_id", limits)

    datasets = _require_list(value["datasets"], "plan.datasets")
    if len(datasets) < 2 or len(datasets) > limits.maximum_datasets:
        raise _error("plan.datasets", "must contain between 2 and maximum_datasets entries")
    roles: list[str] = []
    dataset_ids: set[str] = set()
    dataset_hashes: set[str] = set()
    dataset_paths: set[str] = set()
    for index, raw in enumerate(datasets):
        path = f"plan.datasets[{index}]"
        entry = _require_dict(raw, path)
        _require_exact_keys(entry, {"dataset_id", "role", "path", "dataset_sha256"}, set(), path)
        dataset_id = _require_id(entry["dataset_id"], f"{path}.dataset_id", limits)
        if dataset_id in dataset_ids:
            raise _error(f"{path}.dataset_id", "is duplicated")
        dataset_ids.add(dataset_id)
        role = _require_string(entry["role"], f"{path}.role", limits)
        if role not in {"fitting", "heldout"}:
            raise _error(f"{path}.role", "must equal fitting or heldout")
        roles.append(role)
        relative = _require_string(entry["path"], f"{path}.path", limits)
        relative_path = Path(relative)
        if relative_path.is_absolute() or ".." in relative_path.parts or relative in {".", ""}:
            raise _error(f"{path}.path", "must be a safe relative path without parent traversal")
        normalized = relative_path.as_posix()
        if normalized in dataset_paths:
            raise _error(f"{path}.path", "is duplicated")
        dataset_paths.add(normalized)
        digest = _require_hash(entry["dataset_sha256"], f"{path}.dataset_sha256")
        if digest in dataset_hashes:
            raise _error(f"{path}.dataset_sha256", "is reused across dataset roles")
        dataset_hashes.add(digest)
    if roles.count("fitting") < 1 or roles.count("heldout") < 1:
        raise _error("plan.datasets", "must include at least one fitting and one heldout dataset")

    windows = _require_dict(value["windows"], "plan.windows")
    _require_exact_keys(windows, set(PARAMETER_FAMILIES), set(), "plan.windows")
    normalized_windows: dict[str, dict[str, int]] = {}
    for family in PARAMETER_FAMILIES:
        window = _require_dict(windows[family], f"plan.windows.{family}")
        _require_exact_keys(window, {"start_ns", "end_ns"}, set(), f"plan.windows.{family}")
        start = _require_integer(window["start_ns"], f"plan.windows.{family}.start_ns", minimum=0)
        end = _require_integer(window["end_ns"], f"plan.windows.{family}.end_ns", minimum=0)
        if end <= start:
            raise _error(f"plan.windows.{family}", "end_ns must be greater than start_ns")
        normalized_windows[family] = {"start_ns": start, "end_ns": end}

    thresholds = _require_dict(value["validation_thresholds"], "plan.validation_thresholds")
    _require_exact_keys(thresholds, set(PARAMETER_FAMILIES), set(), "plan.validation_thresholds")
    normalized_thresholds: dict[str, float] = {}
    for family in PARAMETER_FAMILIES:
        normalized_thresholds[family] = _require_number(
            thresholds[family],
            f"plan.validation_thresholds.{family}",
            minimum=0.0,
            maximum=1e12,
        )

    integrity = _require_dict(value["integrity"], "plan.integrity")
    _require_exact_keys(integrity, {"algorithm", "plan_sha256"}, set(), "plan.integrity")
    if integrity["algorithm"] != HASH_ALGORITHM:
        raise _error("plan.integrity.algorithm", f"must equal {HASH_ALGORITHM}")
    expected = _require_hash(integrity["plan_sha256"], "plan.integrity.plan_sha256")
    actual = compute_fit_plan_sha256(value)
    if expected != actual:
        raise _error("plan.integrity.plan_sha256", "does not match canonical plan content")
    return {
        "plan_id": plan_id,
        "profile_id": profile_id,
        "actuator_id": actuator_id,
        "compatibility": compatibility,
        "windows": normalized_windows,
        "thresholds": normalized_thresholds,
        "plan_sha256": actual,
        "dataset_count": len(datasets),
        "fitting_dataset_count": roles.count("fitting"),
        "heldout_dataset_count": roles.count("heldout"),
    }


def load_fit_plan(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> dict[str, Any]:
    value = load_json_file(path, limits=limits)
    validate_fit_plan(value, limits=limits)
    return value
