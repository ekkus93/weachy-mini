#!/usr/bin/env python3
"""Validate and compile an RMA-072 calibration experiment plan."""

from __future__ import annotations

import argparse
import json
from datetime import UTC, datetime
from pathlib import Path

from calibration_experiment import (
    canonical_json_bytes,
    command_jsonl_bytes,
    compile_plan,
    load_plan_file,
    schedule_json_bytes,
    validate_plan,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--manifest-output", type=Path, required=True)
    parser.add_argument("--schedule-output", type=Path, required=True)
    parser.add_argument("--command-jsonl-output", type=Path, required=True)
    parser.add_argument("--created-utc")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    plan = load_plan_file(args.plan)
    summary = validate_plan(plan)
    schedule = compile_plan(plan)
    created_utc = args.created_utc or datetime.now(UTC).isoformat(timespec="microseconds").replace(
        "+00:00", "Z"
    )
    manifest = schedule.manifest(created_utc=created_utc, physical_execution=False)

    for path in (args.manifest_output, args.schedule_output, args.command_jsonl_output):
        path.parent.mkdir(parents=True, exist_ok=True)
    args.manifest_output.write_bytes(canonical_json_bytes(manifest))
    args.schedule_output.write_bytes(schedule_json_bytes(schedule))
    args.command_jsonl_output.write_bytes(command_jsonl_bytes(schedule))

    print(
        json.dumps(
            {
                **summary,
                "schedule_sha256": schedule.schedule_sha256,
                "command_jsonl_bytes": args.command_jsonl_output.stat().st_size,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
