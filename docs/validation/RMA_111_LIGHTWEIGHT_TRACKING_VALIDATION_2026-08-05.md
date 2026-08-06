# RMA-111 Lightweight Tracking Validation

**Milestone:** RMA-111

**Date:** 2026-08-05

**Status:** Implementation validation in progress; no completion claim

## Candidate contract

The candidate installs bundled on-device ML Kit face detection and selfie
segmentation behind the RMA-110 `IVisualTracker` boundary. It consumes only
owned transformed Reachy-eye frames with validity metadata, applies managed
center-validity filtering, assigns stable local IDs, and expires stale tracks
deterministically.

## Fail-closed requirements

The candidate:

- does not invoke a VLM;
- does not use a network model-download dependency;
- does not retry or select a fallback provider;
- does not queue concurrent requests;
- does not accept raw phone frames for normal tracking;
- does not report detections centered in invalid pixels; and
- does not preserve a stale detection after expiry or continuity reset.

## Required validation

Before RMA-111 can be marked complete, one exact implementation SHA must pass:

- managed warnings-as-errors contracts for validity, stable IDs, expiry,
  ordering, cancellation, provider failure, concurrency, and ownership;
- Android bridge Java warnings-as-errors compilation and lint with the exact
  bundled ML Kit dependency pins;
- real-graphics Unity tests for owned texture copies, bounded staging,
  top-left conversion, asynchronous GPU readback, and deferred disposal;
- ARM64 API-26 APK build and verification;
- physical RMA-111 bundled face inference using the pinned licensed fixture;
- all existing RMA-090, RMA-091, RMA-092, lifecycle, and authoritative-rendering
  regressions; and
- permanent RMA-111 workflow, hosted CI, and final commit-status publication.

## Rejected candidates and repairs

- Candidate `3b8b9440f60ff8a79de42e22537a273515b81567` was rejected after permanent
  workflow run `31064376991` failed during Unity script compilation. The four
  new runtime files declared `ReachyMini.Application`, which shadowed
  `UnityEngine.Application` throughout sibling `ReachyMini.*` namespaces and
  broke existing `persistentDataPath`, `isPlaying`, `platform`, and
  `onBeforeRender` references.
- Repair `f833e50cb25578499a1366eea6c015e1286baa1f` moves those runtime types into
  the existing `ReachyMini.AppState` namespace, updates the scoped editor-test
  import, and removes the non-Android dead `disposed` assignment. The one-use
  repair workflow failed closed on unexpected source shapes and removed itself
  from the repair commit.
- Candidate `33452be00d74e70aa52fd98b26d92c69a04797c9` was rejected after permanent
  workflow run `31067334232` compiled the runtime assembly but failed the
  editor-test assembly. The RMA-111 validity texture test supplied a `Color[]`
  to a helper requiring `Color32[]`.
- Repair `9af54646567537ea642b811b7f337a9c934088d1` supplies four explicit
  `Color32` validity pixels. It also closes a separate fail-closed lifecycle
  defect in the Android bridge: a synchronous exception from either ML Kit
  `process()` start can no longer strand `activeRequest` or leak the request
  bitmap. Face listeners are attached before person segmentation starts, the
  first-task start failure releases ownership immediately, and a second-task
  start failure drains through the already-running face task. A permanent
  managed source contract verifies that ordering and cleanup shape.
- One-use repair run `31067797579` passed its exact-pattern applicator, managed
  warnings-as-errors contracts, scoped repository-diff gate, and self-removal
  checks before producing `9af54646567537ea642b811b7f337a9c934088d1`.
- Exact-head permanent validation of the repaired source remains required; this
  document does not treat applicator checks as completion evidence.

Exact SHAs, workflow IDs, artifact IDs, digests, test counts, physical-device
results, and any additional rejected Ralph-loop candidates will be recorded
here after the acceptance boundary is crossed.
