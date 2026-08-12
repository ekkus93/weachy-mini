"""RMA-073 dataset loading: path resolution and fit-plan-driven dataset import."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_fitting_validation (for `_error`,
# `ImportLimits`, `DEFAULT_LIMITS`), calibration_fitting_jsonio (for the
# `calibration_data` module binding, so dataset files are parsed exactly once
# via a single sys.modules entry), and calibration_fitting_contracts (for
# `validate_fit_plan`) -- and both jsonio and contracts themselves depend on
# validation, so validation must be loaded first. It is loaded either as part
# of the calibration_fitting.py facade's ordered bootstrap (in which case all
# three siblings are already in sys.modules) or standalone / directly by
# path, in which case scripts/ is not necessarily on sys.path. To be
# self-sufficient in both cases, check sys.modules first and only fall back
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

ImportLimits = calibration_fitting_validation.ImportLimits
DEFAULT_LIMITS = calibration_fitting_validation.DEFAULT_LIMITS
_error = calibration_fitting_validation._error

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

calibration_data = calibration_fitting_jsonio.calibration_data

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

validate_fit_plan = calibration_fitting_contracts.validate_fit_plan


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
