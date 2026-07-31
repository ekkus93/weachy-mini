# RMA-070 Calibration Data Schema Validation

**Date:** 2026-07-31  
**Validated implementation commit:** `bba44600441165bc9b264ee211e7db25d2ababc4`  
**Fail-closed hardening commit:** `2129b3895a81d32f046c639f19bbc81021bf7825`  
**Hardening workflow run:** `30666337991`  
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
- exact schema and column-manifest hashes pinned by the v1 validator;
- canonical whole-dataset SHA-256 verification;
- monotonic timestamps and strictly increasing sequences;
- explicit clocks and source-to-primary alignments;
- derived synchronization state;
- robot register-configuration hashing;
- rejection of duplicate JSON object keys and unknown members;
- rejection of non-finite JSON constants;
- rejection of tampered content;
- rejection of missing clock alignment;
- rejection of false synchronization claims;
- bounded file, stream, clock, alignment, sample, source-file, register, and
  string resources.

## Fidelity boundary

Numeric ceilings are defensive import bounds, not Reachy calibration ranges.
The fixture dataset is generated from synthetic values and is not admissible
as a calibrated profile or physical validation dataset.

## Initial automated evidence

Integration workflow run `30662958335` passed the original 15 focused tests,
validated the committed fixture, regenerated a capture fixture, estimated a
clock offset, and uploaded the generated reports for implementation commit
`bba44600441165bc9b264ee211e7db25d2ababc4`.

## Fail-closed hardening and final sign-off

Review of the accepted implementation found boundary cases that were not
covered by the initial green gate. Commit
`2129b3895a81d32f046c639f19bbc81021bf7825` corrected them without changing
the version-1 logical record model:

- imported datasets must contain the exact pinned version-1 schema and column
  manifest hashes rather than merely well-formed SHA-256 strings;
- local schema-file drift is rejected before a capture dataset is emitted;
- strict JSON loading rejects duplicate object keys, non-finite constants,
  invalid UTF-8, and oversized input after one bounded read;
- environment notes obey the general string ceiling;
- clock and alignment collection sizes are bounded at 64 and 63;
- the permanent documentation now states these constraints explicitly.

One-shot hardening run `30666337991` passed `python3 -m compileall`, all 23
calibration regression tests, committed-fixture validation, clock estimation,
synthetic capture, and final dataset validation. It then removed its temporary
workflow and patch script before committing the clean tree.

The run published artifact `8807094560`, named
`calibration-hardening-evidence-2129b3895a81d32f046c639f19bbc81021bf7825`,
with ZIP SHA-256
`024e0412fe509d7bc94c4e72537beba19ce1e378f0eff5f862eb5a7654c13981`.
The generated hardened capture validated with dataset SHA-256
`6e3cd3ec080840bb818e280b9729be91ff5463f09cd389d2c7293663015cf061`.
The exact hardening commit received the successful
`RMA-070/RMA-071 Calibration Data` commit status.

RMA-070 is complete. The evidence remains limited to schema, parser, fixture,
and synthetic capture validation; no physical calibration claim is made.
