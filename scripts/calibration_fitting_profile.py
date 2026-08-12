"""RMA-073 signed profile manifests: building, hashing, signing, and verification."""

from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

SIGNATURE_ALGORITHM = "ed25519"
GENERATOR_VERSION = "1.0.0"


class SignatureError(RuntimeError):
    """Raised when profile signing or verification fails."""


# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_fitting_validation (for the `_require_*`
# helpers, `FittingValidationError`, `ImportLimits`, `DEFAULT_LIMITS`,
# `_error`, `_validate_utc`, and `canonical_json_bytes`) and on
# calibration_fitting_contracts (for `_validate_compatibility`, `PROFILE_KIND`,
# `HASH_ALGORITHM`, `APPROVAL_STATE`, the profile schema constants, and
# `validate_fit_plan`) -- and contracts itself depends on validation, so
# validation must be loaded first. It is loaded either as part of the
# calibration_fitting.py facade's ordered bootstrap (in which case both
# siblings are already in sys.modules) or standalone / directly by path, in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient in
# both cases, check sys.modules first and only fall back to loading each
# sibling by a path relative to this file if it isn't already registered.
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
_require_hash = calibration_fitting_validation._require_hash
_validate_utc = calibration_fitting_validation._validate_utc
canonical_json_bytes = calibration_fitting_validation.canonical_json_bytes

if "calibration_fitting_contracts" in sys.modules:
    calibration_fitting_contracts = sys.modules["calibration_fitting_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_fitting_contracts",
        Path(__file__).with_name("calibration_fitting_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_contracts.py")
    calibration_fitting_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_fitting_contracts"] = calibration_fitting_contracts
    _contracts_spec.loader.exec_module(calibration_fitting_contracts)

PROFILE_CONTRACT_ID = calibration_fitting_contracts.PROFILE_CONTRACT_ID
PROFILE_SCHEMA_VERSION = calibration_fitting_contracts.PROFILE_SCHEMA_VERSION
PROFILE_SCHEMA_SHA256 = calibration_fitting_contracts.PROFILE_SCHEMA_SHA256
HASH_ALGORITHM = calibration_fitting_contracts.HASH_ALGORITHM
PROFILE_KIND = calibration_fitting_contracts.PROFILE_KIND
APPROVAL_STATE = calibration_fitting_contracts.APPROVAL_STATE
PARAMETER_FAMILIES = calibration_fitting_contracts.PARAMETER_FAMILIES
_validate_compatibility = calibration_fitting_contracts._validate_compatibility
validate_fit_plan = calibration_fitting_contracts.validate_fit_plan


def build_profile_manifest(
    plan: dict[str, Any],
    parameter_results: dict[str, dict[str, Any]],
    heldout_validation: dict[str, Any],
    *,
    created_utc: str,
) -> dict[str, Any]:
    summary = validate_fit_plan(plan)
    _validate_utc(created_utc, "created_utc", DEFAULT_LIMITS)
    datasets = [
        {
            "dataset_id": entry["dataset_id"],
            "role": entry["role"],
            "dataset_sha256": entry["dataset_sha256"],
        }
        for entry in plan["datasets"]
    ]
    return {
        "contract_id": PROFILE_CONTRACT_ID,
        "schema_version": PROFILE_SCHEMA_VERSION,
        "schema_sha256": PROFILE_SCHEMA_SHA256,
        "profile_id": summary["profile_id"],
        "profile_kind": PROFILE_KIND,
        "calibrated": False,
        "approval_state": APPROVAL_STATE,
        "created_utc": created_utc,
        "generator": {
            "name": "weachy-mini-rma073-fitting",
            "version": GENERATOR_VERSION,
            "fit_plan_id": summary["plan_id"],
            "fit_plan_sha256": summary["plan_sha256"],
        },
        "compatibility": copy.deepcopy(summary["compatibility"]),
        "datasets": datasets,
        "parameter_results": copy.deepcopy(parameter_results),
        "heldout_validation": copy.deepcopy(heldout_validation),
        "integrity": {"algorithm": HASH_ALGORITHM},
        "signature": {},
    }


def _profile_hash_candidate(profile: dict[str, Any]) -> dict[str, Any]:
    candidate = copy.deepcopy(profile)
    candidate.pop("signature", None)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("profile_sha256", None)
    return candidate


def compute_profile_sha256(profile: dict[str, Any]) -> str:
    return hashlib.sha256(canonical_json_bytes(_profile_hash_candidate(profile))).hexdigest()


def _signature_payload(profile: dict[str, Any]) -> bytes:
    candidate = copy.deepcopy(profile)
    candidate.pop("signature", None)
    return canonical_json_bytes(candidate)


def public_key_sha256(public_key_path: Path) -> str:
    return hashlib.sha256(public_key_path.read_bytes()).hexdigest()


def _run_openssl(arguments: list[str], *, input_bytes: bytes | None = None) -> bytes:
    try:
        completed = subprocess.run(
            ["openssl", *arguments],
            input=input_bytes,
            capture_output=True,
            check=False,
        )
    except FileNotFoundError as exc:
        raise SignatureError("OpenSSL is required for Ed25519 profile signatures") from exc
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace").strip()
        raise SignatureError(f"OpenSSL command failed: {detail}")
    return completed.stdout


def sign_profile(
    profile: dict[str, Any],
    *,
    private_key_path: Path,
    public_key_path: Path,
    public_key_id: str,
) -> dict[str, Any]:
    value = copy.deepcopy(profile)
    if value.get("calibrated") is not False or value.get("profile_kind") != PROFILE_KIND:
        raise FittingValidationError(
            "RMA-073 may sign only unapproved fit candidates, never calibrated profiles"
        )
    integrity = _require_dict(value.get("integrity"), "integrity")
    integrity["algorithm"] = HASH_ALGORITHM
    integrity["profile_sha256"] = compute_profile_sha256(value)
    payload = _signature_payload(value)
    with tempfile.TemporaryDirectory() as temp_text:
        payload_path = Path(temp_text) / "payload.json"
        payload_path.write_bytes(payload)
        signature = _run_openssl(
            [
                "pkeyutl",
                "-sign",
                "-rawin",
                "-inkey",
                str(private_key_path),
                "-in",
                str(payload_path),
            ]
        )
    value["signature"] = {
        "algorithm": SIGNATURE_ALGORITHM,
        "public_key_id": public_key_id,
        "public_key_sha256": public_key_sha256(public_key_path),
        "signature_base64": base64.b64encode(signature).decode("ascii"),
    }
    validate_profile_structure(value)
    return value


def validate_profile_structure(
    profile: Any, *, limits: ImportLimits = DEFAULT_LIMITS
) -> dict[str, Any]:
    value = _require_dict(profile, "profile")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "schema_version",
            "schema_sha256",
            "profile_id",
            "profile_kind",
            "calibrated",
            "approval_state",
            "created_utc",
            "generator",
            "compatibility",
            "datasets",
            "parameter_results",
            "heldout_validation",
            "integrity",
            "signature",
        },
        set(),
        "profile",
    )
    if value["contract_id"] != PROFILE_CONTRACT_ID:
        raise _error("profile.contract_id", f"must equal {PROFILE_CONTRACT_ID}")
    if value["schema_version"] != PROFILE_SCHEMA_VERSION:
        raise _error("profile.schema_version", f"must equal {PROFILE_SCHEMA_VERSION}")
    if _require_hash(value["schema_sha256"], "profile.schema_sha256") != PROFILE_SCHEMA_SHA256:
        raise _error("profile.schema_sha256", "does not match pinned profile schema")
    _require_id(value["profile_id"], "profile.profile_id", limits)
    if value["profile_kind"] != PROFILE_KIND:
        raise _error("profile.profile_kind", f"must equal {PROFILE_KIND}")
    if value["calibrated"] is not False:
        raise _error(
            "profile.calibrated", "RMA-073 profiles must remain false until RMA-074 approval"
        )
    if value["approval_state"] != APPROVAL_STATE:
        raise _error("profile.approval_state", f"must equal {APPROVAL_STATE}")
    _validate_utc(value["created_utc"], "profile.created_utc", limits)
    _validate_compatibility(value["compatibility"], "profile.compatibility", limits)
    datasets = _require_list(value["datasets"], "profile.datasets")
    if len(datasets) < 2 or len(datasets) > limits.maximum_datasets:
        raise _error("profile.datasets", "must contain bounded fitting and heldout evidence")
    roles = []
    hashes = set()
    for index, raw in enumerate(datasets):
        path = f"profile.datasets[{index}]"
        entry = _require_dict(raw, path)
        _require_exact_keys(entry, {"dataset_id", "role", "dataset_sha256"}, set(), path)
        _require_id(entry["dataset_id"], f"{path}.dataset_id", limits)
        role = _require_string(entry["role"], f"{path}.role", limits)
        if role not in {"fitting", "heldout"}:
            raise _error(f"{path}.role", "must equal fitting or heldout")
        roles.append(role)
        digest = _require_hash(entry["dataset_sha256"], f"{path}.dataset_sha256")
        if digest in hashes:
            raise _error(f"{path}.dataset_sha256", "is duplicated across roles")
        hashes.add(digest)
    if "fitting" not in roles or "heldout" not in roles:
        raise _error("profile.datasets", "must preserve fitting and heldout split")
    results = _require_dict(value["parameter_results"], "profile.parameter_results")
    _require_exact_keys(results, set(PARAMETER_FAMILIES), set(), "profile.parameter_results")
    heldout = _require_dict(value["heldout_validation"], "profile.heldout_validation")
    _require_exact_keys(
        heldout,
        {"split_policy", "supported_family_count", "all_supported_passed", "families"},
        set(),
        "profile.heldout_validation",
    )
    integrity = _require_dict(value["integrity"], "profile.integrity")
    _require_exact_keys(integrity, {"algorithm", "profile_sha256"}, set(), "profile.integrity")
    if integrity["algorithm"] != HASH_ALGORITHM:
        raise _error("profile.integrity.algorithm", f"must equal {HASH_ALGORITHM}")
    _require_hash(integrity["profile_sha256"], "profile.integrity.profile_sha256")
    signature = _require_dict(value["signature"], "profile.signature")
    _require_exact_keys(
        signature,
        {"algorithm", "public_key_id", "public_key_sha256", "signature_base64"},
        set(),
        "profile.signature",
    )
    if signature["algorithm"] != SIGNATURE_ALGORITHM:
        raise _error("profile.signature.algorithm", f"must equal {SIGNATURE_ALGORITHM}")
    _require_id(signature["public_key_id"], "profile.signature.public_key_id", limits)
    _require_hash(signature["public_key_sha256"], "profile.signature.public_key_sha256")
    try:
        decoded = base64.b64decode(signature["signature_base64"], validate=True)
    except Exception as exc:
        raise _error("profile.signature.signature_base64", "must be valid base64") from exc
    if len(decoded) != 64:
        raise _error("profile.signature.signature_base64", "must contain an Ed25519 signature")
    return {
        "profile_id": value["profile_id"],
        "profile_sha256": integrity["profile_sha256"],
        "compatibility": copy.deepcopy(value["compatibility"]),
    }


def verify_profile(
    profile: dict[str, Any],
    *,
    public_key_path: Path,
    expected_compatibility: dict[str, Any] | None = None,
) -> dict[str, Any]:
    summary = validate_profile_structure(profile)
    expected_hash = profile["integrity"]["profile_sha256"]
    actual_hash = compute_profile_sha256(profile)
    if expected_hash != actual_hash:
        raise FittingValidationError(
            "profile.integrity.profile_sha256 does not match canonical content"
        )
    expected_key_hash = profile["signature"]["public_key_sha256"]
    actual_key_hash = public_key_sha256(public_key_path)
    if expected_key_hash != actual_key_hash:
        raise FittingValidationError(
            "profile signature public key hash does not match supplied key"
        )
    signature = base64.b64decode(profile["signature"]["signature_base64"], validate=True)
    with tempfile.TemporaryDirectory() as temp_text:
        temp = Path(temp_text)
        payload_path = temp / "payload.json"
        signature_path = temp / "signature.bin"
        payload_path.write_bytes(_signature_payload(profile))
        signature_path.write_bytes(signature)
        try:
            _run_openssl(
                [
                    "pkeyutl",
                    "-verify",
                    "-rawin",
                    "-pubin",
                    "-inkey",
                    str(public_key_path),
                    "-sigfile",
                    str(signature_path),
                    "-in",
                    str(payload_path),
                ]
            )
        except SignatureError as exc:
            raise FittingValidationError("profile Ed25519 signature verification failed") from exc
    if expected_compatibility is not None:
        normalized = _validate_compatibility(
            expected_compatibility, "expected_compatibility", DEFAULT_LIMITS
        )
        if summary["compatibility"] != normalized:
            raise FittingValidationError(
                "profile is incompatible with the expected model or simulator version"
            )
    return {
        "status": "ok",
        "profile_id": summary["profile_id"],
        "profile_sha256": actual_hash,
        "signature_algorithm": SIGNATURE_ALGORITHM,
        "public_key_sha256": actual_key_hash,
        "heldout_all_supported_passed": profile["heldout_validation"]["all_supported_passed"],
        "calibrated": False,
        "approval_state": APPROVAL_STATE,
    }
