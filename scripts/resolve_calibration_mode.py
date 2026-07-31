#!/usr/bin/env python3
"""Resolve the user-facing calibrated/uncalibrated mode fail closed."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from calibration_profile_approval import (
    canonical_json_bytes,
    load_json_file,
    resolve_calibration_label,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--approval", type=Path)
    parser.add_argument("--approval-public-key", type=Path)
    parser.add_argument("--compatibility", type=Path, required=True)
    parser.add_argument("--connected-hardware-id-sha256")
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    compatibility = load_json_file(args.compatibility)
    if not isinstance(compatibility, dict):
        raise SystemExit("compatibility must be an object")
    approval = load_json_file(args.approval) if args.approval else None
    result = resolve_calibration_label(
        approval,
        public_key_path=args.approval_public_key,
        expected_compatibility=compatibility,
        connected_hardware_id_sha256=args.connected_hardware_id_sha256,
    ).to_document()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(canonical_json_bytes(result))
    print(json.dumps(result, sort_keys=True))
    return 0 if result["calibrated"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
