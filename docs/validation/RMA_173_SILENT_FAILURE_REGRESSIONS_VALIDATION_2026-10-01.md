# RMA-173 Silent-Failure Regression Validation — 2026-10-01

## Scope

This note records exact-head evidence for `RMA-173 — Add silent-failure regression tests` in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.

## Exact source under test

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Commit: `a7c279d3ccc01e71294718d452774a5dfe042be8`

## GitHub Actions evidence

Workflow `RMA-173 Silent Failure Regressions` run `36826674201` completed successfully on exact head `a7c279d3ccc01e71294718d452774a5dfe042be8`.

The single job, `Prove fail-closed regression contracts` (`110253782498`), passed every named RMA-173 contract step:

1. On-device ASR cannot activate cloud ASR.
2. Offline TTS cannot activate network voice.
3. Local LLM cannot activate OpenAI.
4. Invalid camera pixels cannot reuse stale content.
5. Solver failure cannot switch to Unity animation.
6. Calibration mismatch cannot become calibrated placeholder.
7. Dropped command or state buffers are visible.
8. Malformed provider response cannot fabricate intent.
9. Shipping-path silent-failure reconciliation gate.

## Release-review cross-check

The exact same source head also passed workflow `RMA Release Review Contracts` run `36826674252`. That release-review run includes the RMA-195 fail-closed composition contract plus the RMA-191/RMA-192/RMA-193/RMA-194 release contracts, reducing the risk that RMA-173 was satisfied only by isolated test-only paths.

## Evidence boundary

This document records automated fail-closed regression evidence only. It does not claim completion of physical Android scenarios owned by RMA-125, RMA-184, RMA-190, or the final RMA-194 release gate.
