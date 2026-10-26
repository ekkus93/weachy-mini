# RMA-190/RMA-194 consolidation update — RMA-184 profile metrics

Date: 2026-09-25

## Scope

This note records the latest representative-device performance evidence that supports RMA-184 and the RMA-194 release-acceptance row requiring a representative-device performance report.

It does not replace the authoritative roadmap. Roadmap checkbox closure still requires the relevant evidence PR to merge and the authoritative TODO to be reconciled on current `master`.

## Evidence now available

PR #81, `ralph/rma184-a54-profile-capture-evidence`, publishes the A54 default-profile capture evidence in:

- `docs/validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md`

The branch records two physical A54 jobs:

1. Successful pass run:
   - workflow: `A54 RMA-184 Default Profiles`
   - run id: `36187273962`
   - job id: `108243676196`
   - head: `7ee2e211d1405e4e9aa294a1dde4041cf8bcee3e`
   - result: `success`
2. Metric-extraction run:
   - workflow: `A54 RMA-184 Profile Summary Extract`
   - run id: `36192045766`
   - job id: `108259212316`
   - head: `4eabd6995aa713686526eeecf6b2422d0e043745`
   - terminal result: intentional failure after `RMA-180` acceptance passed and the `RMA184_PROFILE_SUMMARY` metrics were emitted into Ralph-readable log evidence.

## Captured default profiles

The evidence file now publishes measured 30 FPS and 60 FPS default profile rows for `NativePhysics` and `UnityRendering`, with all unexercised workloads explicitly recorded as unavailable for this default-profile session rather than implied successes.

Important captured values:

- 30 FPS profile:
  - duration seconds: `300.0421124`
  - NativePhysics: 150020 samples, median `0.4017 ms`, p95 `0.5869 ms`, p99 `0.7534 ms`, max `28.3355 ms`
  - UnityRendering: 8954 samples, median `33.32587331533432 ms`, p95 `33.43089669942856 ms`, p99 `33.525142818689346 ms`, max `133.39173793792725 ms`
- 60 FPS profile:
  - duration seconds: `300.0041515`
  - NativePhysics: 150001 samples, median `0.4341 ms`, p95 `0.6415000000000001 ms`, p99 `0.7527 ms`, max `18.425 ms`
  - UnityRendering: 15722 samples, median `16.667155548930168 ms`, p95 `33.32940116524696 ms`, p99 `33.41473266482353 ms`, max `266.6037678718567 ms`

## Consolidation status

This evidence supports the following checklist areas once it merges and the TODO is reconciled:

- RMA-184: `Publish measured default profiles`
- RMA-194: `Representative-device performance report exists`

It does not close unrelated RMA-190 scenario lanes, speech human-utterance acceptance, provider-failure lanes, model corruption/OOM, solver controlled reset, security/privacy review, license review, or documentation review.

## Current blocker

PR #81's generic PR CI currently fails with the same zero-step `JOB_NOT_FOUND` infrastructure pattern seen across other ready branches. The evidence branch itself is docs-only and the physical A54 evidence has passed. Merge remains blocked by the repository clean-mergeability gate, not by an identified source-code or documentation defect.
