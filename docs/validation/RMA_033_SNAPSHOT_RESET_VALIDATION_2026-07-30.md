# RMA-033 snapshot and deterministic-reset validation

**Date:** 2026-07-30  
**Validated source commit:** `1606bb5583e63a14ace171d1bfbb553d2769826a`  
**Checklist closure commit:** pending final closure

## Scope

This record closes RMA-033 only. It validates named resets, snapshot identity,
incompatibility rejection, transactional restore, authoritative-worker
ownership, and deterministic replay. RMA-040 and later tasks remain unchanged.

## Contract matrix

| Requirement | Verified behavior |
|---|---|
| Named reset poses | Stable `SleepRest=0` and `NeutralAwake=1`; unknown IDs reject. |
| No fabricated sleep pose | Official model without a named rest keyframe returns `UNSUPPORTED`. |
| Persisted identity | Snapshot version, ABI/header size, exact model hash, sequence/time, payload size, and calibration ID are mandatory. |
| Production payload | Timestep, model format, command/reset state, health, pending wrench, and complete MuJoCo integration state are bound. |
| Incompatibility | Version/model/calibration/configuration/payload/state mismatches return typed `SNAPSHOT_INCOMPATIBLE`. |
| Transactionality | Failed restore rolls back live integration state and warning data and publishes nothing. |
| Worker ownership | Capture and restore run on the authoritative worker and require `Paused`. |
| Successful restore | Future commands are discarded visibly, restored state is published, and the worker remains paused. |
| Replay tolerance | Same-runtime native/managed replay requires byte-identical final state and snapshot bytes. |
| Cross-platform tolerance | RMA-042 numeric trace tolerances remain separate and do not weaken same-runtime replay. |

## Automated evidence

### Hosted Quality Gates

Run `30561792617` passed on `1606bb55`:

- native warnings-as-errors;
- ASan and UBSan;
- managed warnings-as-errors;
- native-backed authoritative-worker snapshot acceptance;
- static repository checks;
- Android lint/tests;
- official-model validation and reference trace.

### Self-hosted Unity/Android

Run `30561792261` validated the same exact commit on `kawa` and the LG G6:

- generated presentation preparation;
- production ARM64 MuJoCo staging;
- Unity EditMode/PlayMode tests;
- ARM64 API-26 IL2CPP APK build and verification;
- installed native lifecycle acceptance;
- physical authoritative-rendering acceptance;
- evidence and APK uploads.

The first physical-rendering attempt timed out with
`runtime=Running, renderer=WaitingForSnapshots`. The app was focused, lifecycle
acceptance had passed, and no runtime fault was present. An exact rerun of the
same job, commit, and artifact passed every step without source changes. This is
recorded as an isolated device scheduling race, not hidden as a successful first
attempt.

## Result

All five RMA-033 checklist and acceptance items are supported by permanent
implementation, deterministic tests, hosted validation, and exact-head physical
Unity/Android validation.
