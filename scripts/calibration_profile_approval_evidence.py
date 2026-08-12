"""RMA-074 approval evidence validation: preflight, dataset provenance, held-out report."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_profile_approval_validation (for the
# metric constants, `_require_*`/`_validate_utc` primitives, and
# `canonical_json_bytes`). It is loaded either as part of the
# calibration_profile_approval.py facade's ordered bootstrap (in which case
# the sibling is already in sys.modules) or standalone / directly by path, in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient
# in both cases, check sys.modules first and only fall back to loading the
# sibling by a path relative to this file if it isn't already registered.
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

REQUIRED_METRICS = calibration_profile_approval_validation.REQUIRED_METRICS
CORE_APPROVAL_METRICS = calibration_profile_approval_validation.CORE_APPROVAL_METRICS
METRIC_STATUSES = calibration_profile_approval_validation.METRIC_STATUSES
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
