# RMA-071 Calibration Capture Tooling Validation

**Date:** 2026-07-31  
**Validated implementation commit:** `bba44600441165bc9b264ee211e7db25d2ababc4`  
**Fail-closed hardening commit:** `2129b3895a81d32f046c639f19bbc81021bf7825`  
**Hardening workflow run:** `30666337991`  
**Decision:** Tooling implementation complete; physical robot capture deferred

## Scope

RMA-071 supplies host-side tooling for physical Reachy telemetry capture,
external pose import, force/torque import, and explicit clock alignment. It
does not require a particular Reachy transport and does not fabricate
synchronization when the source clocks are unrelated.

RMA-074 remains responsible for acquiring a real unit-specific dataset and
proving a calibrated profile against held-out physical motions.

## Capture boundary

`scripts/capture_reachy_calibration.py` consumes bounded JSONL from a file or
live standard input. Each record declares its stream, sample type, clock, and
sample. Stream metadata is immutable during a capture. A transport adapter may
pipe serial or SDK telemetry into the tool without becoming part of the
versioned dataset parser.

The same tool imports strict external-pose and force/torque CSV files. Headers,
including column order and uniqueness, must match exactly, and every external
clock must have a declared alignment to the primary clock.

## Synchronization boundary

`scripts/estimate_calibration_clock_offset.py` requires at least three paired
events, estimates the median offset, and reports the maximum residual as
conservative uncertainty. Exceeding the configured uncertainty ceiling fails
closed. The optional override produces an explicitly unsynchronized result
with nonzero uncertainty; it cannot be represented as synchronized.

The source and primary clock identifiers must differ. Input timestamps,
estimated offsets, and uncertainty must remain inside the version-1 import
bounds before an alignment record can be emitted.

## Validation coverage

The focused suite requires:

- grouping valid live/file JSONL records into immutable stream descriptors;
- complete synthetic capture across all eight RMA-070 sample types;
- strict external-pose CSV import;
- strict force/torque CSV import;
- rejection of duplicate JSONL object keys;
- rejection of stream metadata drift;
- rejection of CSV header drift, reordering, and duplication;
- median offset and conservative uncertainty calculation;
- rejection of self-alignment and out-of-contract clock estimates;
- failure when uncertainty exceeds the configured ceiling;
- explicit unsynchronized output when the caller accepts that state;
- final RMA-070 validation and canonical hashing of every generated dataset.

## Physical-device work remaining

No real Reachy Mini telemetry was available for this implementation run. The
capture interface and failure paths are validated with synthetic fixtures.
Before RMA-074 can complete, an operator must connect a physical Reachy Mini,
record its exact firmware/register state, collect synchronized telemetry and
external references, and preserve the resulting non-private dataset hashes.

This remaining physical-data work does not reopen RMA-071: the task requires
the capture/import tooling and explicit synchronization behavior, while
RMA-074 owns unit-specific physical acquisition and calibration acceptance.

## Initial automated evidence

Integration workflow run `30662958335` passed the original focused suite,
executed the capture CLI and clock estimator with committed fixtures, and
uploaded the generated dataset, summary, and alignment report for
implementation commit `bba44600441165bc9b264ee211e7db25d2ababc4`.

## Fail-closed hardening and final sign-off

Review of the accepted tooling found untested parser and synchronization
boundaries. Commit `2129b3895a81d32f046c639f19bbc81021bf7825` hardened them:

- telemetry JSONL now shares the strict duplicate-key and non-finite-number
  parser used by dataset imports;
- synchronization CSV headers must match the exact ordered, unique contract;
- source and target clock IDs cannot be identical;
- paired timestamps, estimated offset, and uncertainty are bounded to values
  representable by the RMA-070 version-1 validator;
- schema and column-manifest drift cannot be hidden by metadata supplied by the
  capture command.

One-shot hardening run `30666337991` passed `python3 -m compileall`, all 23
calibration regression tests, committed-fixture validation, clock estimation,
synthetic capture, and final RMA-070 validation. It removed the temporary
workflow and patch script before committing the clean tree.

The run published artifact `8807094560`, named
`calibration-hardening-evidence-2129b3895a81d32f046c639f19bbc81021bf7825`,
with ZIP SHA-256
`024e0412fe509d7bc94c4e72537beba19ce1e378f0eff5f862eb5a7654c13981`.
The generated hardened capture validated with dataset SHA-256
`6e3cd3ec080840bb818e280b9729be91ff5463f09cd389d2c7293663015cf061`.
The exact hardening commit received the successful
`RMA-070/RMA-071 Calibration Data` commit status.

RMA-071 is complete as capture and import infrastructure. Physical Reachy Mini
data collection remains explicitly deferred to RMA-074 and has not been
fabricated.
