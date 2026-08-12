"""RMA-074 fail-closed resolution of the user-facing calibration label."""

from __future__ import annotations

import importlib.util
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_profile_approval_core (for
# `verify_approval` -- the only consumer of it outside `create_approval`'s
# own self-check). It is loaded either as part of the
# calibration_profile_approval.py facade's ordered bootstrap (in which case
# the sibling is already in sys.modules) or standalone / directly by path, in
# which case scripts/ is not necessarily on sys.path. To be self-sufficient
# in both cases, check sys.modules first and only fall back to loading the
# sibling by a path relative to this file if it isn't already registered.
if "calibration_profile_approval_core" in sys.modules:
    calibration_profile_approval_core = sys.modules["calibration_profile_approval_core"]
else:
    _core_spec = importlib.util.spec_from_file_location(
        "calibration_profile_approval_core",
        Path(__file__).with_name("calibration_profile_approval_core.py"),
    )
    if _core_spec is None or _core_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_profile_approval_core.py")
    calibration_profile_approval_core = importlib.util.module_from_spec(_core_spec)
    sys.modules["calibration_profile_approval_core"] = calibration_profile_approval_core
    _core_spec.loader.exec_module(calibration_profile_approval_core)

verify_approval = calibration_profile_approval_core.verify_approval


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
