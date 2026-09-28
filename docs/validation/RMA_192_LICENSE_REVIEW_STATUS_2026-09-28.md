# RMA-192 License Review Status — 2026-09-28

## Scope

This note records the current state of the RMA-192 license and attribution review while preserving evidence boundaries. It does not close any RMA-192 checkbox.

## Canonical branch under review

- Branch: `ralph/rma192-license-attribution-review`
- Head inspected: `9c13614f78849e2a2b469f0d4e9c8b6c7f2af945`
- Open PR: #83

## Safe observations from the branch diff

The branch contains a coherent license/attribution review record in `docs/RMA_192_LICENSE_ATTRIBUTION_REVIEW_2026-09-25.md` and non-redacted source/test changes for:

- `Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.Labels.cs`, including explicit coordinate-converted Reachy derivative notice text and selected Qwen model notice text;
- `scripts/prepare_reachy_unity_assets.py`, carrying Reachy `ATTRIBUTION.md` and `UPSTREAM_LICENSE` into generated Unity presentation output and recording licensing hashes in `UNITY_RENDER_MAP.json`;
- `scripts/tests/test_prepare_reachy_unity_assets.py`, covering the generated attribution and upstream-license propagation;
- `scripts/tests/test_rma192_license_attribution_review.py`, tying inventory, offline notices, Reachy derivative handling, no-endorsement disclosure, selected local-model metadata, and no-bundled-model policy together.

## Redaction boundary

The same diff modifies `third_party/THIRD_PARTY_NOTICES.md` and `third_party/inventory.json`, but Ralph redacted their patches because they contain URL-bearing records. Because those files are core license-review data, this reconciliation branch intentionally does not reconstruct or partially transplant them from redacted output.

Do not close RMA-192 checkboxes from this note alone. Closure requires the complete license-review branch or an equivalent exact, non-redacted reconciliation of inventory, notices, in-app labels, generated-asset notices, selected-model metadata, and regression tests.

## Current blocker

Repository generic PR CI continues to fail before job steps/logs are available with the zero-step `JOB_NOT_FOUND` infrastructure signature on active reconciliation branches. That is not a license source failure, but it prevents clean merge/reconciliation under exact-head policy.
