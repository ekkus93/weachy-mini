# RMA-112 Bounded World Model Validation

**Task:** RMA-112 — Implement bounded world model  
**Date:** 2026-08-06  
**Status:** Implementation candidate under exact-SHA validation

## Scope

RMA-112 adds a Unity-independent managed world model that consumes the existing RMA-110/RMA-111 perception contracts without inventing unsupported spatial information.

The implementation is in:

- `Assets/ReachyMini/Runtime/Core/Perception/ReachyBoundedWorldModel.cs`
- `managed/ReachyMini.WorldModel.Tests/Program.cs`
- `managed/ReachyMini.WorldModel.Tests/ReachyMini.WorldModel.Tests.csproj`
- `.github/workflows/rma112-bounded-world-model.yml`

## Implemented contract

The world model stores and exposes:

- stable entity identity and entity generation;
- tracker-local identity, classification, confidence, and provider provenance;
- first-seen and last-seen monotonic timestamps;
- camera, source session, source sequence, authoritative sequence, and simulation continuity provenance;
- normalized Reachy-eye direction estimates and source bounds;
- explicit unknown metric position for two-dimensional tracker observations;
- coverage state, valid fraction, and coverage diagnostic;
- bounded observation history;
- bounded semantic-description history, confirmation count, provider provenance, and description age;
- immutable current/recent snapshots for future conversation and behavior consumers;
- bounded ordering cursors and visible drop diagnostics.

## Fail-closed behavior

The implementation does not:

- fabricate a metric or three-dimensional position from two-dimensional bounds;
- silently refresh an entity after tracker failure or unusable coverage;
- accept stale or conflicting observations;
- silently overwrite retained entities when capacity is exhausted;
- attach semantic descriptions to a reused tracker ID from another entity generation;
- retain unbounded entities, observations, descriptions, text, or source-session cursors;
- substitute a fallback provider or stale semantic result.

Capacity rejection, stale/conflicting input, unsupported coverage, description rejection, and bounded-history drops are returned or counted explicitly.

## Deterministic tests

The managed contract harness covers sixteen cases:

1. stable tracker identity updates one entity;
2. continuity reset creates a new entity generation;
3. expiry occurs at the exact configured boundary;
4. unusable coverage creates no observation and does not refresh visibility;
5. degraded coverage remains attached to the observation;
6. normalized semantic duplicates are deduplicated and age is explicit;
7. description history and text length remain bounded;
8. published snapshots are deep immutable copies;
9. entity-capacity failure is atomic and visible;
10. a long synthetic stream remains within all configured limits;
11. source-session ordering cursors remain bounded;
12. two-dimensional tracking never reports a metric position;
13. current, recent, and expired visibility are distinct;
14. stale and conflicting observations fail visibly;
15. semantic results cannot cross entity generations;
16. source contracts retain the no-fallback and bounded-state requirements.

## Candidate history

### Source implementation

Commit `b1bd69869af6eefc4661ce141999103b7d969cc5` introduced the production implementation, tests, and permanent workflow.

The production managed core built with warnings as errors and zero warnings. The first dedicated RMA-112 run, `31084148208`, then exposed two test-harness compilation defects:

- `TrackedTracked` instead of `TrackedObject`;
- analyzer rule CA2249 requiring `string.Contains` rather than `string.IndexOf` for the exact source-contract assertions.

No production contract was weakened.

### Harness repair

Commit `d65a6cfe619f5b67da9fbb53411ddfc610b6ded7` corrected only those two deterministic harness defects. Commit `bc4c26ac09fb257a42a8568e77d5a1487943b6a5` was a no-tree-change user-authored validation trigger because GitHub suppresses recursive workflow execution for pushes made by `GITHUB_TOKEN`.

The earlier physical run `31084147073` was cancelled by the subsequent repair commits under the permanent latest-commit concurrency policy; it did not report a device or source failure.

## Acceptance boundary

This document intentionally does not mark RMA-112 complete yet. Completion requires one exact final commit to pass:

- `RMA-112 Bounded World Model`;
- hosted CI and managed warnings-as-errors;
- the complete self-hosted Unity, Android ARM64/API-26, physical-camera, lifecycle, and authoritative-rendering validation;
- final evidence artifact and digest review;
- repository cleanup verification.
