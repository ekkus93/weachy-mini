#!/usr/bin/env python3
"""Capture Reachy telemetry and import external pose/force data into RMA-070."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import sys
from collections import OrderedDict
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, TextIO

from calibration_data import (
    DEFAULT_LIMITS,
    canonical_json_bytes,
    finalize_dataset,
    load_json_file,
    schema_descriptor,
    validate_dataset,
)

TOOL_ID = "reachy_calibration_capture"
TOOL_VERSION = "1.0.0"


def _strict_json_line(line: str, line_number: int) -> dict[str, Any]:
    def reject_constant(value: str) -> None:
        raise ValueError(f"line {line_number} contains non-finite constant {value}")

    try:
        value = json.loads(line, parse_constant=reject_constant)
    except json.JSONDecodeError as exc:
        raise ValueError(f"line {line_number} is invalid JSON: {exc.msg}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"line {line_number} must contain a JSON object")
    return value


def read_telemetry_jsonl(
    handle: TextIO,
    *,
    maximum_records: int,
    maximum_bytes: int,
) -> tuple[list[dict[str, Any]], int, str]:
    records: list[dict[str, Any]] = []
    total_bytes = 0
    digest = hashlib.sha256()
    for line_number, line in enumerate(handle, start=1):
        encoded = line.encode("utf-8")
        total_bytes += len(encoded)
        digest.update(encoded)
        if total_bytes > maximum_bytes:
            raise ValueError("telemetry JSONL exceeds maximum input bytes")
        if not line.strip():
            continue
        if len(records) >= maximum_records:
            raise ValueError("telemetry JSONL contains too many records")
        record = _strict_json_line(line, line_number)
        expected = {"stream_id", "sample_type", "clock_id", "sample"}
        optional = {"coordinate_frame", "description"}
        missing = expected - record.keys()
        unexpected = record.keys() - expected - optional
        if missing or unexpected:
            raise ValueError(
                f"line {line_number} keys invalid: missing={sorted(missing)} "
                f"unexpected={sorted(unexpected)}"
            )
        records.append(record)
    if not records:
        raise ValueError("telemetry JSONL contains no records")
    return records, total_bytes, digest.hexdigest()


def _float(row: dict[str, str], key: str, row_number: int) -> float:
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError(f"CSV row {row_number} has invalid {key}") from exc


def _integer(row: dict[str, str], key: str, row_number: int) -> int:
    try:
        return int(row[key])
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError(f"CSV row {row_number} has invalid {key}") from exc


def _read_csv(
    path: Path,
    expected_columns: list[str],
    maximum_records: int,
) -> list[dict[str, str]]:
    if path.stat().st_size > DEFAULT_LIMITS.maximum_file_bytes:
        raise ValueError(f"CSV input is too large: {path}")
    rows: list[dict[str, str]] = []
    with path.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        if reader.fieldnames != expected_columns:
            raise ValueError(
                f"CSV columns for {path.name} must be exactly {expected_columns}; "
                f"found {reader.fieldnames}"
            )
        for row in reader:
            if len(rows) >= maximum_records:
                raise ValueError(f"CSV contains too many rows: {path}")
            rows.append(dict(row))
    if not rows:
        raise ValueError(f"CSV contains no rows: {path}")
    return rows


def import_external_pose_csv(path: Path, *, stream_id: str, clock_id: str) -> dict[str, Any]:
    columns = [
        "timestamp_ns",
        "sequence",
        "frame_id",
        "child_frame_id",
        "position_x_m",
        "position_y_m",
        "position_z_m",
        "orientation_x",
        "orientation_y",
        "orientation_z",
        "orientation_w",
        "confidence",
    ]
    rows = _read_csv(path, columns, DEFAULT_LIMITS.maximum_samples_per_stream)
    samples: list[dict[str, Any]] = []
    for row_number, row in enumerate(rows, start=2):
        confidence = row["confidence"].strip()
        samples.append(
            {
                "timestamp_ns": _integer(row, "timestamp_ns", row_number),
                "sequence": _integer(row, "sequence", row_number),
                "frame_id": row["frame_id"],
                "child_frame_id": row["child_frame_id"],
                "position_m": [
                    _float(row, "position_x_m", row_number),
                    _float(row, "position_y_m", row_number),
                    _float(row, "position_z_m", row_number),
                ],
                "orientation_xyzw": [
                    _float(row, "orientation_x", row_number),
                    _float(row, "orientation_y", row_number),
                    _float(row, "orientation_z", row_number),
                    _float(row, "orientation_w", row_number),
                ],
                "confidence": None if confidence == "" else _float(row, "confidence", row_number),
            }
        )
    return {
        "stream_id": stream_id,
        "sample_type": "external_pose",
        "clock_id": clock_id,
        "coordinate_frame": samples[0]["frame_id"],
        "description": f"Imported external pose data from {path.name}",
        "samples": samples,
    }


def import_force_torque_csv(
    path: Path, *, stream_id: str, clock_id: str, coordinate_frame: str
) -> dict[str, Any]:
    columns = [
        "timestamp_ns",
        "sequence",
        "sensor_id",
        "force_x_n",
        "force_y_n",
        "force_z_n",
        "torque_x_nm",
        "torque_y_nm",
        "torque_z_nm",
    ]
    rows = _read_csv(path, columns, DEFAULT_LIMITS.maximum_samples_per_stream)
    samples: list[dict[str, Any]] = []
    for row_number, row in enumerate(rows, start=2):
        samples.append(
            {
                "timestamp_ns": _integer(row, "timestamp_ns", row_number),
                "sequence": _integer(row, "sequence", row_number),
                "sensor_id": row["sensor_id"],
                "force_n": [
                    _float(row, "force_x_n", row_number),
                    _float(row, "force_y_n", row_number),
                    _float(row, "force_z_n", row_number),
                ],
                "torque_nm": [
                    _float(row, "torque_x_nm", row_number),
                    _float(row, "torque_y_nm", row_number),
                    _float(row, "torque_z_nm", row_number),
                ],
            }
        )
    return {
        "stream_id": stream_id,
        "sample_type": "force_torque",
        "clock_id": clock_id,
        "coordinate_frame": coordinate_frame,
        "description": f"Imported force/torque data from {path.name}",
        "samples": samples,
    }


def group_telemetry_records(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: OrderedDict[str, dict[str, Any]] = OrderedDict()
    for record in records:
        stream_id = record["stream_id"]
        if not isinstance(stream_id, str):
            raise ValueError("telemetry stream_id must be a string")
        descriptor = {
            "stream_id": stream_id,
            "sample_type": record["sample_type"],
            "clock_id": record["clock_id"],
            "coordinate_frame": record.get("coordinate_frame"),
            "description": record.get("description"),
        }
        existing = grouped.get(stream_id)
        if existing is None:
            existing = {key: value for key, value in descriptor.items() if value is not None}
            existing["samples"] = []
            grouped[stream_id] = existing
        else:
            comparable = {key: value for key, value in existing.items() if key != "samples"}
            expected = {key: value for key, value in descriptor.items() if value is not None}
            if comparable != expected:
                raise ValueError(
                    f"telemetry stream {stream_id!r} changes metadata within one capture"
                )
        sample = record["sample"]
        if not isinstance(sample, dict):
            raise ValueError(f"telemetry stream {stream_id!r} sample must be an object")
        existing["samples"].append(sample)
    return list(grouped.values())


def _source_file(path: Path, media_type: str) -> dict[str, Any]:
    data = path.read_bytes()
    return {
        "name": path.name,
        "sha256": hashlib.sha256(data).hexdigest(),
        "media_type": media_type,
        "size_bytes": len(data),
    }


def derive_sync_state(alignments: list[dict[str, Any]]) -> str:
    synchronized = sum(1 for alignment in alignments if alignment.get("synchronized") is True)
    unsynchronized = sum(1 for alignment in alignments if alignment.get("synchronized") is False)
    if synchronized and unsynchronized:
        return "partially_synchronized"
    if unsynchronized:
        return "unsynchronized"
    return "synchronized"


def build_dataset(
    *,
    dataset_id: str,
    created_utc: str,
    robot: dict[str, Any],
    environment: dict[str, Any],
    clock_document: dict[str, Any],
    streams: list[dict[str, Any]],
    source_files: list[dict[str, Any]],
    schema_root: Path,
    operator_notes: str | None = None,
) -> dict[str, Any]:
    clocks = clock_document.get("clocks")
    alignments = clock_document.get("clock_alignments", [])
    primary_clock_id = clock_document.get("primary_clock_id")
    if (
        not isinstance(clocks, list)
        or not isinstance(alignments, list)
        or not isinstance(primary_clock_id, str)
    ):
        raise ValueError(
            "clock metadata must contain primary_clock_id, clocks, and clock_alignments"
        )
    capture: dict[str, Any] = {
        "tool": TOOL_ID,
        "tool_version": TOOL_VERSION,
        "primary_clock_id": primary_clock_id,
        "synchronization_state": derive_sync_state(alignments),
    }
    if operator_notes is not None:
        capture["operator_notes"] = operator_notes
    dataset = {
        "schema": schema_descriptor(schema_root),
        "dataset_id": dataset_id,
        "created_utc": created_utc,
        "robot": robot,
        "environment": environment,
        "capture": capture,
        "clocks": clocks,
        "clock_alignments": alignments,
        "streams": streams,
        "source_files": source_files,
        "integrity": {"algorithm": "sha256"},
    }
    finalized = finalize_dataset(dataset)
    validate_dataset(finalized)
    return finalized


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--telemetry-jsonl", required=True, help="Path or - for live stdin")
    parser.add_argument("--robot-metadata-json", type=Path, required=True)
    parser.add_argument("--environment-json", type=Path, required=True)
    parser.add_argument("--clock-metadata-json", type=Path, required=True)
    parser.add_argument("--external-pose-csv", type=Path)
    parser.add_argument("--external-pose-clock-id")
    parser.add_argument("--external-pose-stream-id", default="external_pose")
    parser.add_argument("--force-torque-csv", type=Path)
    parser.add_argument("--force-torque-clock-id")
    parser.add_argument("--force-torque-stream-id", default="force_torque")
    parser.add_argument("--force-torque-frame")
    parser.add_argument("--dataset-id", required=True)
    parser.add_argument("--created-utc")
    parser.add_argument("--operator-notes")
    parser.add_argument("--schema-root", type=Path, default=Path("calibration/schemas"))
    parser.add_argument(
        "--maximum-input-bytes", type=int, default=DEFAULT_LIMITS.maximum_file_bytes
    )
    parser.add_argument("--maximum-records", type=int, default=DEFAULT_LIMITS.maximum_total_samples)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    telemetry_path: Path | None = None
    if args.telemetry_jsonl == "-":
        records, telemetry_size, telemetry_sha256 = read_telemetry_jsonl(
            sys.stdin,
            maximum_records=args.maximum_records,
            maximum_bytes=args.maximum_input_bytes,
        )
    else:
        telemetry_path = Path(args.telemetry_jsonl)
        if telemetry_path.stat().st_size > args.maximum_input_bytes:
            raise ValueError("telemetry JSONL exceeds maximum input bytes")
        with telemetry_path.open("r", encoding="utf-8") as handle:
            records, telemetry_size, telemetry_sha256 = read_telemetry_jsonl(
                handle,
                maximum_records=args.maximum_records,
                maximum_bytes=args.maximum_input_bytes,
            )
    streams = group_telemetry_records(records)
    source_files: list[dict[str, Any]] = []
    if telemetry_path is not None:
        source_files.append(_source_file(telemetry_path, "application/x-ndjson"))
    else:
        source_files.append(
            {
                "name": "stdin-telemetry.jsonl",
                "sha256": telemetry_sha256,
                "media_type": "application/x-ndjson; source=stdin",
                "size_bytes": telemetry_size,
            }
        )
    if args.external_pose_csv is not None:
        if args.external_pose_clock_id is None:
            raise ValueError("--external-pose-clock-id is required with --external-pose-csv")
        streams.append(
            import_external_pose_csv(
                args.external_pose_csv,
                stream_id=args.external_pose_stream_id,
                clock_id=args.external_pose_clock_id,
            )
        )
        source_files.append(_source_file(args.external_pose_csv, "text/csv"))
    if args.force_torque_csv is not None:
        if args.force_torque_clock_id is None or args.force_torque_frame is None:
            raise ValueError(
                "--force-torque-clock-id and --force-torque-frame are required "
                "with --force-torque-csv"
            )
        streams.append(
            import_force_torque_csv(
                args.force_torque_csv,
                stream_id=args.force_torque_stream_id,
                clock_id=args.force_torque_clock_id,
                coordinate_frame=args.force_torque_frame,
            )
        )
        source_files.append(_source_file(args.force_torque_csv, "text/csv"))
    robot_document = load_json_file(args.robot_metadata_json)
    environment = load_json_file(args.environment_json)
    clock_document = load_json_file(args.clock_metadata_json)
    if (
        not isinstance(robot_document, dict)
        or not isinstance(environment, dict)
        or not isinstance(clock_document, dict)
    ):
        raise ValueError("metadata inputs must contain JSON objects")
    source_files.extend(
        [
            _source_file(args.robot_metadata_json, "application/json"),
            _source_file(args.environment_json, "application/json"),
            _source_file(args.clock_metadata_json, "application/json"),
        ]
    )
    created_utc = args.created_utc or datetime.now(UTC).isoformat(timespec="microseconds").replace(
        "+00:00", "Z"
    )
    dataset = build_dataset(
        dataset_id=args.dataset_id,
        created_utc=created_utc,
        robot=robot_document,
        environment=environment,
        clock_document=clock_document,
        streams=streams,
        source_files=source_files,
        schema_root=args.schema_root,
        operator_notes=args.operator_notes,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(canonical_json_bytes(dataset))
    print(json.dumps(validate_dataset(dataset), sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
