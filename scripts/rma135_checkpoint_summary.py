#!/usr/bin/env python3
"""Print only allowlisted RMA-135 checkpoint counters/statuses to CI on failure."""

import json
import re
import sys
from pathlib import Path

FIELDS = {
    "governor_recovery_exhausted": "samples last_mode last_reasons last_real_physics_state",
    "post_recovery_generation_completed": (
        "attempt governed_status provider_status text_events governor_mode governor_reasons "
        "last_real_physics_state worker_steps worker_step_delta worker_deadline_misses "
        "worker_deadline_miss_delta worker_accumulated_lag_us worker_last_step_duration_us"
    ),
    "post_recovery_preflight_wait_completed": "attempt samples ready mode",
    "post_recovery_generation_exhausted": (
        "attempts first_refusal last_status worker_steps_during_retry_window "
        "worker_deadline_misses_during_retry_window"
    ),
}
NAME = re.compile(r"rma135-resource-governor-checkpoint-([0-9]{3,5})\.json")
PAIR = re.compile(r"(?<!\S)([a-z_]+)=([A-Za-z0-9_.+-]+(?:, [A-Za-z0-9_.+-]+)*)")
REASONS = {
    "None",
    "DeviceProfileLimit",
    "ThermalSignalUnavailable",
    "MemorySignalUnavailable",
    "PhysicsSignalUnavailable",
    "ThermalLight",
    "ThermalModerate",
    "ThermalSevereOrWorse",
    "MemoryPressure",
    "MemoryCritical",
    "AndroidLowMemory",
    "PhysicsAtRisk",
    "PhysicsBudgetExceeded",
    "RecoveryHold",
    "RecentOutOfMemory",
    "ProfileIncompatible",
    "PriorityDegradationPolicy",
    "none",
}


def summarize(directory):
    directory = Path(directory)
    if directory.is_symlink() or not directory.is_dir():
        return []
    lines = []
    for path in directory.glob("rma135-resource-governor-checkpoint-*.json"):
        match = NAME.fullmatch(path.name)
        if not match or path.is_symlink() or not path.is_file():
            continue
        try:
            if path.stat().st_size > 16384:
                continue
            obj = json.loads(path.read_text(encoding="utf-8"))
            seq, stage = obj.get("sequence"), obj.get("stage")
            if (
                type(seq) is not int
                or seq != int(match.group(1))
                or obj.get("schema_version") != 1
                or stage not in FIELDS
                or not isinstance(obj.get("detail"), str)
            ):
                continue
            allowed = set(FIELDS[stage].split())
            values = {}
            for key, value in PAIR.findall(obj["detail"][:1024]):
                if (
                    key in allowed
                    and key not in values
                    and (
                        key not in ("governor_reasons", "last_reasons")
                        or all(reason in REASONS for reason in value.split(", "))
                    )
                ):
                    values[key] = value
            suffix = " ".join(f"{key}={values[key]}" for key in sorted(values))
            lines.append((seq, f"RMA-135 safe checkpoint sequence={seq} stage={stage} {suffix}"))
        except (OSError, UnicodeError, ValueError, TypeError):
            continue
    return [line for _, line in sorted(lines)[-40:]]


if __name__ == "__main__":
    for line in summarize(sys.argv[1] if len(sys.argv) == 2 else "/nonexistent"):
        print(line)
