# RMA-194 Release Acceptance Status — 2026-09-28

This document records the current release-gate status for the Reachy Mini Android digital twin. It is intentionally conservative: implementation and static contracts may exist before exact-head CI, Local Unity Android Validation, physical-device, provider, performance, privacy, license, and release-acceptance evidence is complete.

## Release status

**Release status: not accepted.** No final release tag should be cut from the current tree, and no TODO checkbox in RMA-194 should be closed merely because this status document exists.

The project currently has evidence for three RMA-194 subitems that were already checked in the authoritative TODO before this document:

- MuJoCo is authoritative and stable, with supporting implementation status and RMA-032/RMA-060 through RMA-065 evidence.
- The full closed-loop Reachy model is rendered from native state, with RMA-051/RMA-052 evidence and no hidden kinematic fallback.
- Level 1 camera reprojection passes reference tests, with RMA-104 validation evidence and the reprojection workflow retained.

All other RMA-194 checklist entries remain open until their own acceptance evidence passes at the exact candidate head.

## Current open release blockers

The current release gate remains blocked by the following evidence gaps:

- RMA-125 / RMA-190: physical offline speech and spoken end-to-end scenarios still require device proof with suitable installed Android on-device ASR and exact offline TTS services.
- RMA-184: representative-device default profiles require measured low, mid, and high class reports; mid/high physical measurements are still pending.
- RMA-190: production end-to-end scenarios remain unchecked and must exercise shipping runtime paths, not test-only substitutes represented as production evidence.
- RMA-191: the security and privacy review has systematic contracts in-tree, but exact-head CI/device evidence must pass before TODO closure.
- RMA-192: the license and attribution review has systematic contracts in-tree, but exact-head CI evidence must pass before TODO closure.
- RMA-193: the release operator documentation hub exists in-tree, but exact-head CI evidence must pass before TODO closure.
- RMA-194: the remaining release checklist and acceptance criteria must be reconciled only after the evidence above is complete.

## Deferred-feature boundary

The initial release must not half-enable deferred future milestones. Level 2 depth reprojection, Level 3 scene reconstruction, AR placement, movable observer camera, and Reachy-specific VLA or learned social-motion policy remain future milestones and must not be labeled as active release features.

The correct behavior for missing deferred capability is an explicit unavailable, unsupported, or deferred state. The app must not provide a hidden fallback path that treats Level 1 reprojection as depth-aware, substitutes stale camera content, labels nominal dynamics as calibrated physical behavior, or silently promotes cloud/provider paths across a privacy boundary.

## Release labeling rule

No calibrated twin label is allowed without a named physical calibration profile and held-out physical evidence. No final release label is assigned by this document. A future release candidate must choose one of the documented labels — geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin — strictly according to the evidence actually accepted at that candidate head.

## Evidence handling

Infrastructure failures such as zero-step hosted CI jobs, artifact-storage/upload failures, missing GitHub Actions job logs, pending self-hosted jobs, or transient bridge/API failures are not release evidence and are not source-code failures. They should keep the affected checklist items open while independent source, documentation, security, privacy, license, and release-gate work continues.

When the remaining evidence is available, update this file together with the authoritative TODO and include exact commit SHA, workflow run IDs, physical-device reports, artifact digests, tested device matrix, fidelity label, and known limitations.
