#!/usr/bin/env python3
"""Validate a bounded RMA-070 calibration dataset import."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from calibration_data import ImportLimits, load_json_file, validate_dataset


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path)
    parser.add_argument("--maximum-file-bytes", type=int, default=ImportLimits.maximum_file_bytes)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    limits = ImportLimits(maximum_file_bytes=args.maximum_file_bytes)
    dataset = load_json_file(args.input, limits=limits)
    summary = validate_dataset(dataset, limits=limits)
    encoded = json.dumps(summary, indent=2, sort_keys=True) + "\n"
    if args.summary_output is not None:
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        args.summary_output.write_text(encoded, encoding="utf-8", newline="\n")
    print(encoded, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
