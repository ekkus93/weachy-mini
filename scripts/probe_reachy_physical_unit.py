#!/usr/bin/env python3
"""Read-only RMA-074 Reachy Mini hardware discovery and telemetry probe."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

CONTRACT_ID = "rma074_physical_preflight_v1"
DEFAULT_PORT = 8000
DEFAULT_TIMEOUT_SECONDS = 3.0
DEFAULT_OBSERVATION_SECONDS = 2.0
DEFAULT_SAMPLE_HZ = 10.0
MAX_RESPONSE_BYTES = 2 * 1024 * 1024


class PreflightError(RuntimeError):
    """Raised when no admissible physical Reachy Mini is available."""


@dataclass(frozen=True)
class Candidate:
    label: str
    host: str
    port: int


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


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def _finite_number(value: Any, path: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise PreflightError(f"{path} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise PreflightError(f"{path} must be finite")
    return number


def _finite_vector(value: Any, expected: int, path: str) -> list[float]:
    if not isinstance(value, list) or len(value) != expected:
        raise PreflightError(f"{path} must contain exactly {expected} values")
    return [_finite_number(entry, f"{path}[{index}]") for index, entry in enumerate(value)]


def _finite_pose(value: Any, path: str) -> list[float]:
    if isinstance(value, dict):
        matrix = value.get("pose_matrix") or value.get("matrix")
        if matrix is not None:
            value = matrix
    if isinstance(value, list) and len(value) == 16:
        return [_finite_number(entry, f"{path}[{index}]") for index, entry in enumerate(value)]
    if isinstance(value, list) and len(value) == 4 and all(isinstance(row, list) for row in value):
        flattened = [entry for row in value for entry in row]
        return _finite_vector(flattened, 16, path)
    raise PreflightError(f"{path} must be a 4x4 or flat 16-value pose matrix")


def _request_json(candidate: Candidate, path: str, timeout_seconds: float) -> Any:
    url = f"http://{candidate.host}:{candidate.port}{path}"
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/json",
            "User-Agent": "weachy-mini-rma074-read-only-preflight/1",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            if response.status != 200:
                raise PreflightError(
                    f"{candidate.label} returned HTTP {response.status} for {path}"
                )
            raw = response.read(MAX_RESPONSE_BYTES + 1)
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        raise PreflightError(f"{candidate.label} is unreachable at {path}: {exc}") from exc
    if len(raw) > MAX_RESPONSE_BYTES:
        raise PreflightError(f"{candidate.label} response exceeds {MAX_RESPONSE_BYTES} bytes")
    try:
        return json.loads(
            raw.decode("utf-8", errors="strict"),
            parse_constant=lambda token: (_ for _ in ()).throw(
                PreflightError(f"{candidate.label} returned non-finite JSON constant {token}")
            ),
            object_pairs_hook=_reject_duplicate_pairs,
        )
    except UnicodeDecodeError as exc:
        raise PreflightError(f"{candidate.label} returned non-UTF-8 JSON") from exc
    except json.JSONDecodeError as exc:
        raise PreflightError(
            f"{candidate.label} returned invalid JSON at line {exc.lineno}, column {exc.colno}"
        ) from exc


def _reject_duplicate_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise PreflightError(f"response contains duplicate JSON key {key!r}")
        result[key] = value
    return result


def validate_daemon_status(status: Any) -> dict[str, Any]:
    if not isinstance(status, dict):
        raise PreflightError("daemon status must be an object")
    if status.get("simulation_enabled") not in (False, None):
        raise PreflightError("daemon is using MuJoCo simulation, not a physical robot")
    if status.get("mockup_sim_enabled") not in (False, None):
        raise PreflightError("daemon is using mock simulation, not a physical robot")
    if status.get("state") != "running":
        raise PreflightError(f"daemon state must be 'running', found {status.get('state')!r}")
    backend = status.get("backend_status")
    if not isinstance(backend, dict):
        raise PreflightError("daemon backend_status is missing")
    if backend.get("ready") is not True:
        raise PreflightError("physical robot backend is not ready")
    if backend.get("error") not in (None, ""):
        raise PreflightError(f"physical robot backend reports error: {backend.get('error')}")
    if "last_alive" not in backend or backend["last_alive"] is None:
        raise PreflightError("physical robot backend has no liveness timestamp")
    _finite_number(backend["last_alive"], "backend_status.last_alive")
    robot_name = status.get("robot_name")
    if not isinstance(robot_name, str) or not robot_name:
        raise PreflightError("daemon robot_name is missing")
    version = status.get("version")
    if version is not None and not isinstance(version, str):
        raise PreflightError("daemon version must be a string or null")
    return {
        "robot_name": robot_name,
        "wireless_version": status.get("wireless_version") is True,
        "daemon_version": version,
        "backend_control_mode": backend.get("motor_control_mode"),
        "backend_ready": True,
        "backend_control_loop_stats_present": isinstance(backend.get("control_loop_stats"), dict),
    }


def validate_hardware_id(response: Any) -> str:
    if not isinstance(response, dict):
        raise PreflightError("hardware-id response must be an object")
    hardware_id = response.get("hardware_id")
    if not isinstance(hardware_id, str) or not hardware_id.strip():
        raise PreflightError("daemon did not return a physical hardware identifier")
    if len(hardware_id) > 4096:
        raise PreflightError("hardware identifier exceeds import bounds")
    return hashlib.sha256(hardware_id.encode("utf-8")).hexdigest()


def validate_full_state(state: Any) -> dict[str, Any]:
    if not isinstance(state, dict):
        raise PreflightError("full state must be an object")
    head_joints = _finite_vector(state.get("head_joints"), 7, "state.head_joints")
    antennas = _finite_vector(state.get("antennas_position"), 2, "state.antennas_position")
    body_yaw = _finite_number(state.get("body_yaw"), "state.body_yaw")
    pose = _finite_pose(state.get("head_pose"), "state.head_pose")
    control_mode = state.get("control_mode")
    if not isinstance(control_mode, str) or not control_mode:
        raise PreflightError("state.control_mode is missing")
    return {
        "head_joint_count": len(head_joints),
        "antenna_joint_count": len(antennas),
        "body_yaw_rad": body_yaw,
        "head_pose_element_count": len(pose),
        "control_mode": control_mode,
        "head_joints": head_joints,
        "antennas": antennas,
        "head_pose": pose,
    }


def candidate_list(explicit_host: str | None, port: int) -> list[Candidate]:
    candidates: list[Candidate] = []
    if explicit_host:
        candidates.append(Candidate("configured_host", explicit_host, port))
    environment_host = os.environ.get("REACHY_MINI_HOST", "").strip()
    if environment_host and environment_host != explicit_host:
        candidates.append(Candidate("environment_host", environment_host, port))
    candidates.extend(
        [
            Candidate("localhost", "127.0.0.1", port),
            Candidate("mdns_default", "reachy-mini.local", port),
        ]
    )
    unique: list[Candidate] = []
    seen: set[tuple[str, int]] = set()
    for candidate in candidates:
        key = (candidate.host, candidate.port)
        if key not in seen:
            unique.append(candidate)
            seen.add(key)
    return unique


def probe_candidate(
    candidate: Candidate,
    *,
    timeout_seconds: float,
    observation_seconds: float,
    sample_hz: float,
) -> dict[str, Any]:
    status = validate_daemon_status(_request_json(candidate, "/api/daemon/status", timeout_seconds))
    hardware_id_sha256 = validate_hardware_id(
        _request_json(candidate, "/api/daemon/hardware-id", timeout_seconds)
    )
    query = urllib.parse.urlencode(
        {
            "with_control_mode": "true",
            "with_head_pose": "true",
            "with_target_head_pose": "false",
            "with_head_joints": "true",
            "with_target_head_joints": "false",
            "with_body_yaw": "true",
            "with_target_body_yaw": "false",
            "with_antenna_positions": "true",
            "with_target_antenna_positions": "false",
            "with_passive_joints": "true",
            "with_doa": "false",
            "use_pose_matrix": "true",
        }
    )
    state_path = f"/api/state/full?{query}"
    sample_count = max(3, math.ceil(observation_seconds * sample_hz))
    period = 1.0 / sample_hz
    samples: list[dict[str, Any]] = []
    start = time.monotonic()
    for index in range(sample_count):
        if index:
            deadline = start + index * period
            remaining = deadline - time.monotonic()
            if remaining > 0:
                time.sleep(remaining)
        samples.append(validate_full_state(_request_json(candidate, state_path, timeout_seconds)))
    control_modes = sorted({sample["control_mode"] for sample in samples})
    joint_ranges = []
    for joint_index in range(7):
        values = [sample["head_joints"][joint_index] for sample in samples]
        joint_ranges.append(max(values) - min(values))
    antenna_ranges = []
    for joint_index in range(2):
        values = [sample["antennas"][joint_index] for sample in samples]
        antenna_ranges.append(max(values) - min(values))
    return {
        "status": "ready",
        "candidate_label": candidate.label,
        "port": candidate.port,
        "hardware_id_sha256": hardware_id_sha256,
        "robot": status,
        "observation": {
            "duration_seconds": observation_seconds,
            "requested_sample_hz": sample_hz,
            "sample_count": len(samples),
            "control_modes": control_modes,
            "maximum_head_joint_drift_rad": max(joint_ranges),
            "maximum_antenna_joint_drift_rad": max(antenna_ranges),
            "first_sample": {
                key: value
                for key, value in samples[0].items()
                if key not in {"head_joints", "antennas", "head_pose"}
            },
        },
        "telemetry_capabilities": {
            "joint_positions": True,
            "head_pose": True,
            "body_yaw": True,
            "imu": status["wireless_version"],
            "servo_current": False,
            "bus_voltage": False,
            "motor_temperature": False,
            "external_pose": False,
            "force_torque": False,
            "contact": False,
        },
        "privacy": {
            "raw_hardware_id_retained": False,
            "network_host_retained": False,
        },
        "motion_commands_issued": 0,
        "torque_commands_issued": 0,
    }


def build_report(
    *,
    candidates: list[Candidate],
    timeout_seconds: float,
    observation_seconds: float,
    sample_hz: float,
) -> tuple[dict[str, Any], bool]:
    attempts: list[dict[str, Any]] = []
    for candidate in candidates:
        try:
            result = probe_candidate(
                candidate,
                timeout_seconds=timeout_seconds,
                observation_seconds=observation_seconds,
                sample_hz=sample_hz,
            )
        except Exception as exc:
            attempts.append(
                {
                    "candidate_label": candidate.label,
                    "port": candidate.port,
                    "status": "unavailable",
                    "error_type": type(exc).__name__,
                    "error": str(exc)[:4096],
                }
            )
            continue
        report = {
            "contract_id": CONTRACT_ID,
            "created_utc": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
            "result": "physical_unit_ready",
            "attempts": attempts,
            "physical_unit": result,
        }
        report["report_sha256"] = hashlib.sha256(canonical_json_bytes(report)).hexdigest()
        return report, True
    report = {
        "contract_id": CONTRACT_ID,
        "created_utc": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "result": "physical_unit_unavailable",
        "attempts": attempts,
        "physical_unit": None,
    }
    report["report_sha256"] = hashlib.sha256(canonical_json_bytes(report)).hexdigest()
    return report, False


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--host")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--timeout-seconds", type=float, default=DEFAULT_TIMEOUT_SECONDS)
    parser.add_argument("--observation-seconds", type=float, default=DEFAULT_OBSERVATION_SECONDS)
    parser.add_argument("--sample-hz", type=float, default=DEFAULT_SAMPLE_HZ)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not 1 <= args.port <= 65535:
        raise SystemExit("--port must be between 1 and 65535")
    if not 0.1 <= args.timeout_seconds <= 60.0:
        raise SystemExit("--timeout-seconds must be between 0.1 and 60")
    if not 0.2 <= args.observation_seconds <= 60.0:
        raise SystemExit("--observation-seconds must be between 0.2 and 60")
    if not 1.0 <= args.sample_hz <= 50.0:
        raise SystemExit("--sample-hz must be between 1 and 50")
    report, ready = build_report(
        candidates=candidate_list(args.host, args.port),
        timeout_seconds=args.timeout_seconds,
        observation_seconds=args.observation_seconds,
        sample_hz=args.sample_hz,
    )
    write_json(args.output, report)
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if ready else 2


if __name__ == "__main__":
    sys.exit(main())
