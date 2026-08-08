#!/usr/bin/env python3
"""Prepare one physical Android device for an RMA-133 V6 reproducibility run."""

from __future__ import annotations

import json
import math
import os
import shlex
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = Path(
    os.environ.get(
        "RMA133_REPRO_PRECONDITION_OUTPUT",
        ROOT / "build/rma133/reproducibility/precondition.json",
    )
)
MAX_START_TEMP_C = float(os.environ.get("RMA133_REPRO_MAX_START_TEMP_C", "32.0"))
MAX_STABLE_SPAN_C = float(os.environ.get("RMA133_REPRO_MAX_STABLE_SPAN_C", "0.3"))
STABLE_WINDOW_SECONDS = float(os.environ.get("RMA133_REPRO_STABLE_WINDOW_SECONDS", "60"))
SAMPLE_INTERVAL_SECONDS = float(os.environ.get("RMA133_REPRO_SAMPLE_INTERVAL_SECONDS", "10"))
TIMEOUT_SECONDS = float(os.environ.get("RMA133_REPRO_TIMEOUT_SECONDS", "3600"))

TEMPERATURE_PATHS = (
    "/sys/class/power_supply/battery/temp",
    "/sys/class/power_supply/battery/batt_temp",
    "/sys/class/power_supply/bms/temp",
)


def run(args: list[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        check=check,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def adb(serial: str, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return run(["adb", "-s", serial, *args], check=check)


def shell(serial: str, argv: list[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    return adb(serial, "shell", shlex.join(argv), check=check)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def normalize_temperature(raw: float) -> float:
    if raw <= 0.0:
        return -1.0
    if raw >= 10000.0:
        return raw / 1000.0
    if raw >= 100.0:
        return raw / 10.0
    return raw


def read_battery_temperature_c(serial: str) -> tuple[float, str]:
    for path in TEMPERATURE_PATHS:
        result = shell(serial, ["cat", path], check=False)
        if result.returncode != 0:
            continue
        try:
            value = normalize_temperature(float(result.stdout.strip()))
        except ValueError:
            continue
        if value > 0.0:
            return value, path
    raise RuntimeError("battery temperature telemetry is unavailable")


def resolve_device() -> tuple[str, dict[str, Any]]:
    serial = os.environ.get("RMA133_DEVICE_SERIAL", "")
    if not serial:
        lines = run(["adb", "devices"]).stdout.splitlines()[1:]
        devices = [
            line.split()[0]
            for line in lines
            if len(line.split()) >= 2 and line.split()[1] == "device"
        ]
        if len(devices) != 1:
            raise RuntimeError(
                f"RMA-133 reproducibility requires exactly one authorized device; found {len(devices)}"
            )
        serial = devices[0]

    if adb(serial, "get-state").stdout.strip() != "device":
        raise RuntimeError("Android device is not ready")

    abi = shell(serial, ["getprop", "ro.product.cpu.abi"]).stdout.strip()
    api_raw = shell(serial, ["getprop", "ro.build.version.sdk"]).stdout.strip()
    model = shell(serial, ["getprop", "ro.product.model"]).stdout.strip()
    qemu = shell(serial, ["getprop", "ro.kernel.qemu"]).stdout.strip()
    if abi != "arm64-v8a" or not api_raw.isdigit() or int(api_raw) < 26 or qemu == "1":
        raise RuntimeError(
            f"RMA-133 reproducibility requires physical ARM64 API26+; "
            f"ABI={abi} API={api_raw} qemu={qemu}"
        )
    return serial, {
        "serial": serial,
        "model": model,
        "abi": abi,
        "api": int(api_raw),
        "qemu": qemu,
    }


def benchmark_pids(serial: str) -> list[int]:
    script = r"""
for p in /proc/[0-9]*; do
  [ -r "$p/cmdline" ] || continue
  cmd="$(tr '\000' ' ' < "$p/cmdline" 2>/dev/null || true)"
  case "$cmd" in
    *rma133_benchmark_v6*) printf '%s\n' "${p#/proc/}" ;;
  esac
done
"""
    result = shell(serial, ["sh", "-c", script], check=False)
    if result.returncode != 0:
        raise RuntimeError(f"could not inspect stale benchmark processes: {result.stderr.strip()}")
    pids: list[int] = []
    for line in result.stdout.splitlines():
        text = line.strip()
        if text.isdigit():
            pids.append(int(text))
    return sorted(set(pids))


def terminate_stale_benchmark(serial: str) -> dict[str, Any]:
    evidence: dict[str, Any] = {
        "found_pids": [],
        "term_pids": [],
        "kill_pids": [],
        "remaining_pids": [],
    }
    initial = benchmark_pids(serial)
    evidence["found_pids"] = initial
    if not initial:
        return evidence

    shell(serial, ["kill", "-TERM", *[str(pid) for pid in initial]], check=False)
    evidence["term_pids"] = initial
    time.sleep(2.0)
    remaining = benchmark_pids(serial)
    if remaining:
        shell(serial, ["kill", "-KILL", *[str(pid) for pid in remaining]], check=False)
        evidence["kill_pids"] = remaining
        time.sleep(1.0)
        remaining = benchmark_pids(serial)
    evidence["remaining_pids"] = remaining
    if remaining:
        raise RuntimeError(f"stale RMA-133 benchmark process could not be stopped: {remaining}")
    return evidence


def stable_window(samples: list[dict[str, Any]]) -> list[dict[str, Any]] | None:
    required_samples = math.ceil(STABLE_WINDOW_SECONDS / SAMPLE_INTERVAL_SECONDS) + 1
    if len(samples) < required_samples:
        return None
    window = samples[-required_samples:]
    elapsed = float(window[-1]["elapsed_seconds"]) - float(window[0]["elapsed_seconds"])
    if elapsed < STABLE_WINDOW_SECONDS:
        return None
    temperatures = [float(sample["temperature_c"]) for sample in window]
    if any(value > MAX_START_TEMP_C for value in temperatures):
        return None
    if max(temperatures) - min(temperatures) > MAX_STABLE_SPAN_C:
        return None
    return window


def write_evidence(data: dict[str, Any]) -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    if shutil.which("adb") is None:
        raise SystemExit("RMA-133 reproducibility requires adb")
    if MAX_START_TEMP_C <= 0.0 or MAX_STABLE_SPAN_C < 0.0:
        raise SystemExit("RMA-133 reproducibility temperature limits are invalid")
    if STABLE_WINDOW_SECONDS <= 0.0 or SAMPLE_INTERVAL_SECONDS <= 0.0 or TIMEOUT_SECONDS <= 0.0:
        raise SystemExit("RMA-133 reproducibility timing limits are invalid")

    evidence: dict[str, Any] = {
        "schema_version": 1,
        "protocol": "rma133-v6-cool-start-reproducibility-v1",
        "status": "initializing",
        "started_at_utc": utc_now(),
        "limits": {
            "maximum_start_temperature_c": MAX_START_TEMP_C,
            "maximum_stable_span_c": MAX_STABLE_SPAN_C,
            "stable_window_seconds": STABLE_WINDOW_SECONDS,
            "sample_interval_seconds": SAMPLE_INTERVAL_SECONDS,
            "timeout_seconds": TIMEOUT_SECONDS,
        },
        "temperature_paths": list(TEMPERATURE_PATHS),
        "samples": [],
    }

    try:
        serial, device = resolve_device()
        evidence["device"] = device
        evidence["stale_benchmark_cleanup"] = terminate_stale_benchmark(serial)
        shell(
            serial,
            ["sh", "-c", "rm -rf /data/local/tmp/reachy-rma133-v6-*"],
            check=False,
        )
        evidence["battery_snapshot_before"] = shell(
            serial, ["dumpsys", "battery"], check=False
        ).stdout
        evidence["thermal_snapshot_before"] = shell(
            serial, ["dumpsys", "thermalservice"], check=False
        ).stdout

        started = time.monotonic()
        while True:
            elapsed = time.monotonic() - started
            if elapsed > TIMEOUT_SECONDS:
                evidence["status"] = "invalid_environment"
                evidence["completed_at_utc"] = utc_now()
                evidence["reason"] = "cool/stable precondition was not reached before timeout"
                write_evidence(evidence)
                return 2

            temperature_c, path = read_battery_temperature_c(serial)
            sample = {
                "timestamp_utc": utc_now(),
                "elapsed_seconds": round(elapsed, 3),
                "temperature_c": temperature_c,
                "source_path": path,
            }
            evidence["samples"].append(sample)
            print(
                f"RMA-133 reproducibility precondition: "
                f"elapsed={elapsed:.0f}s battery={temperature_c:.1f}C source={path}",
                flush=True,
            )

            window = stable_window(evidence["samples"])
            if window is not None:
                temperatures = [float(item["temperature_c"]) for item in window]
                evidence["status"] = "passed"
                evidence["completed_at_utc"] = utc_now()
                evidence["accepted_window"] = {
                    "sample_count": len(window),
                    "start_elapsed_seconds": window[0]["elapsed_seconds"],
                    "end_elapsed_seconds": window[-1]["elapsed_seconds"],
                    "minimum_temperature_c": min(temperatures),
                    "maximum_temperature_c": max(temperatures),
                    "span_c": max(temperatures) - min(temperatures),
                    "final_temperature_c": temperatures[-1],
                }
                write_evidence(evidence)
                print("RMA-133 reproducibility cool/stable precondition passed.", flush=True)
                return 0

            time.sleep(SAMPLE_INTERVAL_SECONDS)
    except Exception as exc:  # noqa: BLE001 - evidence must survive an environmental failure
        evidence["status"] = "invalid_environment"
        evidence["completed_at_utc"] = utc_now()
        evidence["reason"] = str(exc)
        write_evidence(evidence)
        print(f"RMA-133 reproducibility precondition failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
