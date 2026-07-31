# Calibration Data and Capture Architecture

## Scope

RMA-070 and RMA-071 define the data boundary used by later Reachy Mini
calibration experiments. They do not create a calibrated robot profile and do
not make an accuracy claim. RMA-074 remains responsible for physical data
collection, fitting, held-out validation, and a unit-specific calibrated
profile.

The implementation has four permanent parts:

- `calibration/schemas/calibration-dataset-v1.schema.json` defines the strict
  JSON representation;
- `calibration/schemas/calibration-stream-columns-v1.json` defines the logical
  columns and units used by JSON, CBOR, and Parquet encodings;
- `scripts/calibration_data.py` performs bounded, fail-closed validation and
  canonical hashing without a third-party runtime dependency;
- `scripts/capture_reachy_calibration.py` and
  `scripts/estimate_calibration_clock_offset.py` capture or import source data
  while preserving explicit clock provenance.

## Dataset envelope

The `rma070_calibration_dataset_v1` envelope contains:

- exact schema and column-manifest hashes;
- a stable dataset identifier and UTC creation timestamp;
- robot identity, hardware revision, firmware, register configuration, and a
  hash of that configuration;
- ambient conditions without silently inventing missing measurements;
- capture-tool identity and synchronization state;
- monotonic clock definitions and source-to-primary clock alignments;
- typed sample streams;
- source-file names, media types, sizes, and hashes;
- a canonical whole-dataset SHA-256.

The dataset hash is calculated over canonical UTF-8 JSON with sorted keys,
compact separators, finite JSON numbers, and a trailing newline. The
self-referential `integrity.dataset_sha256` member is removed before hashing.
Changing any other field or sample invalidates the digest.

## Stream model

Every stream has one sample type, one clock domain, and a strictly increasing
sequence. `timestamp_ns` is an unsigned integer in nanoseconds and must be
monotonic nondecreasing within the stream. Optional
`arrival_timestamp_ns` records host receipt time but never replaces the source
timestamp.

Version 1 defines these stream tables:

- `command`;
- `joint`;
- `current_load`;
- `voltage`;
- `imu`;
- `external_pose`;
- `force_torque`;
- `temperature`.

Units are encoded in column names and repeated in the column manifest. Vector
values use fixed-length arrays, which map to CBOR arrays and Parquet fixed-size
list columns. Optional values map to JSON/CBOR null and nullable Parquet
columns. A Parquet export uses one table per stream rather than mixing
incompatible sample records into one sparse table.

## Coordinate frames

IMU, external-pose, and force/torque streams require an explicit
`coordinate_frame`. External-pose samples also carry parent and child frame
identifiers. This contract records frame names but does not infer transforms or
silently convert axes. Experiment-specific frame relationships belong in the
capture metadata or a later versioned transform manifest.

## Clock alignment

One clock is declared primary. Every non-primary clock requires exactly one
alignment to the primary clock with:

- signed offset in nanoseconds;
- nonnegative uncertainty in nanoseconds;
- method;
- synchronization-event count;
- an explicit synchronized boolean.

`scripts/estimate_calibration_clock_offset.py` computes the median paired-event
offset and uses the maximum absolute residual as conservative uncertainty. It
fails when uncertainty exceeds the caller's budget unless
`--allow-unsynchronized` is given. In that case the output is explicitly marked
`method=unsynchronized`, `synchronized=false`, and retains nonzero uncertainty.

The dataset-level synchronization state is derived from the alignments. A file
cannot claim `synchronized` when any declared clock is unsynchronized. Missing
alignment metadata is a validation error.

## Physical telemetry capture

`scripts/capture_reachy_calibration.py` accepts newline-delimited JSON telemetry
from a file or live standard input. This intentionally separates the
repository's stable capture contract from a particular Reachy SDK or transport.
A physical adapter may stream records from serial, SDK callbacks, or another
process without adding that transport to the trusted dataset parser.

Each JSONL record contains:

```json
{
  "stream_id": "joints",
  "sample_type": "joint",
  "clock_id": "reachy_clock",
  "sample": {
    "timestamp_ns": 1000000,
    "sequence": 1,
    "actuator_id": "yaw_body",
    "position_rad": 0.09,
    "velocity_rad_s": 0.01,
    "applied_torque_nm": 0.02,
    "fault_flags": 0
  }
}
```

A stream may not change its sample type, clock, frame, or description during a
capture. Input byte and record ceilings are enforced before a dataset is
created.

## External imports

The capture tool supports two strict CSV forms:

- external pose with timestamp, sequence, frame names, position, quaternion,
  and optional confidence;
- force/torque with timestamp, sequence, sensor identity, force vector, and
  torque vector.

CSV headers must match exactly. Unknown columns, missing columns, empty data,
non-numeric values, excessive size, or excessive row counts fail visibly. The
caller must provide the source clock ID. A non-primary source cannot enter a
valid dataset without explicit alignment and uncertainty metadata.

## Import security and failure policy

The validator treats datasets and imports as untrusted. The default ceilings
are:

- 256 MiB per JSON or CSV input;
- 64 streams;
- 1,000,000 samples per stream;
- 2,000,000 samples total;
- 256 source files;
- 4,096 register entries;
- 4,096 characters per general string.

The validator rejects unknown object members, duplicate identifiers,
non-finite numbers, malformed hashes, nonmonotonic samples, sequence reuse,
out-of-range import values, invalid quaternion norms, undeclared clocks,
missing alignments, false synchronization claims, and canonical hash mismatch.
The broad numeric ranges are import-safety limits, not physical calibration
limits and not evidence that a value is plausible for Reachy Mini.

## Commands

Validate a committed or imported dataset:

```bash
python3 scripts/validate_calibration_dataset.py \
  --input calibration/fixtures/minimal-calibration-dataset.json
```

Estimate a camera-to-Reachy clock alignment:

```bash
python3 scripts/estimate_calibration_clock_offset.py \
  --pairs-csv calibration/fixtures/clock-pairs.csv \
  --from-clock-id camera_clock \
  --to-clock-id reachy_clock \
  --maximum-uncertainty-ns 1000 \
  --output build/calibration/camera-alignment.json
```

Build a dataset from physical telemetry and external instruments:

```bash
python3 scripts/capture_reachy_calibration.py \
  --telemetry-jsonl - \
  --robot-metadata-json calibration/fixtures/robot-metadata.json \
  --environment-json calibration/fixtures/environment.json \
  --clock-metadata-json calibration/fixtures/clock-metadata.json \
  --external-pose-csv pose.csv \
  --external-pose-clock-id camera_clock \
  --force-torque-csv force.csv \
  --force-torque-clock-id force_clock \
  --force-torque-frame reachy_body \
  --dataset-id reachy-unit-capture-001 \
  --output build/calibration/capture.json
```

The fixture files are synthetic test data and must never be presented as a
physical calibration dataset.
