# RMA-192 License and Attribution Review — 2026-09-23

This review reconciles the release-license inventory with the product state
after RMA-012 and RMA-133. The paired
`scripts/tests/test_rma192_license_review.py` contract prevents the selected
model and Reachy-derived-asset metadata from drifting again.

## Packaged dependency and asset inventory

`third_party/inventory.json` remains the machine-readable source for the
offline app notice parity check. RMA-192 corrects two stale entries:

- Reachy Mini hardware/model assets are build inputs that are transformed into
  generated Unity presentation assets during validated builds, not merely
  "planned/not imported".
- The local language model is selected: **Qwen3 0.6B Q4_K_M**, source revision
  `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`, Apache-2.0. Its GGUF is not
  vendored or bundled by this repository.

## Repository and in-app notices

`third_party/THIRD_PARTY_NOTICES.md` and
`ReachySettingsStateStore.GetLicenseNotices()` now present the same selected
model and Reachy-derived-asset facts as the inventory.

The existing RMA-012 parity contract still requires every inventory component
name to have an offline in-app notice.

## Reachy-derived asset treatment

The pinned upstream Reachy source revision remains
`a739a6e461eb6d722901f1cfc225265ffc85c28d`, with hardware/model assets
identified as CC BY-NC-SA by the source lock. The validated build pipeline
converts the pinned MJCF/mesh inputs into Unity presentation assets and excludes
the upstream source cameras from that presentation.

The inventory and notices explicitly identify those conversion/presentation
changes and retain the attribution, noncommercial, ShareAlike, and
modification-notice obligations. This review does not reinterpret or weaken
the upstream license terms.

## Non-endorsement

The installed RMA-012 notice remains explicit that Weachy Mini is an unofficial
community project and is not endorsed by Pollen Robotics, Hugging Face, Google
DeepMind, Unity, Google, or OpenAI.

## Selected local model presentation

The selected model manifest is provenance metadata, not an automatic-download
authorization. The RMA-192 contract requires the inventory and app notice to
match the manifest's display name, source URI/revision, and Apache-2.0 license,
and to state that the GGUF is not bundled. Any later import/download UI must
present source and license information before acquisition.

## Completion rule

RMA-192 roadmap checkboxes should be reconciled only after the exact branch
head passes normal CI and the license/settings contracts. This review changes
the underlying evidence and notices; it does not pre-claim roadmap completion.
