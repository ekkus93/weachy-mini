# RMA-071 Calibration Capture Tooling Validation

**Date:** 2026-07-31
**Validated implementation commit:** `IMPLEMENTATION_COMMIT`
**Integration workflow run:** `INTEGRATION_RUN`
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

The same tool imports strict external-pose and force/torque CSV files. Headers
must match exactly and every external clock must have a declared alignment to
the primary clock.

## Synchronization boundary

`scripts/estimate_calibration_clock_offset.py` requires at least three paired
events, estimates the median offset, and reports the maximum residual as
conservative uncertainty. Exceeding the configured uncertainty ceiling fails
closed. The optional override produces an explicitly unsynchronized result
with nonzero uncertainty; it cannot be represented as synchronized.

## Validation coverage

The focused suite requires:

- grouping valid live/file JSONL records into immutable stream descriptors;
- complete synthetic capture across all eight RMA-070 sample types;
- strict external-pose CSV import;
- strict force/torque CSV import;
- rejection of stream metadata drift;
- rejection of CSV header drift;
- median offset and conservative uncertainty calculation;
- failure when uncertainty exceeds the configured ceiling;
- explicit unsynchronized output when the caller accepts that state;
- final RMA-070 validation and canonical hashing of every generated dataset.

## Physical-device work remaining

No real Reachy Mini telemetry was available for this implementation run. The
capture interface and failure paths are validated with synthetic fixtures.
Before RMA-074 can complete, an operator must connect a physical Reachy Mini,
record its exact firmware/register state, collect synchronized telemetry and
external references, and preserve the resulting non-private dataset hashes.

## Automated evidence

The permanent calibration-data workflow executes the capture CLI and clock
estimator with committed fixtures and uploads the generated dataset, summary,
and alignment report. Exact run and artifact identifiers are recorded in the
TODO completion evidence after the accepted workflow run.
