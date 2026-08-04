"""RMA-073 deterministic parameter fitting, held-out validation, and signed profiles."""

from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import json
import math
import re
import statistics
import subprocess
import sys
import tempfile
from collections.abc import Iterable
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

FIT_PLAN_CONTRACT_ID = "rma073_calibration_fit_plan_v1"
FIT_PLAN_SCHEMA_VERSION = 1
FIT_PLAN_SCHEMA_SHA256 = "7460af9bbb1be0fd091d90ad2cee30c7601257ce804dfdcfe12fa77286caed69"
PROFILE_CONTRACT_ID = "rma073_calibration_profile_manifest_v1"
PROFILE_SCHEMA_VERSION = 1
PROFILE_SCHEMA_SHA256 = "a3a7f9e1b193997419a100890780b9f5730625bb83f235529d95cacae8cd1bd4"
HASH_ALGORITHM = "sha256"
SIGNATURE_ALGORITHM = "ed25519"
PROFILE_KIND = "fit_candidate_unapproved"
APPROVAL_STATE = "unapproved_fit_candidate"
GENERATOR_VERSION = "1.0.0"
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
GIT_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}(?:[0-9a-f]{24})?$")
PARAMETER_FAMILIES = (
    "friction",
    "backlash",
    "latency",
    "controller",
    "voltage",
    "compliance",
    "thermal",
)


class FittingValidationError(ValueError):
    """Raised when untrusted fitting input violates the contract."""


class SignatureError(RuntimeError):
    """Raised when profile signing or verification fails."""


@dataclass(frozen=True)
class ImportLimits:
    maximum_file_bytes: int = 64 * 1024 * 1024
    maximum_string_length: int = 4096
    maximum_datasets: int = 64
    maximum_samples_per_window: int = 1_000_000


DEFAULT_LIMITS = ImportLimits()


def _load_calibration_data() -> Any:
    try:
        import calibration_data  # type: ignore

        return calibration_data
    except ModuleNotFoundError:
        path = Path(__file__).with_name("calibration_data.py")
        spec = importlib.util.spec_from_file_location("calibration_data", path)
        if spec is None or spec.loader is None:
            raise RuntimeError("cannot load sibling calibration_data.py") from None
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        return module


calibration_data = _load_calibration_data()


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


def _error(path: str, message: str) -> FittingValidationError:
    return FittingValidationError(f"{path}: {message}")


def _require_dict(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise _error(path, "must be an object")
    return value


def _require_list(value: Any, path: str) -> list[Any]:
    if not isinstance(value, list):
        raise _error(path, "must be an array")
    return value


def _require_exact_keys(
    value: dict[str, Any], required: set[str], optional: set[str], path: str
) -> None:
    missing = required - value.keys()
    if missing:
        raise _error(path, f"missing keys: {sorted(missing)}")
    unexpected = value.keys() - required - optional
    if unexpected:
        raise _error(path, f"unexpected keys: {sorted(unexpected)}")


def _require_string(value: Any, path: str, limits: ImportLimits) -> str:
    if not isinstance(value, str) or not value:
        raise _error(path, "must be a non-empty string")
    if len(value) > limits.maximum_string_length:
        raise _error(path, "exceeds maximum string length")
    return value


def _require_id(value: Any, path: str, limits: ImportLimits) -> str:
    text = _require_string(value, path, limits)
    if ID_PATTERN.fullmatch(text) is None:
        raise _error(path, "contains unsupported characters or is too long")
    return text


def _require_integer(
    value: Any,
    path: str,
    *,
    minimum: int | None = None,
    maximum: int | None = None,
) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise _error(path, "must be an integer")
    if minimum is not None and value < minimum:
        raise _error(path, f"must be >= {minimum}")
    if maximum is not None and value > maximum:
        raise _error(path, f"must be <= {maximum}")
    return value


def _require_number(
    value: Any,
    path: str,
    *,
    minimum: float | None = None,
    maximum: float | None = None,
) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise _error(path, "must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise _error(path, "must be finite")
    if minimum is not None and number < minimum:
        raise _error(path, f"must be >= {minimum}")
    if maximum is not None and number > maximum:
        raise _error(path, f"must be <= {maximum}")
    return number


def _require_hash(value: Any, path: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase SHA-256 digest")
    return value


def _require_git_commit(value: Any, path: str) -> str:
    if not isinstance(value, str) or GIT_COMMIT_PATTERN.fullmatch(value) is None:
        raise _error(path, "must be a lowercase 40- or 64-hex Git commit")
    return value


def _validate_utc(value: Any, path: str, limits: ImportLimits) -> str:
    text = _require_string(value, path, limits)
    if not text.endswith("Z"):
        raise _error(path, "must be an RFC 3339 UTC timestamp ending in Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise _error(path, "is not a valid RFC 3339 timestamp") from exc
    if parsed.utcoffset() is None or parsed.utcoffset().total_seconds() != 0:
        raise _error(path, "must use UTC")
    return text


def strict_json_loads(text: str, *, source: str = "JSON") -> Any:
    try:
        return calibration_data.load_json_text(text, source=source)
    except Exception as exc:
        raise FittingValidationError(str(exc)) from exc


def load_json_file(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> Any:
    raw = path.read_bytes()
    if len(raw) > limits.maximum_file_bytes:
        raise _error(str(path), f"file size {len(raw)} exceeds {limits.maximum_file_bytes}")
    try:
        text = raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise _error(str(path), "is not valid UTF-8") from exc
    return strict_json_loads(text, source=str(path))


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


def _resolve_dataset_path(dataset_root: Path, relative: str) -> Path:
    root = dataset_root.resolve(strict=True)
    candidate = (root / relative).resolve(strict=True)
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise _error("dataset.path", "resolves outside dataset root") from exc
    if not candidate.is_file():
        raise _error("dataset.path", "must resolve to a regular file")
    return candidate


def load_datasets(
    plan: dict[str, Any], dataset_root: Path, *, limits: ImportLimits = DEFAULT_LIMITS
) -> dict[str, list[dict[str, Any]]]:
    validate_fit_plan(plan, limits=limits)
    result: dict[str, list[dict[str, Any]]] = {"fitting": [], "heldout": []}
    identity: tuple[Any, ...] | None = None
    for index, entry in enumerate(plan["datasets"]):
        path = _resolve_dataset_path(dataset_root, entry["path"])
        dataset = calibration_data.load_json_file(path)
        summary = calibration_data.validate_dataset(dataset)
        if summary["dataset_id"] != entry["dataset_id"]:
            raise _error(f"plan.datasets[{index}].dataset_id", "does not match loaded dataset")
        if summary["dataset_sha256"] != entry["dataset_sha256"]:
            raise _error(f"plan.datasets[{index}].dataset_sha256", "does not match loaded dataset")
        robot = dataset["robot"]
        current_identity = (
            robot["robot_id"],
            robot["hardware_revision"],
            robot["firmware_version"],
            robot["register_configuration_sha256"],
        )
        if identity is None:
            identity = current_identity
        elif current_identity != identity:
            raise _error(
                f"plan.datasets[{index}]", "robot identity/configuration differs across split"
            )
        result[entry["role"]].append(dataset)
    return result


def _streams(dataset: dict[str, Any], sample_type: str) -> list[dict[str, Any]]:
    return [stream for stream in dataset["streams"] if stream["sample_type"] == sample_type]


def _samples(
    datasets: Iterable[dict[str, Any]],
    sample_type: str,
    start_ns: int,
    end_ns: int,
    *,
    actuator_id: str | None = None,
) -> list[dict[str, Any]]:
    values: list[dict[str, Any]] = []
    for dataset in datasets:
        for stream in _streams(dataset, sample_type):
            for sample in stream["samples"]:
                timestamp = sample["timestamp_ns"]
                if start_ns <= timestamp <= end_ns:
                    if actuator_id is not None:
                        sample_actuator = sample.get("actuator_id") or sample.get("sensor_id")
                        if sample_actuator != actuator_id:
                            continue
                    values.append(sample)
    values.sort(key=lambda sample: (sample["timestamp_ns"], sample["sequence"]))
    return values


def _nearest_by_timestamp(
    left: list[dict[str, Any]], right: list[dict[str, Any]], *, tolerance_ns: int
) -> list[tuple[dict[str, Any], dict[str, Any]]]:
    if not left or not right:
        return []
    pairs: list[tuple[dict[str, Any], dict[str, Any]]] = []
    j = 0
    for first in left:
        while j + 1 < len(right) and abs(
            right[j + 1]["timestamp_ns"] - first["timestamp_ns"]
        ) <= abs(right[j]["timestamp_ns"] - first["timestamp_ns"]):
            j += 1
        if abs(right[j]["timestamp_ns"] - first["timestamp_ns"]) <= tolerance_ns:
            pairs.append((first, right[j]))
    return pairs


def _solve_linear(
    features: list[list[float]], targets: list[float]
) -> tuple[list[float], dict[str, Any]]:
    if not features or len(features) != len(targets):
        raise FittingValidationError("linear fit requires non-empty aligned observations")
    width = len(features[0])
    if width == 0 or any(len(row) != width for row in features):
        raise FittingValidationError("linear fit feature width is inconsistent")
    if len(features) <= width:
        raise FittingValidationError("linear fit is underdetermined")
    matrix = [[0.0 for _ in range(width + 1)] for _ in range(width)]
    for row, target in zip(features, targets, strict=True):
        for i in range(width):
            for j in range(width):
                matrix[i][j] += row[i] * row[j]
            matrix[i][width] += row[i] * target
    pivots: list[float] = []
    for col in range(width):
        pivot = max(range(col, width), key=lambda row: abs(matrix[row][col]))
        if abs(matrix[pivot][col]) < 1e-14:
            raise FittingValidationError("linear fit design matrix is singular")
        matrix[col], matrix[pivot] = matrix[pivot], matrix[col]
        pivot_value = matrix[col][col]
        pivots.append(abs(pivot_value))
        for entry in range(col, width + 1):
            matrix[col][entry] /= pivot_value
        for row in range(width):
            if row == col:
                continue
            factor = matrix[row][col]
            for entry in range(col, width + 1):
                matrix[row][entry] -= factor * matrix[col][entry]
    coefficients = [matrix[index][width] for index in range(width)]
    predictions = [sum(c * x for c, x in zip(coefficients, row, strict=True)) for row in features]
    residuals = [
        target - prediction for target, prediction in zip(targets, predictions, strict=True)
    ]
    rmse = math.sqrt(sum(value * value for value in residuals) / len(residuals))
    mean_target = statistics.fmean(targets)
    total = sum((value - mean_target) ** 2 for value in targets)
    unexplained = sum(value * value for value in residuals)
    r2 = 1.0 - unexplained / total if total > 0 else (1.0 if unexplained == 0 else 0.0)
    condition_proxy = max(pivots) / min(pivots)
    return coefficients, {
        "observation_count": len(targets),
        "training_rmse": rmse,
        "training_r2": r2,
        "condition_proxy": condition_proxy,
    }


def _confidence(metrics: dict[str, Any], sensitivity: float) -> str:
    count = metrics.get("observation_count", 0)
    r2 = metrics.get("training_r2", 0.0)
    if count >= 40 and r2 >= 0.98 and sensitivity <= 0.05:
        return "high_for_supplied_dataset"
    if count >= 15 and r2 >= 0.9 and sensitivity <= 0.2:
        return "medium_for_supplied_dataset"
    return "low_for_supplied_dataset"


def _jackknife_sensitivity(
    features: list[list[float]], targets: list[float], baseline: list[float]
) -> float:
    if len(features) < max(8, len(baseline) + 3):
        return 1.0
    indexes = sorted({round(i * (len(features) - 1) / 15) for i in range(16)})
    maximum = 0.0
    for omitted in indexes:
        reduced_features = [row for index, row in enumerate(features) if index != omitted]
        reduced_targets = [value for index, value in enumerate(targets) if index != omitted]
        coefficients, _ = _solve_linear(reduced_features, reduced_targets)
        for original, changed in zip(baseline, coefficients, strict=True):
            denominator = max(abs(original), 1e-12)
            maximum = max(maximum, abs(changed - original) / denominator)
    return maximum


def _unsupported(reason: str, required_streams: list[str]) -> dict[str, Any]:
    return {
        "status": "unsupported",
        "reason": reason,
        "required_streams": required_streams,
        "values": {},
        "confidence": "not_estimated",
        "sensitivity": None,
    }


def _fit_friction(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    observations = [sample for sample in joints if abs(float(sample["velocity_rad_s"])) >= 0.03]
    if len(observations) < 12:
        return _unsupported("insufficient nonzero-velocity joint samples", ["joint"])
    features = [
        [math.copysign(1.0, sample["velocity_rad_s"]), sample["velocity_rad_s"]]
        for sample in observations
    ]
    targets = [sample["applied_torque_nm"] for sample in observations]
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_coulomb_plus_viscous",
        "values": {
            "coulomb_friction_nm": coefficients[0],
            "viscous_friction_nm_s_per_rad": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["joint"],
    }


def _estimate_backlash(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    positive: list[float] = []
    negative: list[float] = []
    for command, joint in pairs:
        velocity = command.get("target_velocity_rad_s")
        target = command.get("target_position_rad")
        if velocity is None or target is None or abs(velocity) < 1e-9:
            continue
        residual = float(target) - float(joint["position_rad"])
        (positive if velocity > 0 else negative).append(residual)
    if len(positive) < 5 or len(negative) < 5:
        return _unsupported("insufficient bidirectional reversal samples", ["command", "joint"])
    positive_median = statistics.median(positive)
    negative_median = statistics.median(negative)
    half_width = abs(positive_median - negative_median) / 2.0
    deviations = [abs(value - positive_median) for value in positive] + [
        abs(value - negative_median) for value in negative
    ]
    mad = statistics.median(deviations)
    sensitivity = mad / max(half_width, 1e-12)
    return {
        "status": "fitted",
        "method": "bidirectional_residual_median",
        "values": {"backlash_half_width_rad": half_width},
        "metrics": {
            "observation_count": len(positive) + len(negative),
            "direction_positive_count": len(positive),
            "direction_negative_count": len(negative),
            "median_absolute_deviation_rad": mad,
        },
        "confidence": "high_for_supplied_dataset"
        if sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _estimate_latency(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    if len(commands) < 8 or len(joints) < 8:
        return _unsupported("insufficient command and joint samples", ["command", "joint"])
    command_steps: list[tuple[int, float]] = []
    previous: float | None = None
    for sample in commands:
        target = sample.get("target_position_rad")
        if target is None:
            continue
        value = float(target)
        if previous is not None and abs(value - previous) >= 0.05:
            command_steps.append((sample["timestamp_ns"], value - previous))
        previous = value
    response_steps: list[tuple[int, float]] = []
    previous_position: float | None = None
    for sample in joints:
        position = float(sample["position_rad"])
        if previous_position is not None:
            delta = position - previous_position
            if abs(delta) >= 0.02:
                response_steps.append((sample["timestamp_ns"], delta))
        previous_position = position
    latencies: list[int] = []
    for command_time, command_delta in command_steps:
        for response_time, response_delta in response_steps:
            if response_time >= command_time and math.copysign(
                1.0, response_delta
            ) == math.copysign(1.0, command_delta):
                delta = response_time - command_time
                if delta <= 1_000_000_000:
                    latencies.append(delta)
                    break
    if len(latencies) < 3:
        return _unsupported(
            "insufficient matched command-response transitions", ["command", "joint"]
        )
    median_ns = int(statistics.median(latencies))
    mad_ns = statistics.median(abs(value - median_ns) for value in latencies)
    sensitivity = mad_ns / max(median_ns, 1)
    return {
        "status": "fitted",
        "method": "matched_step_transition_median",
        "values": {"command_latency_s": median_ns / 1e9},
        "metrics": {
            "observation_count": len(latencies),
            "median_latency_ns": median_ns,
            "mad_ns": mad_ns,
        },
        "confidence": "high_for_supplied_dataset"
        if len(latencies) >= 5 and sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _fit_controller(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    features: list[list[float]] = []
    targets: list[float] = []
    for command, joint in pairs:
        target_position = command.get("target_position_rad")
        if target_position is None:
            continue
        error = float(target_position) - float(joint["position_rad"])
        features.append([error, -float(joint["velocity_rad_s"])])
        targets.append(float(joint["applied_torque_nm"]))
    if len(features) < 12:
        return _unsupported("insufficient aligned command/joint observations", ["command", "joint"])
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_pd_controller",
        "values": {
            "position_gain_nm_per_rad": coefficients[0],
            "velocity_gain_nm_s_per_rad": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["command", "joint"],
    }


def _fit_voltage(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
    voltages = _samples(datasets, "voltage", **window)
    pairs = _nearest_by_timestamp(currents, voltages, tolerance_ns=2_000_000)
    if len(pairs) < 12:
        return _unsupported(
            "insufficient aligned current and voltage observations", ["current_load", "voltage"]
        )
    features = [[1.0, -float(current["current_a"])] for current, _ in pairs]
    targets = [float(voltage["voltage_v"]) for _, voltage in pairs]
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_shared_bus_sag",
        "values": {
            "open_circuit_voltage_v": coefficients[0],
            "source_impedance_ohm": coefficients[1],
        },
        "metrics": metrics,
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["current_load", "voltage"],
    }


def _estimate_compliance(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    commands = _samples(datasets, "command", **window, actuator_id=actuator)
    joints = _samples(datasets, "joint", **window, actuator_id=actuator)
    loads = _samples(datasets, "current_load", **window, actuator_id=actuator)
    command_joint = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
    load_by_time = {sample["timestamp_ns"]: sample for sample in loads}
    stiffness_values: list[float] = []
    for command, joint in command_joint:
        target = command.get("target_position_rad")
        if target is None:
            continue
        load = load_by_time.get(command["timestamp_ns"])
        if load is None or load.get("estimated_load_nm") is None:
            continue
        deflection = float(target) - float(joint["position_rad"])
        torque = float(load["estimated_load_nm"])
        if abs(deflection) >= 1e-4 and abs(torque) >= 1e-4 and deflection * torque > 0:
            stiffness_values.append(torque / deflection)
    if len(stiffness_values) < 8:
        return _unsupported(
            "insufficient load/deflection observations", ["command", "joint", "current_load"]
        )
    estimate = statistics.median(stiffness_values)
    mad = statistics.median(abs(value - estimate) for value in stiffness_values)
    sensitivity = mad / max(abs(estimate), 1e-12)
    return {
        "status": "fitted",
        "method": "median_load_over_deflection",
        "values": {"compliance_stiffness_nm_per_rad": estimate},
        "metrics": {
            "observation_count": len(stiffness_values),
            "median_absolute_deviation_nm_per_rad": mad,
        },
        "confidence": "high_for_supplied_dataset"
        if sensitivity <= 0.1
        else "medium_for_supplied_dataset",
        "sensitivity": {"mad_relative_to_estimate": sensitivity},
        "required_streams": ["command", "joint", "current_load"],
    }


def _fit_thermal(
    datasets: list[dict[str, Any]], window: dict[str, int], actuator: str
) -> dict[str, Any]:
    currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
    temperatures = _samples(datasets, "temperature", **window, actuator_id=actuator)
    pairs = _nearest_by_timestamp(temperatures, currents, tolerance_ns=2_000_000)
    if len(pairs) < 20:
        return _unsupported(
            "insufficient aligned thermal observations", ["temperature", "current_load"]
        )
    features: list[list[float]] = []
    targets: list[float] = []
    ambient_values: list[float] = []
    for dataset in datasets:
        ambient = dataset.get("environment", {}).get("ambient_temperature_c")
        if ambient is not None:
            ambient_values.append(float(ambient))
    if not ambient_values:
        return _unsupported("ambient temperature is missing", ["temperature", "current_load"])
    ambient = statistics.fmean(ambient_values)
    for index in range(1, len(pairs)):
        previous_temperature, _ = pairs[index - 1]
        temperature, current = pairs[index]
        dt = (temperature["timestamp_ns"] - previous_temperature["timestamp_ns"]) / 1e9
        if dt <= 0:
            continue
        derivative = (
            float(temperature["temperature_c"]) - float(previous_temperature["temperature_c"])
        ) / dt
        previous_value = float(previous_temperature["temperature_c"])
        features.append([float(current["current_a"]) ** 2, -(previous_value - ambient)])
        targets.append(derivative)
    if len(features) < 15:
        return _unsupported(
            "insufficient positive-duration thermal intervals", ["temperature", "current_load"]
        )
    coefficients, metrics = _solve_linear(features, targets)
    sensitivity = _jackknife_sensitivity(features, targets, coefficients)
    return {
        "status": "fitted",
        "method": "ordinary_least_squares_lumped_thermal",
        "values": {"heating_c_per_a2_s": coefficients[0], "cooling_per_s": coefficients[1]},
        "metrics": metrics | {"ambient_temperature_c": ambient},
        "confidence": _confidence(metrics, sensitivity),
        "sensitivity": {"maximum_leave_one_out_relative_change": sensitivity},
        "required_streams": ["temperature", "current_load"],
    }


_FITTERS = {
    "friction": _fit_friction,
    "backlash": _estimate_backlash,
    "latency": _estimate_latency,
    "controller": _fit_controller,
    "voltage": _fit_voltage,
    "compliance": _estimate_compliance,
    "thermal": _fit_thermal,
}


def fit_parameters(
    fitting_datasets: list[dict[str, Any]], plan: dict[str, Any]
) -> dict[str, dict[str, Any]]:
    summary = validate_fit_plan(plan)
    actuator = summary["actuator_id"]
    results: dict[str, dict[str, Any]] = {}
    for family in PARAMETER_FAMILIES:
        results[family] = _FITTERS[family](fitting_datasets, summary["windows"][family], actuator)
    return results


def _prediction_error(
    family: str,
    result: dict[str, Any],
    datasets: list[dict[str, Any]],
    window: dict[str, int],
    actuator: str,
) -> tuple[float | None, int, str]:
    if result["status"] != "fitted":
        return None, 0, "parameter_not_fitted"
    values = result["values"]
    if family == "friction":
        joints = _samples(datasets, "joint", **window, actuator_id=actuator)
        observations = [sample for sample in joints if abs(float(sample["velocity_rad_s"])) >= 0.03]
        errors = []
        for sample in observations:
            velocity = float(sample["velocity_rad_s"])
            prediction = (
                values["coulomb_friction_nm"] * math.copysign(1.0, velocity)
                + values["viscous_friction_nm_s_per_rad"] * velocity
            )
            errors.append(float(sample["applied_torque_nm"]) - prediction)
        return _rmse(errors), len(errors), "rmse_nm"
    if family == "controller":
        commands = _samples(datasets, "command", **window, actuator_id=actuator)
        joints = _samples(datasets, "joint", **window, actuator_id=actuator)
        pairs = _nearest_by_timestamp(commands, joints, tolerance_ns=2_000_000)
        errors = []
        for command, joint in pairs:
            target = command.get("target_position_rad")
            if target is None:
                continue
            prediction = values["position_gain_nm_per_rad"] * (
                float(target) - float(joint["position_rad"])
            ) - values["velocity_gain_nm_s_per_rad"] * float(joint["velocity_rad_s"])
            errors.append(float(joint["applied_torque_nm"]) - prediction)
        return _rmse(errors), len(errors), "rmse_nm"
    if family == "voltage":
        currents = _samples(datasets, "current_load", **window, actuator_id=actuator)
        voltages = _samples(datasets, "voltage", **window)
        pairs = _nearest_by_timestamp(currents, voltages, tolerance_ns=2_000_000)
        errors = [
            float(voltage["voltage_v"])
            - (
                values["open_circuit_voltage_v"]
                - values["source_impedance_ohm"] * float(current["current_a"])
            )
            for current, voltage in pairs
        ]
        return _rmse(errors), len(errors), "rmse_v"
    if family == "thermal":
        temporary = _fit_thermal(datasets, window, actuator)
        if temporary["status"] != "fitted":
            return None, 0, "heldout_estimate_unavailable"
        expected = temporary["values"]
        relative = max(
            abs(values[key] - expected[key]) / max(abs(expected[key]), 1e-12)
            for key in ("heating_c_per_a2_s", "cooling_per_s")
        )
        return (
            relative,
            temporary["metrics"]["observation_count"],
            "maximum_relative_parameter_error",
        )
    temporary = _FITTERS[family](datasets, window, actuator)
    if temporary["status"] != "fitted":
        return None, 0, "heldout_estimate_unavailable"
    key = next(iter(values))
    expected = temporary["values"][key]
    relative = abs(values[key] - expected) / max(abs(expected), 1e-12)
    return relative, temporary["metrics"]["observation_count"], "relative_parameter_error"


def _rmse(errors: list[float]) -> float | None:
    if not errors:
        return None
    return math.sqrt(sum(value * value for value in errors) / len(errors))


def validate_heldout(
    parameter_results: dict[str, dict[str, Any]],
    heldout_datasets: list[dict[str, Any]],
    plan: dict[str, Any],
) -> dict[str, Any]:
    summary = validate_fit_plan(plan)
    family_results: dict[str, dict[str, Any]] = {}
    all_supported_passed = True
    supported_count = 0
    for family in PARAMETER_FAMILIES:
        error, count, metric = _prediction_error(
            family,
            parameter_results[family],
            heldout_datasets,
            summary["windows"][family],
            summary["actuator_id"],
        )
        threshold = summary["thresholds"][family]
        if error is None:
            status = "unsupported"
            passed = None
        else:
            supported_count += 1
            passed = error <= threshold
            status = "passed" if passed else "failed"
            all_supported_passed = all_supported_passed and passed
        family_results[family] = {
            "status": status,
            "metric": metric,
            "value": error,
            "threshold": threshold,
            "observation_count": count,
            "passed": passed,
        }
    return {
        "split_policy": "heldout datasets are loaded only after fitting results are frozen",
        "supported_family_count": supported_count,
        "all_supported_passed": all_supported_passed and supported_count > 0,
        "families": family_results,
    }


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
