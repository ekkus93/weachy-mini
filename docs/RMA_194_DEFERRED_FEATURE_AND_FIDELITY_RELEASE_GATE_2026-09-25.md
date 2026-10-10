# RMA-194 deferred-feature and fidelity release gate — 2026-09-25

## Scope

This release-gate record covers the two RMA-194 acceptance criteria that can be checked from repository state without new physical-device evidence:

- no deferred Level 2, Level 3, AR, movable-observer-camera, or VLA feature is half-enabled in production; and
- the current release label must not overstate fidelity beyond the evidence already merged on `master`.

This document does **not** close the full RMA-194 release gate. Physical-device, provider, performance, representative-device, license, and end-to-end evidence still have separate checklist ownership.

## Deferred-feature boundary

The authoritative TODO keeps the following milestones explicitly outside this roadmap until they receive their own specification and TODO:

- depth-assisted Level 2 reprojection;
- persistent Level 3 scene reconstruction;
- AR placement of Reachy in a real room;
- user-controlled movable observer camera;
- optional continuous/local VLM research;
- experimental Reachy-specific VLA or learned social-motion policy.

The shipping production source contains no ARCore/world-anchoring implementation, no Level 2 depth-assisted reprojection implementation, no Level 3 persistent scene reconstruction implementation, no movable observer-camera control path, and no VLA/end-to-end learned motor-policy runtime path. Existing interfaces remain future-compatible, but those future capabilities are not selectable production modes.

The fixed-presentation camera requirement also remains in force: the application shell intentionally rejects orbit/pan/free-camera gestures and the generated scene uses a fixed front/three-quarter presentation camera independent of Reachy's simulated eye camera.

## Fidelity label boundary

The currently merged fidelity evidence is the uncalibrated upstream geometric baseline:

- `models/reachy-mini/model-parameter-audit.json` reports `profile_id=upstream_geometric_baseline`, `classification=geometric_baseline`, `calibrated=false`, `may_be_labeled_calibrated=false`, and `diagnostics_status=uncalibrated_upstream_baseline`.
- The settings default calibration label is `Uncalibrated`.
- `ProjectMetadata.InitialFidelity` remains `SimulationFidelity.Unavailable`; release labeling must therefore be derived from accepted evidence, not from an optimistic production default.
- RMA-073 can sign only unapproved fit candidates, and RMA-074 physical calibration remains optional/non-blocking unless a future contributed profile passes the explicit unit-calibrated approval gate.

Given current merged evidence, a release may describe the simulator as an uncalibrated geometric/upstream baseline and may reference completed dynamic and servo-model engineering-estimate components only with their explicit noncalibrated caveats. It must not claim `calibrated twin`, `unit-calibrated`, mature physical accuracy, Level 2/3 perception fidelity, AR readiness, or learned VLA control.

## Regression contract

`scripts/tests/test_rma194_deferred_feature_and_fidelity_gate.py` locks this release boundary by checking:

- the TODO still lists the deferred future milestones;
- production roots do not contain implementation tokens for ARCore, Level 2/3, scene reconstruction, movable observer-camera, world anchoring, or VLA paths;
- the fixed-camera/no-user-navigation contract remains documented;
- model-fidelity audit fields remain geometric and uncalibrated;
- the shipped settings default remains `Uncalibrated`; and
- `ProjectMetadata.InitialFidelity` does not pre-label the app as calibrated, servo-fidelity, or dynamic-baseline at startup.

## Closure policy

After this gate is committed to master and exact-head CI is healthy, the RMA-194 acceptance criteria for deferred-feature absence and accurate fidelity labeling may be reconciled in the authoritative TODO. When hosted CI is demonstrably unavailable only through zero-step/`JOB_NOT_FOUND` infrastructure failures, exact-head self-hosted release-source contracts plus a successful production APK build/audit are sufficient evidence for these two source/package criteria. They should not be used to close unrelated RMA-194 items or the overall release gate.
