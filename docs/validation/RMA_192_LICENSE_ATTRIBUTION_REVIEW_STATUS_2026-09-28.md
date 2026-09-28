# RMA-192 license and attribution review status — 2026-09-28

## Scope

This note records the current RMA-192 review boundary for current `master`.

The inspected source lane was:

- branch: `ralph/rma192-license-attribution-review`
- inspected head: `9c13614f78849e2a2b469f0d4e9c8b6c7f2af945`

The lane aims to reconcile the release-facing third-party inventory, generated Reachy asset attribution, offline in-app notices, and selected local-model metadata.

## Coverage described by the inspected lane

The readable review document describes coverage for:

- packaged dependency and asset inventory state;
- Reachy source-lock identity, Reachy attribution, ShareAlike, and modification treatment;
- generated Unity presentation attribution and upstream-license propagation;
- no-endorsement disclosure;
- selected Qwen3 0.6B Q4_K_M manifest identity, Apache-2.0 license, size, and SHA-256;
- no-model-binary repository policy and the currently unavailable shipping acquisition UI.

The readable test plan was `scripts/tests/test_rma192_license_attribution_review.py`, intended to tie inventory, third-party notices, offline license labels, asset-prep scripts, model manifest, and asset policy together.

## Transplant boundary

The RMA-192 source lane modifies `third_party/inventory.json` and `third_party/THIRD_PARTY_NOTICES.md`. During current review, Ralph returned `third_party/inventory.json` as a redacted URL-bearing record. Because the full machine-readable inventory is the primary source of truth for this review, the license/attribution changes are not transplanted into current `master` by this note.

No partial inventory, notice text, selected-model metadata, or generated attribution requirement is reconstructed from a redacted view.

## Current closure status

This status note does **not** close RMA-192. The authoritative checklist items remain open until the full inventory/notices/source changes are safely readable, applied to current `master`, and qualified with exact-head validation.

RMA-192 also remains relevant to RMA-194's broader `Licenses and attribution are complete` release acceptance item.
