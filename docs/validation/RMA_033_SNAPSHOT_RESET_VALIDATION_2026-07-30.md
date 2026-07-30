# RMA-033 snapshot and deterministic-reset validation

**Date:** 2026-07-30  
**Validated snapshot source commit:** `1606bb5583e63a14ace171d1bfbb553d2769826a`  
**Physical-acceptance hardening commit:** `ec1b08a8fef04aeae02e527e82caff9f6d7339a4`  
**Checklist closure commit:** `3c45507c28be90998f78a0884b099ccc0bdaafe6`  
**Record layout commit:** `71369e55264fd443777e7efdc3f6269ef8b572ff`

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
`runtime=Running, renderer=WaitingForSnapshots`; an unchanged exact rerun passed.
Later closure-only exact-head runs reproduced that fixed 30-second startup
binding timeout twice while the app remained focused, lifecycle acceptance
passed, and no runtime fault was present. The condition was therefore not
classified as an isolated successful-first-attempt race. Commit `ec1b08a8`
keeps the gate fail-closed but gives cold production binding 60 seconds inside a
120-second device-script envelope and adds simulation/runtime/renderer fault
state to any future timeout report.

## Result

All five RMA-033 checklist and acceptance items are supported by permanent
implementation, deterministic tests, hosted validation, and physical
Unity/Android validation. Exact-head status remains authoritative in the hosted
and self-hosted CI status records.
