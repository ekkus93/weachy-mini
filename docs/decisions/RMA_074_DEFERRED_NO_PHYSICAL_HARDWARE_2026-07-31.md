# RMA-074 Physical Calibration Deferral

**Date:** 2026-07-31  
**Decision:** Deferred; not required for the emulated Reachy Mini MVP  
**Project:** Reachy Mini Android digital twin

## Context

The project owner does not have access to a physical Reachy Mini. The purpose of
this repository is to create an emulated/digital-twin Reachy Mini that runs on
Android hardware. The LG Android phone and the `kawa` self-hosted runner are
test infrastructure for the emulator; they are not a substitute for a physical
Reachy Mini calibration source.

RMA-074 explicitly requires measurements from a physical Reachy Mini, a
unit-specific fit, and held-out real-robot validation. Those requirements cannot
be truthfully satisfied with MuJoCo output, synthetic fixtures, the Android
phone, or an emulated daemon.

## Decision

- RMA-074 remains incomplete and is deferred until a physical Reachy Mini and
  suitable measurement instrumentation become available.
- RMA-074 does not block Phase 9 or later emulator implementation.
- The simulator must continue to label its active dynamics accurately as
  `upstream_baseline`, engineering-estimate/servo-fidelity, or uncalibrated.
- The application must not display `Calibrated for this unit` without a valid
  RMA-074 unit approval.
- Synthetic RMA-070 through RMA-073 evidence remains valid for validating the
  calibration infrastructure and estimators, but it is not physical calibration.
- The RMA-074 physical preflight workflow is manual-only and optional. Normal
  pushes must not fail because no physical Reachy Mini is present.

## Repository treatment

The physical preflight, approval schema, signature verification, and label
resolver are retained as optional future infrastructure. They remain fail
closed and cannot promote synthetic data into a calibrated claim.

No USB setup, Reachy daemon installation, robot hostname, or physical motion
procedure is required for current emulator development.

## Next ordered work

Continue with **RMA-080 — Create application state architecture**, followed by
RMA-081 and RMA-082. The calibration-profile selector should expose the current
uncalibrated/baseline state accurately while keeping the deferred physical
profile path unavailable unless valid unit-specific evidence is installed.
