"""RMA-070 calibration dataset integrity: hashing, finalization, and strict JSON loading."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_data_contracts (for the schema constants,
# `CalibrationValidationError`, `ImportLimits`, `_require_dict`,
# `_validate_hash`, `canonical_json_bytes`), calibration_data_metadata (for
# `_validate_schema`/`_validate_robot`/`_validate_environment`/
# `_validate_clocks`/`_validate_capture`/`_validate_alignments`), and
# calibration_data_samples (for `_validate_streams`/`_validate_source_files`)
# -- and both metadata and samples themselves depend on contracts, so
# contracts must be loaded first. It is loaded either as part of the
# calibration_data.py facade's ordered bootstrap (in which case all three
# siblings are already in sys.modules) or standalone / directly by path, in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient
# in both cases, check sys.modules first and only fall back to loading each
# sibling by a path relative to this file if it isn't already registered.
if "calibration_data_contracts" in sys.modules:
    calibration_data_contracts = sys.modules["calibration_data_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_data_contracts",
        Path(__file__).with_name("calibration_data_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_data_contracts.py")
    calibration_data_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_data_contracts"] = calibration_data_contracts
    _contracts_spec.loader.exec_module(calibration_data_contracts)

CONTRACT_ID = calibration_data_contracts.CONTRACT_ID
SCHEMA_VERSION = calibration_data_contracts.SCHEMA_VERSION
COLUMN_MANIFEST_ID = calibration_data_contracts.COLUMN_MANIFEST_ID
HASH_ALGORITHM = calibration_data_contracts.HASH_ALGORITHM
EXPECTED_SCHEMA_SHA256 = calibration_data_contracts.EXPECTED_SCHEMA_SHA256
EXPECTED_COLUMN_MANIFEST_SHA256 = calibration_data_contracts.EXPECTED_COLUMN_MANIFEST_SHA256
TOP_LEVEL_KEYS = calibration_data_contracts.TOP_LEVEL_KEYS
CalibrationValidationError = calibration_data_contracts.CalibrationValidationError
ImportLimits = calibration_data_contracts.ImportLimits
DEFAULT_LIMITS = calibration_data_contracts.DEFAULT_LIMITS
canonical_json_bytes = calibration_data_contracts.canonical_json_bytes
_error = calibration_data_contracts._error
_require_dict = calibration_data_contracts._require_dict
_require_exact_keys = calibration_data_contracts._require_exact_keys
_require_id = calibration_data_contracts._require_id
_validate_iso_utc = calibration_data_contracts._validate_iso_utc
_validate_hash = calibration_data_contracts._validate_hash

if "calibration_data_metadata" in sys.modules:
    calibration_data_metadata = sys.modules["calibration_data_metadata"]
else:
    _metadata_spec = importlib.util.spec_from_file_location(
        "calibration_data_metadata",
        Path(__file__).with_name("calibration_data_metadata.py"),
    )
    if _metadata_spec is None or _metadata_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_data_metadata.py")
    calibration_data_metadata = importlib.util.module_from_spec(_metadata_spec)
    sys.modules["calibration_data_metadata"] = calibration_data_metadata
    _metadata_spec.loader.exec_module(calibration_data_metadata)

_validate_schema = calibration_data_metadata._validate_schema
_validate_robot = calibration_data_metadata._validate_robot
_validate_environment = calibration_data_metadata._validate_environment
_validate_capture = calibration_data_metadata._validate_capture
_validate_clocks = calibration_data_metadata._validate_clocks
_validate_alignments = calibration_data_metadata._validate_alignments

if "calibration_data_samples" in sys.modules:
    calibration_data_samples = sys.modules["calibration_data_samples"]
else:
    _samples_spec = importlib.util.spec_from_file_location(
        "calibration_data_samples",
        Path(__file__).with_name("calibration_data_samples.py"),
    )
    if _samples_spec is None or _samples_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_data_samples.py")
    calibration_data_samples = importlib.util.module_from_spec(_samples_spec)
    sys.modules["calibration_data_samples"] = calibration_data_samples
    _samples_spec.loader.exec_module(calibration_data_samples)

_validate_streams = calibration_data_samples._validate_streams
_validate_source_files = calibration_data_samples._validate_source_files


def compute_dataset_sha256(dataset: dict[str, Any]) -> str:
    """Hash the complete dataset with the self-referential digest removed."""

    candidate = copy.deepcopy(dataset)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("dataset_sha256", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def finalize_dataset(dataset: dict[str, Any]) -> dict[str, Any]:
    """Return a deep-copied dataset with its canonical SHA-256 populated."""

    finalized = copy.deepcopy(dataset)
    integrity = finalized.setdefault("integrity", {})
    if not isinstance(integrity, dict):
        raise _error("integrity", "must be an object")
    integrity["algorithm"] = HASH_ALGORITHM
    integrity["dataset_sha256"] = compute_dataset_sha256(finalized)
    return finalized


def validate_dataset(
    dataset: Any,
    *,
    limits: ImportLimits = DEFAULT_LIMITS,
    verify_integrity: bool = True,
) -> dict[str, Any]:
    """Validate an untrusted dataset and return a compact deterministic summary."""

    value = _require_dict(dataset, "dataset")
    _require_exact_keys(value, TOP_LEVEL_KEYS, set(), "dataset")
    _validate_schema(value["schema"], limits)
    _require_id(value["dataset_id"], "dataset.dataset_id", limits)
    _validate_iso_utc(value["created_utc"], "dataset.created_utc", limits)
    _validate_robot(value["robot"], limits)
    _validate_environment(value["environment"], limits)
    clock_ids = _validate_clocks(value["clocks"], limits)
    primary_clock_id, declared_sync_state = _validate_capture(value["capture"], clock_ids, limits)
    alignments, derived_sync_state = _validate_alignments(
        value["clock_alignments"],
        clock_ids,
        primary_clock_id,
        limits,
    )
    if declared_sync_state != derived_sync_state:
        raise _error(
            "capture.synchronization_state",
            f"declares {declared_sync_state!r} but alignments derive {derived_sync_state!r}",
        )
    total_samples, type_counts = _validate_streams(
        value["streams"],
        clock_ids,
        primary_clock_id,
        alignments,
        limits,
    )
    _validate_source_files(value["source_files"], limits)
    integrity = _require_dict(value["integrity"], "integrity")
    _require_exact_keys(integrity, {"algorithm", "dataset_sha256"}, set(), "integrity")
    if integrity["algorithm"] != HASH_ALGORITHM:
        raise _error("integrity.algorithm", f"must equal {HASH_ALGORITHM}")
    expected_hash = _validate_hash(integrity["dataset_sha256"], "integrity.dataset_sha256")
    actual_hash = compute_dataset_sha256(value)
    if verify_integrity and expected_hash != actual_hash:
        raise _error("integrity.dataset_sha256", "does not match canonical dataset content")
    return {
        "contract_id": CONTRACT_ID,
        "dataset_id": value["dataset_id"],
        "dataset_sha256": actual_hash,
        "stream_count": len(value["streams"]),
        "sample_count": total_samples,
        "sample_type_counts": dict(sorted(type_counts.items())),
        "clock_count": len(clock_ids),
        "synchronization_state": derived_sync_state,
        "status": "ok",
    }


def load_json_text(text: str, *, source: str = "JSON") -> Any:
    """Load strict JSON text, rejecting duplicate keys and non-finite constants."""

    def reject_constant(value: str) -> None:
        raise CalibrationValidationError(f"{source}: contains non-finite constant {value}")

    def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise CalibrationValidationError(
                    f"{source}: JSON object contains duplicate key {key!r}"
                )
            result[key] = value
        return result

    try:
        return json.loads(
            text,
            parse_constant=reject_constant,
            object_pairs_hook=reject_duplicate_keys,
        )
    except json.JSONDecodeError as exc:
        raise CalibrationValidationError(
            f"{source}: invalid JSON at line {exc.lineno} column {exc.colno}: {exc.msg}"
        ) from exc


def load_json_file(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> Any:
    """Load bounded strict UTF-8 JSON without a stat/read race."""

    data = path.read_bytes()
    size = len(data)
    if size > limits.maximum_file_bytes:
        raise _error(str(path), f"file size {size} exceeds {limits.maximum_file_bytes}")
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise CalibrationValidationError(f"{path}: is not valid UTF-8") from exc
    return load_json_text(text, source=str(path))


def schema_descriptor(schema_root: Path) -> dict[str, Any]:
    """Return the exact schema identifiers and hashes embedded in datasets."""

    schema_path = schema_root / "calibration-dataset-v1.schema.json"
    columns_path = schema_root / "calibration-stream-columns-v1.json"
    descriptor = {
        "contract_id": CONTRACT_ID,
        "schema_version": SCHEMA_VERSION,
        "schema_sha256": hashlib.sha256(schema_path.read_bytes()).hexdigest(),
        "column_manifest_id": COLUMN_MANIFEST_ID,
        "column_manifest_sha256": hashlib.sha256(columns_path.read_bytes()).hexdigest(),
    }
    if descriptor["schema_sha256"] != EXPECTED_SCHEMA_SHA256:
        raise CalibrationValidationError(
            "calibration-dataset-v1.schema.json does not match the pinned v1 hash"
        )
    if descriptor["column_manifest_sha256"] != EXPECTED_COLUMN_MANIFEST_SHA256:
        raise CalibrationValidationError(
            "calibration-stream-columns-v1.json does not match the pinned v1 hash"
        )
    return descriptor
