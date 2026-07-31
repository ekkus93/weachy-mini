# RMA-074 Physical Calibration Validation

**Date:** 2026-07-31  
**Status:** In progress — physical-unit discovery pending

## Completion boundary

RMA-074 cannot be accepted from synthetic data or a simulator. Completion
requires a real Reachy Mini unit, separate physical fitting and held-out
datasets, a unit-specific fitted profile, the complete required held-out metric
report, accurate calibrated/uncalibrated labeling, and a redistributable
repository report.

## Current work

The first permanent gate is a read-only physical-unit probe. It:

- targets the existing self-hosted hardware runner;
- checks the pinned daemon status and physical hardware identifier;
- rejects simulation, mock, unready, and faulted backends;
- observes joints and head pose without enabling torque or commanding motion;
- publishes only a hashed unit identity and redacted connection metadata;
- records the telemetry sources that are and are not available.

A passing preflight authorizes implementation of the separate motion-capture
workflow. It does not itself authorize motion or establish calibration.

## Required remaining evidence

- successful read-only physical-unit artifact;
- reviewed production `ExperimentAdapter`;
- explicit operator motion authorization;
- physical fitting dataset;
- independently captured physical held-out dataset;
- unit-specific fit candidate;
- position, orientation, settling, overshoot, current, decay, and contact
  metric results, with unsupported sources called out;
- approved calibrated-profile envelope signed with a non-fixture key;
- profile-selection/UI labeling tests and integration;
- final permanent workflow on an exact clean commit.

No RMA-074 completion claim is made in this document until every applicable
item above has exact hashes and accepted workflow evidence.
