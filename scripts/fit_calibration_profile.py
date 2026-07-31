#!/usr/bin/env python3
"""Fit an unapproved RMA-073 profile candidate and sign its manifest."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import calibration_fitting


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--dataset-root", type=Path, required=True)
    parser.add_argument("--private-key", type=Path, required=True)
    parser.add_argument("--public-key", type=Path, required=True)
    parser.add_argument("--public-key-id", required=True)
    parser.add_argument("--created-utc", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verification-output", type=Path)
    args = parser.parse_args()
    plan = calibration_fitting.load_fit_plan(args.plan)
    profile, verification = calibration_fitting.fit_profile(
        plan,
        dataset_root=args.dataset_root,
        created_utc=args.created_utc,
        private_key_path=args.private_key,
        public_key_path=args.public_key,
        public_key_id=args.public_key_id,
    )
    calibration_fitting.write_json(args.output, profile)
    if args.verification_output:
        calibration_fitting.write_json(args.verification_output, verification)
    print(json.dumps(verification, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
