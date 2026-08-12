"""RMA-073 JSON I/O: strict JSON decoding plus the calibration_data sibling bootstrap."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends only on `FittingValidationError`, `ImportLimits`, and
# `_error` from calibration_fitting_validation. It is loaded either as part of
# the calibration_fitting.py facade's ordered bootstrap (in which case
# calibration_fitting_validation is already in sys.modules) or standalone /
# directly by path, in which case scripts/ is not necessarily on sys.path. To
# be self-sufficient in both cases, check sys.modules first and only fall back
# to loading the sibling by a path relative to this file if it isn't already
# registered.
if "calibration_fitting_validation" in sys.modules:
    calibration_fitting_validation = sys.modules["calibration_fitting_validation"]
else:
    _spec = importlib.util.spec_from_file_location(
        "calibration_fitting_validation",
        Path(__file__).with_name("calibration_fitting_validation.py"),
    )
    if _spec is None or _spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_fitting_validation.py")
    calibration_fitting_validation = importlib.util.module_from_spec(_spec)
    sys.modules["calibration_fitting_validation"] = calibration_fitting_validation
    _spec.loader.exec_module(calibration_fitting_validation)

FittingValidationError = calibration_fitting_validation.FittingValidationError
ImportLimits = calibration_fitting_validation.ImportLimits
DEFAULT_LIMITS = calibration_fitting_validation.DEFAULT_LIMITS
_error = calibration_fitting_validation._error


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
