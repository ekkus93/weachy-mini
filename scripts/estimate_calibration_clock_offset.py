#!/usr/bin/env python3
"""Estimate a conservative clock offset from paired synchronization events."""

from __future__ import annotations

import argparse
import csv
import json
import statistics
from pathlib import Path


def estimate_alignment(
    rows: list[tuple[int, int]],
    *,
    from_clock_id: str,
    to_clock_id: str,
    maximum_uncertainty_ns: int,
    allow_unsynchronized: bool,
) -> dict[str, object]:
    if from_clock_id == to_clock_id:
        raise ValueError("source and primary clock IDs must differ")
    if maximum_uncertainty_ns < 0 or maximum_uncertainty_ns > 10**18:
        raise ValueError("maximum uncertainty must be between 0 and 10^18 ns")
    if len(rows) < 3:
        raise ValueError("at least three paired synchronization events are required")
    for target, source in rows:
        if target < 0 or source < 0 or target > 10**19 or source > 10**19:
            raise ValueError("paired timestamps must be between 0 and 10^19 ns")
    offsets = [target - source for target, source in rows]
    median_offset = round(statistics.median(offsets))
    uncertainty = max(abs(offset - median_offset) for offset in offsets)
    if abs(median_offset) > 10**18 or uncertainty > 10**18:
        raise ValueError("estimated alignment exceeds the v1 import bounds")
    synchronized = uncertainty <= maximum_uncertainty_ns
    if not synchronized and not allow_unsynchronized:
        raise ValueError(
            f"estimated uncertainty {uncertainty} ns exceeds maximum {maximum_uncertainty_ns} ns"
        )
    return {
        "from_clock_id": from_clock_id,
        "to_clock_id": to_clock_id,
        "offset_ns": median_offset,
        "uncertainty_ns": max(uncertainty, 1 if not synchronized else 0),
        "method": "paired_events_median" if synchronized else "unsynchronized",
        "sample_count": len(rows),
        "synchronized": synchronized,
    }


def read_pairs(path: Path, maximum_rows: int) -> list[tuple[int, int]]:
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("synchronization CSV exceeds 16 MiB")
    rows: list[tuple[int, int]] = []
    with path.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        expected = ["primary_timestamp_ns", "source_timestamp_ns"]
        if reader.fieldnames != expected:
            raise ValueError(f"synchronization CSV columns must be exactly {expected}")
        for row_index, row in enumerate(reader, start=2):
            if len(rows) >= maximum_rows:
                raise ValueError("synchronization CSV contains too many rows")
            try:
                primary = int(row["primary_timestamp_ns"])
                source = int(row["source_timestamp_ns"])
            except (TypeError, ValueError) as exc:
                raise ValueError(f"row {row_index} contains a non-integer timestamp") from exc
            if primary < 0 or source < 0:
                raise ValueError(f"row {row_index} contains a negative timestamp")
            rows.append((primary, source))
    return rows


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pairs-csv", type=Path, required=True)
    parser.add_argument("--from-clock-id", required=True)
    parser.add_argument("--to-clock-id", required=True)
    parser.add_argument("--maximum-uncertainty-ns", type=int, required=True)
    parser.add_argument("--maximum-rows", type=int, default=1_000_000)
    parser.add_argument("--allow-unsynchronized", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.maximum_rows <= 0:
        raise ValueError("maximum rows must be positive")
    rows = read_pairs(args.pairs_csv, args.maximum_rows)
    alignment = estimate_alignment(
        rows,
        from_clock_id=args.from_clock_id,
        to_clock_id=args.to_clock_id,
        maximum_uncertainty_ns=args.maximum_uncertainty_ns,
        allow_unsynchronized=args.allow_unsynchronized,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(alignment, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(alignment, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
