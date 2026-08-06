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

Exact SHAs, workflow IDs, artifact IDs, digests, test counts, physical-device
results, and any rejected Ralph-loop candidates will be recorded here after the
acceptance boundary is crossed.
