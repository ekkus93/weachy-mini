# RMA-191 / RMA-192 / RMA-193 release-review validation — 2026-09-29

## Scope

This report records exact-head non-device release-review evidence for RMA-191 (privacy/security), RMA-192 (license/attribution), and RMA-193 (documentation). It does not claim physical-device acceptance for RMA-125, RMA-135, RMA-184, or the remaining RMA-190 scenarios.

## Accepted source head

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `949c0e61d4b2ab90228870c73579ebf8fa2ecde9`
- Workflow: `RMA Release Review Contracts`
- Run: `36658518119`
- Job: `109707913617`
- Result: **success**

The job completed successfully at 2026-09-30 02:28:30 UTC and exercised the release-review contracts against the exact source SHA above.

## RMA-191 — privacy and security review

The successful job ran all of the following before the canonical RMA-191 gate:

- RMA-161 credential-lifecycle contract, binding provider credentials to the Android-Keystore lifecycle and fail-closed invalidation behavior.
- RMA-162 private-media retention contract, binding raw camera/microphone/cloud-request media to no-retention defaults and controlled temporary cleanup.
- RMA-170 structured-logging contract, binding diagnostics to centralized redaction and raw-media exclusion.
- RMA-172 diagnostic-bundle contract, binding exports to redacted-only production selection and exclusion of credentials/raw media/transcripts/conversation text.
- RMA-195 fail-closed composition contract, preventing unavailable provider/camera/model paths from being represented as successful shipping behavior.
- Canonical RMA-191 security/privacy contract.
- RMA-191 compatibility guard.

Together these gates cover the RMA-191 review obligations: no embedded provider key acceptance, Keystore-backed credential handling, explicit network/cleartext policy, privacy-boundary authorization before cloud switching, redacted diagnostics/logging, temporary private-media cleanup, and native ABI/buffer/lifetime safety review. The review is repository-wide contract evidence; it does not replace already-separate physical credential acceptance where that task explicitly requires it.

## RMA-192 — license and attribution review

The successful job ran the RMA-012 in-app license contract and the RMA-192 license-review contract. The RMA-192 gate reconciles the selected local-model manifest, machine-readable third-party inventory, repository notices, offline in-app notices, Reachy-derived attribution/share-alike obligations, and no-endorsement disclosure. It also binds the selected Qwen3 0.6B Q4_K_M presentation to its Apache-2.0 license and externally supplied/not-bundled distribution state.

This is the release-review evidence for packaged dependency/asset reconciliation, repository/app notice parity, Reachy-derived attribution treatment, no-endorsement language, and selected-model redistribution/download presentation.

## RMA-193 — documentation review

The successful RMA-193 documentation contract verifies that maintained documentation covers:

- build instructions;
- Android device requirements and support floors;
- local-model installation/import;
- provider configuration and BYOK warning;
- calibration workflow;
- fidelity-level explanation;
- Camera Level 1 approximation and missing-pixel behavior;
- troubleshooting and diagnostic-bundle instructions; and
- privacy/data-flow explanation.

The contract also ties documented Android requirements to the RMA-184 support policy so API/architecture requirements cannot silently drift apart.

## Release-status guard

The same exact-head job also passed the RMA-194 release-status contract. That contract is intentionally conservative: it prevents the release status from claiming closure for device-dependent gates that remain physically unqualified. Passing it is not evidence that those physical gates are complete.

## Qualification boundary

This report closes only the review evidence represented by RMA-191, RMA-192, and RMA-193. It deliberately makes no new claim for:

- offline spoken conversation on a device with the required installed ASR/TTS services;
- local-LLM thermal/physics acceptance on the SM-A546E;
- RMA-184 measured default profiles or long-run representative-device qualification;
- RMA-190 scenarios not already backed by physical end-to-end evidence; or
- any device run attempted while the A54 is unavailable.

Those remain governed by their own acceptance evidence and must stay open until that evidence exists.
