#!/usr/bin/env python3
"""Verify an RMA-073 signed profile and exact runtime compatibility."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import calibration_fitting


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--public-key", type=Path, required=True)
    parser.add_argument("--expected-compatibility", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path)
    args = parser.parse_args()
    profile = calibration_fitting.load_json_file(args.profile)
    compatibility = calibration_fitting.load_json_file(args.expected_compatibility)
    summary = calibration_fitting.verify_profile(
        profile,
        public_key_path=args.public_key,
        expected_compatibility=compatibility,
    )
    if args.summary_output:
        calibration_fitting.write_json(args.summary_output, summary)
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
