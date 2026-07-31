#!/usr/bin/env python3
"""Create and independently verify an RMA-074 unit calibration approval."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from calibration_profile_approval import (
    canonical_json_bytes,
    create_approval,
    load_json_file,
    verify_approval,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--approval-id", required=True)
    parser.add_argument("--created-utc", required=True)
    parser.add_argument("--candidate-profile", type=Path, required=True)
    parser.add_argument("--candidate-public-key", type=Path, required=True)
    parser.add_argument("--compatibility", type=Path, required=True)
    parser.add_argument("--preflight-report", type=Path, required=True)
    parser.add_argument("--dataset-evidence", type=Path, required=True)
    parser.add_argument("--heldout-report", type=Path, required=True)
    parser.add_argument("--approval-private-key", type=Path, required=True)
    parser.add_argument("--approval-public-key", type=Path, required=True)
    parser.add_argument("--approval-public-key-id", required=True)
    parser.add_argument("--approver-statement", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verification-output", type=Path, required=True)
    return parser.parse_args()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def main() -> int:
    args = parse_args()
    candidate = load_json_file(args.candidate_profile)
    compatibility = load_json_file(args.compatibility)
    preflight = load_json_file(args.preflight_report)
    dataset_evidence = load_json_file(args.dataset_evidence)
    heldout = load_json_file(args.heldout_report)
    if not isinstance(candidate, dict):
        raise SystemExit("candidate profile must be an object")
    if not isinstance(compatibility, dict):
        raise SystemExit("compatibility must be an object")
    if not isinstance(preflight, dict):
        raise SystemExit("preflight report must be an object")
    if not isinstance(dataset_evidence, list):
        raise SystemExit("dataset evidence must be an array")
    if not isinstance(heldout, dict):
        raise SystemExit("heldout report must be an object")
    document = create_approval(
        approval_id=args.approval_id,
        created_utc=args.created_utc,
        candidate_profile=candidate,
        candidate_public_key_path=args.candidate_public_key,
        expected_compatibility=compatibility,
        preflight_report=preflight,
        dataset_evidence=dataset_evidence,
        heldout_report=heldout,
        approval_private_key_path=args.approval_private_key,
        approval_public_key_path=args.approval_public_key,
        approval_public_key_id=args.approval_public_key_id,
        approver_statement=args.approver_statement,
    )
    write_json(args.output, document)
    hardware_hash = document["unit"]["hardware_id_sha256"]
    summary = verify_approval(
        document,
        public_key_path=args.approval_public_key,
        expected_compatibility=compatibility,
        expected_hardware_id_sha256=hardware_hash,
    )
    write_json(args.verification_output, summary)
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
