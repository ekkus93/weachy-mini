# RMA-070 Calibration Data Schema Validation

**Date:** 2026-07-31
**Validated implementation commit:** `bba44600441165bc9b264ee211e7db25d2ababc4`
**Integration workflow run:** `30662958335`
**Decision:** Implementation complete; synthetic contract validation only

## Scope

RMA-070 defines a versioned, storage-neutral calibration dataset contract. It
covers command, joint, current/load, voltage, IMU, external pose,
force/torque, and temperature samples. The contract records robot, firmware,
register, environment, source-file, clock, synchronization, schema, and
integrity metadata.

This task does not claim that any fixture value is physically measured or
calibrated.

## Contract evidence

The committed contract consists of:

- `calibration/schemas/calibration-dataset-v1.schema.json`;
- `calibration/schemas/calibration-stream-columns-v1.json`;
- `scripts/calibration_data.py`;
- `scripts/validate_calibration_dataset.py`;
- `calibration/fixtures/minimal-calibration-dataset.json`.

The logical records use only maps, arrays, UTF-8 strings, booleans, nulls,
integers, and finite floating-point values. They therefore map directly to
JSON or CBOR. The column manifest defines one Parquet table per stream,
fixed-size vector columns, and nullable optional fields.

## Validation coverage

The focused suite requires:

- one valid sample for all eight sample types;
- exact schema and column-manifest hashes;
- canonical whole-dataset SHA-256 verification;
- monotonic timestamps and strictly increasing sequences;
- explicit clocks and source-to-primary alignments;
- derived synchronization state;
- robot register-configuration hashing;
- rejection of unknown members;
- rejection of non-finite JSON constants;
- rejection of tampered content;
- rejection of missing clock alignment;
- rejection of false synchronization claims;
- bounded file, stream, sample, source-file, register, and string resources.

## Fidelity boundary

Numeric ceilings are defensive import bounds, not Reachy calibration ranges.
The fixture dataset is generated from synthetic values and is not admissible
as a calibrated profile or physical validation dataset.

## Automated evidence

The permanent calibration-data workflow runs the focused unit tests, validates
the committed fixture, regenerates a capture fixture, estimates a clock
offset, and uploads the generated reports. Exact run and artifact identifiers
are recorded in the TODO completion evidence after the accepted workflow run.
