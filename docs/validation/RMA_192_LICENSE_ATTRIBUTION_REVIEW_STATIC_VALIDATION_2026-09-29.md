# RMA-192 License and Attribution Review Static Validation — 2026-09-29

## Scope

This document records a source-level regression-gate update for RMA-192 license and attribution review.

The gate does not close the full release legal review by itself. It preserves the existing RMA-192 release-review checks and adds repository contracts that bind the current license, notice, inventory, app-notice, model-selection, Reachy-derived asset, and non-endorsement assumptions so later drift fails visibly.

## Updated permanent gate

Existing test file updated:

- `scripts/tests/test_rma192_license_attribution_review.py`

The file is intended to run through the existing static validation path:

```bash
python3 -m unittest discover -s scripts/tests -v
```

## Preserved pre-existing coverage

The update preserves the pre-existing RMA-192 checks for:

- packaged dependency inventory backing and notice anchors;
- repository and app notice coverage for MuJoCo, Reachy assets, non-endorsement, and noncommercial language;
- Reachy-derived ShareAlike/noncommercial attribution obligations;
- offline app notice non-endorsement language;
- selected local-model manifest and Android release operator-guide identity/hash requirements.

## Added coverage

| RMA-192 area | Additional source-level coverage |
| --- | --- |
| Reconcile packaged dependencies/assets against inventory | Requires `third_party/inventory.json` schema version 1, unique entries, required fields, and expected inventory IDs for Unity, MuJoCo, Reachy software/assets, llama.cpp, local-model candidate, AndroidX CameraX, ML Kit face detection, ML Kit selfie segmentation, and the RMA-111 public-domain fixture. |
| Verify notices in repository and app | Requires Apache-2.0 root `LICENSE`, README pointers to `third_party/THIRD_PARTY_NOTICES.md` and `third_party/inventory.json`, repository notices, and the app's offline notices UI path (`DrawLicenseSettings`, `ReachyLicenseNotice`, `GetLicenseNotices`, component/attribution/license-reference display). |
| Verify Reachy-derived asset attribution/share-alike treatment | Requires the Reachy hardware/model asset inventory entry to stay CC-BY-NC-SA and blocked pending asset audit, with attribution, noncommercial, ShareAlike, modification notice, pinned-source, provenance, and fail-visible asset-policy language. |
| Verify no endorsement claim | Scans README and third-party notice Markdown for prohibited positive endorsement phrases while requiring the explicit not-endorsed disclaimer to remain present. |
| Verify selected local model redistribution/download presentation | Requires the local-model inventory entry to remain candidate/blocked-pending-selection, requires notices to state that no model is selected or distributed, and requires the asset policy to prohibit committed GGUF/model binaries. |

## Current evidence boundary

This commit updates the review gate and this validation record. It does not by itself mark RMA-192 complete.

RMA-192 should remain open until an exact-head validation result confirms that static unittest discovery passed with this updated file present, and until any final release-packaging notices or generated dependency notices required by packaged APK/AAB contents are either produced or explicitly deferred to a named later milestone with rationale.

## Related context

RMA-191 static validation was added immediately before this slice in commit `7af8f053e559a380358649c85e397983afe96d2c`, and the latest completed A54 no-artifacts validation before the RMA-191/RMA-192 static-review work was run `36630438538` on `bff9c7789331ed72320260821d4d024f303d3a8b`.

This RMA-192 slice is source/documentation validation only; it does not change production behavior or packaged Android assets.
