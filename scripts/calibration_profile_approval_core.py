"""RMA-074 approval creation and verification: the create_approval/verify_approval pair.

`create_approval` calls `verify_approval` as a mandatory self-check on the
document it just built and signed; `verify_approval` never calls
`create_approval` back, so this is a genuine one-way (not cyclic) dependency.
`_verify_candidate_default` is only ever called from inside `create_approval`.
All three are kept together in this one module deliberately -- they also
share nearly every other dependency (evidence validation, signing helpers),
so separating them would buy no real size benefit for real risk.
"""

from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_profile_approval_validation (for the
# contract constants and `_require_*`/`_validate_utc` primitives),
# calibration_profile_approval_evidence (for `_candidate_hash`,
# `_candidate_datasets`, `_validate_preflight`, `_validate_dataset_evidence`,
# `_validate_heldout_report`), and calibration_profile_approval_signing (for
# `compute_approval_sha256`, `signature_payload_bytes`, `_openssl_sign`,
# `_openssl_verify`) -- and both evidence and signing themselves depend on
# validation, so validation must be loaded first. It is loaded either as part
# of the calibration_profile_approval.py facade's ordered bootstrap (in which
# case all three siblings are already in sys.modules) or standalone /
# directly by path, in which case scripts/ is not necessarily on sys.path. To
# be self-sufficient in both cases, check sys.modules first and only fall
# back to loading each sibling by a path relative to this file if it isn't
# already registered.
if "calibration_profile_approval_validation" in sys.modules:
    calibration_profile_approval_validation = sys.modules["calibration_profile_approval_validation"]
else:
    _validation_spec = importlib.util.spec_from_file_location(
        "calibration_profile_approval_validation",
        Path(__file__).with_name("calibration_profile_approval_validation.py"),
    )
    if _validation_spec is None or _validation_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_profile_approval_validation.py")
    calibration_profile_approval_validation = importlib.util.module_from_spec(_validation_spec)
    sys.modules["calibration_profile_approval_validation"] = calibration_profile_approval_validation
    _validation_spec.loader.exec_module(calibration_profile_approval_validation)

CONTRACT_ID = calibration_profile_approval_validation.CONTRACT_ID
SCHEMA_VERSION = calibration_profile_approval_validation.SCHEMA_VERSION
HASH_ALGORITHM = calibration_profile_approval_validation.HASH_ALGORITHM
SIGNATURE_ALGORITHM = calibration_profile_approval_validation.SIGNATURE_ALGORITHM
PROFILE_KIND = calibration_profile_approval_validation.PROFILE_KIND
APPROVAL_STATE = calibration_profile_approval_validation.APPROVAL_STATE
APPROVAL_POLICY = calibration_profile_approval_validation.APPROVAL_POLICY
BLOCKED_APPROVAL_PUBLIC_KEY_SHA256 = (
    calibration_profile_approval_validation.BLOCKED_APPROVAL_PUBLIC_KEY_SHA256
)
ApprovalValidationError = calibration_profile_approval_validation.ApprovalValidationError
_error = calibration_profile_approval_validation._error
_require_dict = calibration_profile_approval_validation._require_dict
_require_list = calibration_profile_approval_validation._require_list
_require_exact_keys = calibration_profile_approval_validation._require_exact_keys
_require_string = calibration_profile_approval_validation._require_string
_require_id = calibration_profile_approval_validation._require_id
_require_hash = calibration_profile_approval_validation._require_hash
_require_bool = calibration_profile_approval_validation._require_bool
_validate_utc = calibration_profile_approval_validation._validate_utc

if "calibration_profile_approval_evidence" in sys.modules:
    calibration_profile_approval_evidence = sys.modules["calibration_profile_approval_evidence"]
else:
    _evidence_spec = importlib.util.spec_from_file_location(
        "calibration_profile_approval_evidence",
        Path(__file__).with_name("calibration_profile_approval_evidence.py"),
    )
    if _evidence_spec is None or _evidence_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_profile_approval_evidence.py")
    calibration_profile_approval_evidence = importlib.util.module_from_spec(_evidence_spec)
    sys.modules["calibration_profile_approval_evidence"] = calibration_profile_approval_evidence
    _evidence_spec.loader.exec_module(calibration_profile_approval_evidence)

_candidate_hash = calibration_profile_approval_evidence._candidate_hash
_candidate_datasets = calibration_profile_approval_evidence._candidate_datasets
_validate_preflight = calibration_profile_approval_evidence._validate_preflight
_validate_dataset_evidence = calibration_profile_approval_evidence._validate_dataset_evidence
_validate_heldout_report = calibration_profile_approval_evidence._validate_heldout_report

if "calibration_profile_approval_signing" in sys.modules:
    calibration_profile_approval_signing = sys.modules["calibration_profile_approval_signing"]
else:
    _signing_spec = importlib.util.spec_from_file_location(
        "calibration_profile_approval_signing",
        Path(__file__).with_name("calibration_profile_approval_signing.py"),
    )
    if _signing_spec is None or _signing_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_profile_approval_signing.py")
    calibration_profile_approval_signing = importlib.util.module_from_spec(_signing_spec)
    sys.modules["calibration_profile_approval_signing"] = calibration_profile_approval_signing
    _signing_spec.loader.exec_module(calibration_profile_approval_signing)

compute_approval_sha256 = calibration_profile_approval_signing.compute_approval_sha256
signature_payload_bytes = calibration_profile_approval_signing.signature_payload_bytes
_openssl_sign = calibration_profile_approval_signing._openssl_sign
_openssl_verify = calibration_profile_approval_signing._openssl_verify


def _verify_candidate_default(
    candidate: dict[str, Any],
    candidate_public_key: Path,
    expected_compatibility: dict[str, Any],
) -> dict[str, Any]:
    script = Path(__file__).resolve().with_name("calibration_fitting.py")
    spec = importlib.util.spec_from_file_location("_rma073_calibration_fitting", script)
    if spec is None or spec.loader is None:
        raise ApprovalValidationError("cannot load RMA-073 calibration verifier")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    try:
        return module.verify_profile(
            candidate,
            public_key_path=candidate_public_key,
            expected_compatibility=expected_compatibility,
        )
    except Exception as exc:
        raise ApprovalValidationError(f"candidate profile verification failed: {exc}") from exc


def create_approval(
    *,
    approval_id: str,
    created_utc: str,
    candidate_profile: dict[str, Any],
    candidate_public_key_path: Path,
    expected_compatibility: dict[str, Any],
    preflight_report: dict[str, Any],
    dataset_evidence: list[dict[str, Any]],
    heldout_report: dict[str, Any],
    approval_private_key_path: Path,
    approval_public_key_path: Path,
    approval_public_key_id: str,
    approver_statement: str,
    candidate_verifier: Callable[[dict[str, Any], Path, dict[str, Any]], dict[str, Any]]
    | None = None,
) -> dict[str, Any]:
    candidate_verifier = candidate_verifier or _verify_candidate_default
    candidate_verification = candidate_verifier(
        candidate_profile,
        candidate_public_key_path,
        expected_compatibility,
    )
    if candidate_profile.get("calibrated") is not False:
        raise _error("candidate.calibrated", "RMA-073 candidate must be false")
    if candidate_profile.get("approval_state") != "unapproved_fit_candidate":
        raise _error("candidate.approval_state", "must equal unapproved_fit_candidate")
    if candidate_verification.get("status") != "ok":
        raise _error("candidate", "verification did not return ok")
    hardware_id_sha256 = _validate_preflight(preflight_report)
    candidate_datasets = _candidate_datasets(candidate_profile)
    normalized_evidence = _validate_dataset_evidence(
        dataset_evidence,
        candidate_datasets,
        hardware_id_sha256,
    )
    normalized_report, passed_metrics, limited_metrics = _validate_heldout_report(
        heldout_report,
        hardware_id_sha256=hardware_id_sha256,
        candidate_datasets=candidate_datasets,
    )
    public_key_sha256 = hashlib.sha256(approval_public_key_path.read_bytes()).hexdigest()
    if public_key_sha256 in BLOCKED_APPROVAL_PUBLIC_KEY_SHA256:
        raise _error("signature.public_key_sha256", "fixture key cannot approve a real unit")
    statement = _require_string(approver_statement, "approver_statement")
    document = {
        "contract_id": CONTRACT_ID,
        "schema_version": SCHEMA_VERSION,
        "approval_id": _require_id(approval_id, "approval_id"),
        "profile_kind": PROFILE_KIND,
        "calibrated": True,
        "approval_state": APPROVAL_STATE,
        "approval_policy": APPROVAL_POLICY,
        "created_utc": _validate_utc(created_utc, "created_utc"),
        "unit": {"hardware_id_sha256": hardware_id_sha256},
        "candidate": {
            "contract_id": candidate_profile.get("contract_id"),
            "profile_id": candidate_profile.get("profile_id"),
            "profile_sha256": _candidate_hash(candidate_profile),
            "public_key_sha256": _require_hash(
                _require_dict(candidate_profile.get("signature"), "candidate.signature").get(
                    "public_key_sha256"
                ),
                "candidate.signature.public_key_sha256",
            ),
        },
        "compatibility": copy.deepcopy(expected_compatibility),
        "datasets": normalized_evidence,
        "parameter_results": copy.deepcopy(candidate_profile.get("parameter_results")),
        "heldout_report": normalized_report,
        "claims": {
            "passed_metrics": passed_metrics,
            "limited_metrics": limited_metrics,
            "mature_accuracy_claims": [
                normalized_report["metrics"][name]["claim_scope"] for name in passed_metrics
            ],
        },
        "approver_statement": statement,
        "integrity": {"algorithm": HASH_ALGORITHM, "approval_sha256": "0" * 64},
        "signature": {
            "algorithm": SIGNATURE_ALGORITHM,
            "public_key_id": _require_id(approval_public_key_id, "signature.public_key_id"),
            "public_key_sha256": public_key_sha256,
            "signature_base64": "",
        },
    }
    document["integrity"]["approval_sha256"] = compute_approval_sha256(document)
    document["signature"]["signature_base64"] = base64.b64encode(
        _openssl_sign(signature_payload_bytes(document), approval_private_key_path)
    ).decode("ascii")
    verify_approval(
        document,
        public_key_path=approval_public_key_path,
        expected_compatibility=expected_compatibility,
        expected_hardware_id_sha256=hardware_id_sha256,
    )
    return document


def verify_approval(
    document: Any,
    *,
    public_key_path: Path,
    expected_compatibility: dict[str, Any],
    expected_hardware_id_sha256: str,
) -> dict[str, Any]:
    value = _require_dict(document, "approval")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "schema_version",
            "approval_id",
            "profile_kind",
            "calibrated",
            "approval_state",
            "approval_policy",
            "created_utc",
            "unit",
            "candidate",
            "compatibility",
            "datasets",
            "parameter_results",
            "heldout_report",
            "claims",
            "approver_statement",
            "integrity",
            "signature",
        },
        set(),
        "approval",
    )
    if value["contract_id"] != CONTRACT_ID or value["schema_version"] != SCHEMA_VERSION:
        raise _error("approval", "unsupported contract or schema version")
    _require_id(value["approval_id"], "approval.approval_id")
    if value["profile_kind"] != PROFILE_KIND:
        raise _error("approval.profile_kind", f"must equal {PROFILE_KIND}")
    if _require_bool(value["calibrated"], "approval.calibrated") is not True:
        raise _error("approval.calibrated", "must be true")
    if value["approval_state"] != APPROVAL_STATE:
        raise _error("approval.approval_state", f"must equal {APPROVAL_STATE}")
    if value["approval_policy"] != APPROVAL_POLICY:
        raise _error("approval.approval_policy", f"must equal {APPROVAL_POLICY}")
    _validate_utc(value["created_utc"], "approval.created_utc")
    unit = _require_dict(value["unit"], "approval.unit")
    _require_exact_keys(unit, {"hardware_id_sha256"}, set(), "approval.unit")
    hardware_hash = _require_hash(unit["hardware_id_sha256"], "approval.unit.hardware_id_sha256")
    if hardware_hash != _require_hash(expected_hardware_id_sha256, "expected_hardware_id_sha256"):
        raise _error("approval.unit.hardware_id_sha256", "does not match connected unit")
    if value["compatibility"] != expected_compatibility:
        raise _error("approval.compatibility", "does not exactly match runtime compatibility")
    candidate = _require_dict(value["candidate"], "approval.candidate")
    _require_exact_keys(
        candidate,
        {"contract_id", "profile_id", "profile_sha256", "public_key_sha256"},
        set(),
        "approval.candidate",
    )
    if candidate["contract_id"] != "rma073_calibration_profile_manifest_v1":
        raise _error("approval.candidate.contract_id", "must identify RMA-073")
    _require_id(candidate["profile_id"], "approval.candidate.profile_id")
    _require_hash(candidate["profile_sha256"], "approval.candidate.profile_sha256")
    _require_hash(candidate["public_key_sha256"], "approval.candidate.public_key_sha256")
    datasets = _require_list(value["datasets"], "approval.datasets")
    if len(datasets) != 2:
        raise _error("approval.datasets", "must contain fitting and heldout entries")
    role_hashes: dict[str, str] = {}
    for index, raw in enumerate(datasets):
        path = f"approval.datasets[{index}]"
        item = _require_dict(raw, path)
        role = _require_string(item.get("role"), f"{path}.role")
        if role not in {"fitting", "heldout"} or role in role_hashes:
            raise _error(f"{path}.role", "must be a unique fitting or heldout role")
        if item.get("source_kind") != "physical_reachy_mini":
            raise _error(f"{path}.source_kind", "must be physical_reachy_mini")
        if item.get("hardware_id_sha256") != hardware_hash:
            raise _error(f"{path}.hardware_id_sha256", "does not match approved unit")
        if item.get("physical_motion") is not True:
            raise _error(f"{path}.physical_motion", "must be true")
        role_hashes[role] = _require_hash(item.get("dataset_sha256"), f"{path}.dataset_sha256")
    if (
        set(role_hashes) != {"fitting", "heldout"}
        or role_hashes["fitting"] == role_hashes["heldout"]
    ):
        raise _error("approval.datasets", "invalid fitting/heldout split")
    report, passed_metrics, limited_metrics = _validate_heldout_report(
        value["heldout_report"],
        hardware_id_sha256=hardware_hash,
        candidate_datasets={
            role: {
                "dataset_sha256": digest,
                "dataset_id": "runtime-bound",
                "role": role,
            }
            for role, digest in role_hashes.items()
        },
    )
    claims = _require_dict(value["claims"], "approval.claims")
    _require_exact_keys(
        claims,
        {"passed_metrics", "limited_metrics", "mature_accuracy_claims"},
        set(),
        "approval.claims",
    )
    if claims["passed_metrics"] != passed_metrics or claims["limited_metrics"] != limited_metrics:
        raise _error("approval.claims", "does not match held-out metric outcomes")
    expected_claims = [report["metrics"][name]["claim_scope"] for name in passed_metrics]
    if claims["mature_accuracy_claims"] != expected_claims:
        raise _error("approval.claims.mature_accuracy_claims", "contains an unpassed claim")
    _require_string(value["approver_statement"], "approval.approver_statement")
    integrity = _require_dict(value["integrity"], "approval.integrity")
    _require_exact_keys(integrity, {"algorithm", "approval_sha256"}, set(), "approval.integrity")
    if integrity["algorithm"] != HASH_ALGORITHM:
        raise _error("approval.integrity.algorithm", f"must equal {HASH_ALGORITHM}")
    expected_hash = _require_hash(
        integrity["approval_sha256"], "approval.integrity.approval_sha256"
    )
    actual_hash = compute_approval_sha256(value)
    if expected_hash != actual_hash:
        raise _error("approval.integrity.approval_sha256", "does not match content")
    signature = _require_dict(value["signature"], "approval.signature")
    _require_exact_keys(
        signature,
        {
            "algorithm",
            "public_key_id",
            "public_key_sha256",
            "signature_base64",
        },
        set(),
        "approval.signature",
    )
    if signature["algorithm"] != SIGNATURE_ALGORITHM:
        raise _error("approval.signature.algorithm", f"must equal {SIGNATURE_ALGORITHM}")
    _require_id(signature["public_key_id"], "approval.signature.public_key_id")
    public_key_sha256 = hashlib.sha256(public_key_path.read_bytes()).hexdigest()
    if public_key_sha256 in BLOCKED_APPROVAL_PUBLIC_KEY_SHA256:
        raise _error("approval.signature.public_key_sha256", "fixture key is blocked")
    if (
        _require_hash(signature["public_key_sha256"], "approval.signature.public_key_sha256")
        != public_key_sha256
    ):
        raise _error("approval.signature.public_key_sha256", "does not match public key")
    try:
        signature_bytes = base64.b64decode(
            _require_string(
                signature["signature_base64"],
                "approval.signature.signature_base64",
                1024,
            ),
            validate=True,
        )
    except Exception as exc:
        raise _error("approval.signature.signature_base64", "is not valid base64") from exc
    _openssl_verify(signature_payload_bytes(value), signature_bytes, public_key_path)
    return {
        "status": "ok",
        "approval_id": value["approval_id"],
        "approval_sha256": actual_hash,
        "hardware_id_sha256": hardware_hash,
        "calibrated": True,
        "label": "Calibrated for this unit",
        "passed_metrics": passed_metrics,
        "limited_metrics": limited_metrics,
    }
