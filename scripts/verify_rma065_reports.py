#!/usr/bin/env python3
"""Fail-closed verification for RMA-065 audit and validation reports."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any


class Rma065ReportError(RuntimeError):
    """Raised when RMA-065 evidence is missing or violates its contract."""


def _read_object(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise Rma065ReportError(f"cannot read JSON report {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise Rma065ReportError(f"JSON report root must be an object: {path}")
    return value


def _object(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise Rma065ReportError(f"{label} must be an object")
    return value


def _finite_number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise Rma065ReportError(f"{label} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise Rma065ReportError(f"{label} must be finite")
    return result


def _zero_warnings(record: dict[str, Any], label: str) -> None:
    if record.get("warning_count") != 0:
        raise Rma065ReportError(f"{label} produced warnings: {record}")


def verify_reports(
    audit_path: Path,
    validation_path: Path,
    profile_path: Path,
    expected_neutral_steps: int,
) -> dict[str, Any]:
    audit = _read_object(audit_path)
    validation = _read_object(validation_path)
    profile = _read_object(profile_path)

    if audit.get("contract") != "rma065_enhanced_collision_audit_v1":
        raise Rma065ReportError(f"unexpected audit contract: {audit.get('contract')!r}")
    neutral_audit = _object(audit.get("neutral_audit"), "audit.neutral_audit")
    if neutral_audit.get("steps") != expected_neutral_steps:
        raise Rma065ReportError(f"audit step count mismatch: {neutral_audit}")
    _zero_warnings(neutral_audit, "audit neutral run")
    if neutral_audit.get("finite_qpos") is not True or neutral_audit.get("finite_qvel") is not True:
        raise Rma065ReportError(f"audit neutral state is non-finite: {neutral_audit}")
    if neutral_audit.get("maximum_contact_count") != 0:
        raise Rma065ReportError(f"audit neutral run produced contacts: {neutral_audit}")
    if (
        _finite_number(
            neutral_audit.get("maximum_penetration_metres"),
            "audit.neutral_audit.maximum_penetration_metres",
        )
        != 0.0
    ):
        raise Rma065ReportError(f"audit neutral run produced penetration: {neutral_audit}")

    inventory = _object(audit.get("compiled_inventory"), "audit.compiled_inventory")
    if inventory.get("collision_geom_count", 0) < 25:
        raise Rma065ReportError(f"insufficient active collision geometry: {inventory}")
    if inventory.get("collision_body_count", 0) < 17:
        raise Rma065ReportError(f"insufficient collision body coverage: {inventory}")
    if inventory.get("limited_joint_count") != 9:
        raise Rma065ReportError(f"hard-stop inventory mismatch: {inventory}")

    if validation.get("contract") != "rma065_collision_hard_stop_validation_v1":
        raise Rma065ReportError(f"unexpected validation contract: {validation.get('contract')!r}")
    if validation.get("status") != "ok":
        raise Rma065ReportError(f"validation status is not ok: {validation}")

    acceptance = _object(validation.get("acceptance"), "validation.acceptance")
    expected_acceptance = {
        "contact_force_and_impulse_exposed",
        "hard_stops_contain_outward_motion",
        "hosted_complexity_within_budget",
        "representative_external_contact_stable",
        "representative_internal_contact_stable",
    }
    if set(acceptance) != expected_acceptance or not all(
        acceptance[key] is True for key in expected_acceptance
    ):
        raise Rma065ReportError(f"validation acceptance is incomplete: {acceptance}")

    contact_parameters = _object(profile.get("contact_parameters"), "profile.contact_parameters")
    maximum_penetration = _finite_number(
        contact_parameters.get("maximum_penetration_metres"),
        "profile.contact_parameters.maximum_penetration_metres",
    )
    if maximum_penetration <= 0.0:
        raise Rma065ReportError("profile penetration limit must be positive")

    for key in ("source_neutral", "enhanced_neutral"):
        record = _object(validation.get(key), f"validation.{key}")
        if record.get("steps") != expected_neutral_steps:
            raise Rma065ReportError(f"{key} step count mismatch: {record}")
        _zero_warnings(record, key)
        if record.get("finite_qpos") is not True or record.get("finite_qvel") is not True:
            raise Rma065ReportError(f"{key} state is non-finite: {record}")

    for key in ("internal_contact_fixture", "external_contact_fixture"):
        record = _object(validation.get(key), f"validation.{key}")
        _zero_warnings(record, key)
        if record.get("observed_contact") is not True:
            raise Rma065ReportError(f"{key} did not observe contact: {record}")
        if record.get("finite_qpos") is not True or record.get("finite_qvel") is not True:
            raise Rma065ReportError(f"{key} state is non-finite: {record}")
        if (
            _finite_number(
                record.get("maximum_normal_force_newtons"),
                f"validation.{key}.maximum_normal_force_newtons",
            )
            <= 0.0
        ):
            raise Rma065ReportError(f"{key} did not expose contact force: {record}")
        if (
            _finite_number(
                record.get("maximum_impulse_newton_seconds"),
                f"validation.{key}.maximum_impulse_newton_seconds",
            )
            <= 0.0
        ):
            raise Rma065ReportError(f"{key} did not expose contact impulse: {record}")
        if (
            _finite_number(
                record.get("maximum_penetration_metres"),
                f"validation.{key}.maximum_penetration_metres",
            )
            > maximum_penetration
        ):
            raise Rma065ReportError(f"{key} exceeds penetration limit: {record}")

    trials = validation.get("hard_stop_trials")
    if not isinstance(trials, list) or len(trials) != 2:
        raise Rma065ReportError(f"expected two hard-stop trials: {trials}")
    expected_joints = {"yaw_body", "right_antenna"}
    seen_joints: set[str] = set()
    for index, value in enumerate(trials):
        trial = _object(value, f"validation.hard_stop_trials[{index}]")
        joint = trial.get("joint")
        if not isinstance(joint, str):
            raise Rma065ReportError(f"hard-stop trial has no joint name: {trial}")
        seen_joints.add(joint)
        _zero_warnings(trial, f"hard-stop trial {joint}")
        if trial.get("observed_limit_constraint") is not True:
            raise Rma065ReportError(f"hard-stop constraint was not reported: {trial}")
        upper = _finite_number(trial.get("upper_limit"), f"{joint}.upper_limit")
        maximum_position = _finite_number(
            trial.get("maximum_position"), f"{joint}.maximum_position"
        )
        if maximum_position > upper + 1.0e-6:
            raise Rma065ReportError(f"{joint} passed through its hard stop: {trial}")
    if seen_joints != expected_joints:
        raise Rma065ReportError(f"hard-stop trial joint set mismatch: {seen_joints}")

    return {
        "status": "ok",
        "audit_contract": audit["contract"],
        "validation_contract": validation["contract"],
        "neutral_steps": expected_neutral_steps,
        "collision_geom_count": inventory["collision_geom_count"],
        "collision_body_count": inventory["collision_body_count"],
        "limited_joint_count": inventory["limited_joint_count"],
        "hosted_p95_overhead_ratio": _finite_number(
            validation.get("hosted_p95_overhead_ratio"),
            "validation.hosted_p95_overhead_ratio",
        ),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--validation", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--neutral-steps", type=int, default=5000)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.neutral_steps <= 0:
        raise Rma065ReportError("neutral step count must be positive")
    summary = verify_reports(
        args.audit,
        args.validation,
        args.profile,
        args.neutral_steps,
    )
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
