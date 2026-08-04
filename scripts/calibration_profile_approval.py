"""RMA-074 physical calibration approval, verification, and UI label resolution."""

from __future__ import annotations

import base64
import copy
import hashlib
import json
import math
import re
import subprocess
import tempfile
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

CONTRACT_ID = "rma074_unit_calibrated_profile_v1"
SCHEMA_VERSION = 1
HASH_ALGORITHM = "sha256"
SIGNATURE_ALGORITHM = "ed25519"
PROFILE_KIND = "unit_calibrated"
APPROVAL_STATE = "approved"
APPROVAL_POLICY = "rma074_physical_calibration_gate_v1"
MAX_FILE_BYTES = 64 * 1024 * 1024
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
REQUIRED_METRICS = {
    "joint_position",
    "head_position",
    "head_orientation",
    "settling",
    "overshoot",
    "current",
    "free_decay",
    "contact",
}
CORE_APPROVAL_METRICS = {
    "joint_position",
    "head_position",
    "head_orientation",
    "settling",
    "overshoot",
    "free_decay",
}
METRIC_STATUSES = {"passed", "failed", "unsupported"}
BLOCKED_APPROVAL_PUBLIC_KEY_SHA256 = {
    "9ba232b4c60858fe77ef79bf14d1392d089de797d8236ec737c46d494bfdc75c",
}


class ApprovalValidationError(ValueError):
    """Raised when physical calibration approval evidence fails closed."""


@dataclass(frozen=True)
class LabelResolution:
    label: str
    calibrated: bool
    reason: str
    approval_id: str | None = None

    def to_document(self) -> dict[str, Any]:
        return {
            "label": self.label,
            "calibrated": self.calibrated,
            "reason": self.reason,
            "approval_id": self.approval_id,
        }


def canonical_json_bytes(value: Any) -> bytes:
    return (
        json.dumps(
            value,
            ensure_ascii=False,
            allow_nan=False,
            sort_keys=True,
            separators=(",", ":"),
        )
        + "\n"
    ).encode("utf-8")


def _error(path: str, message: str) -> ApprovalValidationError:
    return ApprovalValidationError(f"{path}: {message}")


def _require_dict(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise _error(path, "must be an object")
    return value


def _require_list(value: Any, path: str) -> list[Any]:
    if not isinstance(value, list):
        raise _error(path, "must be an array")
    return value


def _require_exact_keys(
    value: dict[str, Any],
    required: set[str],
    optional: set[str],
    path: str,
) -> None:
    missing = required - value.keys()
    if missing:
        raise _error(path, f"missing keys: {sorted(missing)}")
    unexpected = value.keys() - required - optional
    if unexpected:
        raise _error(path, f"unexpected keys: {sorted(unexpected)}")


def _require_string(value: Any, path: str, maximum: int = 4096) -> str:
    if not isinstance(value, str) or not value:
        raise _error(path, "must be a non-empty string")
    if len(value) > maximum:
        raise _error(path, f"exceeds {maximum} characters")
    return value


def _require_id(value: Any, path: str) -> str:
    text = _require_string(value, path, 128)
    if ID_PATTERN.fullmatch(text) is None:
        raise _error(path, "contains unsupported characters")
    return text


def _require_hash(value: Any, path: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase SHA-256 digest")
    return value


def _require_bool(value: Any, path: str) -> bool:
    if not isinstance(value, bool):
        raise _error(path, "must be boolean")
    return value


def _require_int(
    value: Any,
    path: str,
    *,
    minimum: int = 0,
    maximum: int = 2**63 - 1,
) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise _error(path, "must be an integer")
    if value < minimum or value > maximum:
        raise _error(path, f"must be between {minimum} and {maximum}")
    return value


def _require_number(value: Any, path: str) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise _error(path, "must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise _error(path, "must be finite")
    return number


def _validate_utc(value: Any, path: str) -> str:
    text = _require_string(value, path)
    if not text.endswith("Z"):
        raise _error(path, "must be an RFC 3339 UTC timestamp ending in Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise _error(path, "is not a valid RFC 3339 timestamp") from exc
    if parsed.utcoffset() is None or parsed.utcoffset().total_seconds() != 0:
        raise _error(path, "must use UTC")
    return text


def _strict_object_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ApprovalValidationError(f"JSON object contains duplicate key {key!r}")
        result[key] = value
    return result


def strict_json_loads(text: str) -> Any:
    def reject_constant(token: str) -> None:
        raise ApprovalValidationError(f"JSON contains non-finite constant {token}")

    try:
        return json.loads(
            text,
            parse_constant=reject_constant,
            object_pairs_hook=_strict_object_pairs,
        )
    except json.JSONDecodeError as exc:
        raise ApprovalValidationError(
            f"JSON parse error at line {exc.lineno}, column {exc.colno}: {exc.msg}"
        ) from exc


def load_json_file(path: Path) -> Any:
    raw = path.read_bytes()
    if len(raw) > MAX_FILE_BYTES:
        raise _error(str(path), f"file exceeds {MAX_FILE_BYTES} bytes")
    try:
        text = raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise ApprovalValidationError(f"{path}: file is not valid UTF-8") from exc
    return strict_json_loads(text)


def _candidate_hash(candidate: dict[str, Any]) -> str:
    integrity = _require_dict(candidate.get("integrity"), "candidate.integrity")
    return _require_hash(integrity.get("profile_sha256"), "candidate.integrity.profile_sha256")


def _candidate_datasets(candidate: dict[str, Any]) -> dict[str, dict[str, str]]:
    datasets = _require_list(candidate.get("datasets"), "candidate.datasets")
    by_role: dict[str, dict[str, str]] = {}
    for index, raw in enumerate(datasets):
        path = f"candidate.datasets[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {"dataset_id", "dataset_sha256", "role"},
            set(),
            path,
        )
        role = _require_string(value["role"], f"{path}.role")
        if role not in {"fitting", "heldout"}:
            raise _error(f"{path}.role", "must be fitting or heldout")
        if role in by_role:
            raise _error(f"{path}.role", "is duplicated")
        by_role[role] = {
            "dataset_id": _require_id(value["dataset_id"], f"{path}.dataset_id"),
            "dataset_sha256": _require_hash(value["dataset_sha256"], f"{path}.dataset_sha256"),
            "role": role,
        }
    if set(by_role) != {"fitting", "heldout"}:
        raise _error("candidate.datasets", "must contain exactly fitting and heldout roles")
    if by_role["fitting"]["dataset_sha256"] == by_role["heldout"]["dataset_sha256"]:
        raise _error("candidate.datasets", "fitting and heldout hashes must differ")
    return by_role


def _verify_candidate_default(
    candidate: dict[str, Any],
    candidate_public_key: Path,
    expected_compatibility: dict[str, Any],
) -> dict[str, Any]:
    import importlib.util
    import sys

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


def _validate_preflight(preflight: Any) -> str:
    value = _require_dict(preflight, "preflight")
    if value.get("contract_id") != "rma074_physical_preflight_v1":
        raise _error("preflight.contract_id", "is unsupported")
    if value.get("result") != "physical_unit_ready":
        raise _error("preflight.result", "must equal physical_unit_ready")
    unit = _require_dict(value.get("physical_unit"), "preflight.physical_unit")
    if unit.get("motion_commands_issued") != 0 or unit.get("torque_commands_issued") != 0:
        raise _error("preflight.physical_unit", "read-only preflight issued commands")
    return _require_hash(
        unit.get("hardware_id_sha256"),
        "preflight.physical_unit.hardware_id_sha256",
    )


def _validate_dataset_evidence(
    evidence: Any,
    candidate_datasets: dict[str, dict[str, str]],
    hardware_id_sha256: str,
) -> list[dict[str, Any]]:
    values = _require_list(evidence, "dataset_evidence")
    if len(values) != 2:
        raise _error("dataset_evidence", "must contain exactly two entries")
    by_role: dict[str, dict[str, Any]] = {}
    for index, raw in enumerate(values):
        path = f"dataset_evidence[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {
                "dataset_id",
                "dataset_sha256",
                "role",
                "source_kind",
                "hardware_id_sha256",
                "capture_run_id",
                "physical_motion",
            },
            {"artifact_sha256"},
            path,
        )
        role = _require_string(value["role"], f"{path}.role")
        if role not in {"fitting", "heldout"} or role in by_role:
            raise _error(f"{path}.role", "must be a unique fitting or heldout role")
        if value["source_kind"] != "physical_reachy_mini":
            raise _error(f"{path}.source_kind", "must equal physical_reachy_mini")
        if _require_bool(value["physical_motion"], f"{path}.physical_motion") is not True:
            raise _error(f"{path}.physical_motion", "must be true")
        if (
            _require_hash(value["hardware_id_sha256"], f"{path}.hardware_id_sha256")
            != hardware_id_sha256
        ):
            raise _error(f"{path}.hardware_id_sha256", "does not match preflight unit")
        candidate = candidate_datasets[role]
        if _require_id(value["dataset_id"], f"{path}.dataset_id") != candidate["dataset_id"]:
            raise _error(f"{path}.dataset_id", "does not match candidate profile")
        if (
            _require_hash(value["dataset_sha256"], f"{path}.dataset_sha256")
            != candidate["dataset_sha256"]
        ):
            raise _error(f"{path}.dataset_sha256", "does not match candidate profile")
        if "synthetic" in value["dataset_id"].lower():
            raise _error(f"{path}.dataset_id", "synthetic datasets cannot be approved")
        _require_id(value["capture_run_id"], f"{path}.capture_run_id")
        if "artifact_sha256" in value:
            _require_hash(value["artifact_sha256"], f"{path}.artifact_sha256")
        by_role[role] = copy.deepcopy(value)
    if set(by_role) != {"fitting", "heldout"}:
        raise _error("dataset_evidence", "must bind fitting and heldout evidence")
    if by_role["fitting"]["capture_run_id"] == by_role["heldout"]["capture_run_id"]:
        raise _error("dataset_evidence", "fitting and heldout must be separate physical runs")
    return [by_role["fitting"], by_role["heldout"]]


def _validate_metric(name: str, raw: Any) -> dict[str, Any]:
    path = f"heldout_report.metrics.{name}"
    value = _require_dict(raw, path)
    _require_exact_keys(
        value,
        {
            "status",
            "metric",
            "unit",
            "value",
            "threshold",
            "sample_count",
            "source_streams",
            "claim_scope",
        },
        {"reason"},
        path,
    )
    status = _require_string(value["status"], f"{path}.status")
    if status not in METRIC_STATUSES:
        raise _error(f"{path}.status", f"must be one of {sorted(METRIC_STATUSES)}")
    metric = _require_string(value["metric"], f"{path}.metric")
    unit = _require_string(value["unit"], f"{path}.unit")
    sample_count = _require_int(value["sample_count"], f"{path}.sample_count")
    streams = _require_list(value["source_streams"], f"{path}.source_streams")
    if len(streams) > 16:
        raise _error(f"{path}.source_streams", "contains too many entries")
    normalized_streams = [
        _require_id(item, f"{path}.source_streams[{i}]") for i, item in enumerate(streams)
    ]
    claim_scope = _require_string(value["claim_scope"], f"{path}.claim_scope")
    reason = value.get("reason")
    if status == "unsupported":
        if value["value"] is not None or value["threshold"] is not None:
            raise _error(path, "unsupported metrics must have null value and threshold")
        if sample_count != 0 or normalized_streams:
            raise _error(path, "unsupported metrics must have zero samples and no source streams")
        _require_string(reason, f"{path}.reason")
        numeric_value = None
        numeric_threshold = None
    else:
        numeric_value = _require_number(value["value"], f"{path}.value")
        numeric_threshold = _require_number(value["threshold"], f"{path}.threshold")
        if numeric_threshold < 0:
            raise _error(f"{path}.threshold", "must be non-negative")
        if sample_count <= 0 or not normalized_streams:
            raise _error(path, "measured metrics require samples and source streams")
        passed = numeric_value <= numeric_threshold
        if (status == "passed") != passed:
            raise _error(path, "status disagrees with value and threshold")
        if reason is not None:
            _require_string(reason, f"{path}.reason")
    return {
        "status": status,
        "metric": metric,
        "unit": unit,
        "value": numeric_value,
        "threshold": numeric_threshold,
        "sample_count": sample_count,
        "source_streams": normalized_streams,
        "claim_scope": claim_scope,
        **({"reason": reason} if reason is not None else {}),
    }


def _validate_heldout_report(
    report: Any,
    *,
    hardware_id_sha256: str,
    candidate_datasets: dict[str, dict[str, str]],
) -> tuple[dict[str, Any], list[str], list[str]]:
    value = _require_dict(report, "heldout_report")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "report_id",
            "created_utc",
            "hardware_id_sha256",
            "fitting_dataset_sha256",
            "heldout_dataset_sha256",
            "metrics",
            "report_sha256",
        },
        {"notes"},
        "heldout_report",
    )
    if value["contract_id"] != "rma074_physical_heldout_report_v1":
        raise _error("heldout_report.contract_id", "is unsupported")
    _require_id(value["report_id"], "heldout_report.report_id")
    _validate_utc(value["created_utc"], "heldout_report.created_utc")
    if (
        _require_hash(value["hardware_id_sha256"], "heldout_report.hardware_id_sha256")
        != hardware_id_sha256
    ):
        raise _error("heldout_report.hardware_id_sha256", "does not match physical unit")
    if (
        _require_hash(
            value["fitting_dataset_sha256"],
            "heldout_report.fitting_dataset_sha256",
        )
        != candidate_datasets["fitting"]["dataset_sha256"]
    ):
        raise _error("heldout_report.fitting_dataset_sha256", "does not match candidate")
    if (
        _require_hash(
            value["heldout_dataset_sha256"],
            "heldout_report.heldout_dataset_sha256",
        )
        != candidate_datasets["heldout"]["dataset_sha256"]
    ):
        raise _error("heldout_report.heldout_dataset_sha256", "does not match candidate")
    metrics = _require_dict(value["metrics"], "heldout_report.metrics")
    if set(metrics) != REQUIRED_METRICS:
        raise _error(
            "heldout_report.metrics",
            f"must contain exactly {sorted(REQUIRED_METRICS)}",
        )
    normalized_metrics = {name: _validate_metric(name, metrics[name]) for name in sorted(metrics)}
    for name in CORE_APPROVAL_METRICS:
        if normalized_metrics[name]["status"] != "passed":
            raise _error(
                f"heldout_report.metrics.{name}.status",
                "core calibration metric must pass before approval",
            )
    candidate = copy.deepcopy(value)
    expected_report_hash = _require_hash(candidate["report_sha256"], "heldout_report.report_sha256")
    candidate.pop("report_sha256")
    actual_report_hash = hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()
    if expected_report_hash != actual_report_hash:
        raise _error("heldout_report.report_sha256", "does not match report content")
    passed = sorted(
        name for name, metric in normalized_metrics.items() if metric["status"] == "passed"
    )
    limited = sorted(
        name for name, metric in normalized_metrics.items() if metric["status"] != "passed"
    )
    normalized = copy.deepcopy(value)
    normalized["metrics"] = normalized_metrics
    return normalized, passed, limited


def compute_approval_sha256(document: dict[str, Any]) -> str:
    candidate = copy.deepcopy(document)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("approval_sha256", None)
    signature = _require_dict(candidate.get("signature"), "signature")
    signature.pop("signature_base64", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def signature_payload_bytes(document: dict[str, Any]) -> bytes:
    candidate = copy.deepcopy(document)
    signature = _require_dict(candidate.get("signature"), "signature")
    signature.pop("signature_base64", None)
    return canonical_json_bytes(candidate)


def _openssl_sign(payload: bytes, private_key_path: Path) -> bytes:
    with tempfile.TemporaryDirectory() as temp_text:
        root = Path(temp_text)
        payload_path = root / "payload.bin"
        signature_path = root / "signature.bin"
        payload_path.write_bytes(payload)
        result = subprocess.run(
            [
                "openssl",
                "pkeyutl",
                "-sign",
                "-rawin",
                "-inkey",
                str(private_key_path),
                "-in",
                str(payload_path),
                "-out",
                str(signature_path),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            raise ApprovalValidationError(
                f"OpenSSL Ed25519 signing failed: {result.stderr.strip()}"
            )
        return signature_path.read_bytes()


def _openssl_verify(payload: bytes, signature: bytes, public_key_path: Path) -> None:
    with tempfile.TemporaryDirectory() as temp_text:
        root = Path(temp_text)
        payload_path = root / "payload.bin"
        signature_path = root / "signature.bin"
        payload_path.write_bytes(payload)
        signature_path.write_bytes(signature)
        result = subprocess.run(
            [
                "openssl",
                "pkeyutl",
                "-verify",
                "-rawin",
                "-pubin",
                "-inkey",
                str(public_key_path),
                "-in",
                str(payload_path),
                "-sigfile",
                str(signature_path),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            raise ApprovalValidationError("Ed25519 approval signature verification failed")


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


def resolve_calibration_label(
    approval: Any | None,
    *,
    public_key_path: Path | None,
    expected_compatibility: dict[str, Any],
    connected_hardware_id_sha256: str | None,
) -> LabelResolution:
    if approval is None:
        return LabelResolution(
            label="Uncalibrated",
            calibrated=False,
            reason="no approved calibration profile is installed",
        )
    if public_key_path is None:
        return LabelResolution(
            label="Uncalibrated",
            calibrated=False,
            reason="approval public key is unavailable",
        )
    if connected_hardware_id_sha256 is None:
        return LabelResolution(
            label="Uncalibrated",
            calibrated=False,
            reason="connected unit identity is unavailable",
        )
    try:
        result = verify_approval(
            approval,
            public_key_path=public_key_path,
            expected_compatibility=expected_compatibility,
            expected_hardware_id_sha256=connected_hardware_id_sha256,
        )
    except Exception as exc:
        return LabelResolution(
            label="Uncalibrated",
            calibrated=False,
            reason=f"approval verification failed: {exc}",
        )
    return LabelResolution(
        label="Calibrated for this unit",
        calibrated=True,
        reason="signed unit-specific approval verified",
        approval_id=result["approval_id"],
    )
