# RMA-191 / RMA-192 / RMA-193 release-review validation — 2026-09-29

## Scope

This report records exact-head non-device release-review evidence for RMA-191 (privacy/security), RMA-192 (license/attribution), and RMA-193 (documentation). It does not claim physical-device acceptance for RMA-125, RMA-135, RMA-184, or the remaining RMA-190 scenarios.

## Accepted source head

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `35928d183fb48d12d228fdd4b8a2f1515db6bb0e`
- Workflow: `RMA Release Review Contracts`
- Run: `36752801891`
- Result: **success**

The release-review workflow completed successfully on 2026-09-30 and exercised the canonical RMA-191/RMA-192/RMA-193/RMA-194 contracts against the exact source SHA above. This supersedes the earlier accepted exact-head run `36712522030` on `a6f1714b15431da6d6b195774d3d4b3bd7d972e6` with fresher evidence after the RMA-125 A54 revalidation guard was added to the physical lane.

## RMA-191 — privacy and security review

The successful job exercises the release-review security/privacy contracts covering:

- RMA-161 credential lifecycle, binding provider credentials to Android-Keystore lifecycle and fail-closed invalidation behavior;
- RMA-162 private-media retention, binding raw camera/microphone/cloud-request media to no-retention defaults and controlled temporary cleanup;
- RMA-170 structured logging, binding diagnostics to centralized redaction and raw-media exclusion;
- RMA-172 diagnostic-bundle export, binding exports to redacted-only production selection and exclusion of credentials/raw media/transcripts/conversation text;
- RMA-195 fail-closed composition, preventing unavailable provider/camera/model paths from being represented as successful shipping behavior;
- the canonical RMA-191 security/privacy contract and compatibility guard.

Together these gates cover the RMA-191 review obligations: no embedded provider key acceptance, Keystore-backed credential handling, explicit network/cleartext policy, privacy-boundary authorization before cloud switching, redacted diagnostics/logging, temporary private-media cleanup, and native ABI/buffer/lifetime safety review. The review is repository-wide contract evidence; it does not replace separately required physical credential acceptance.

## RMA-192 — license and attribution review

The successful release-review job exercises the RMA-012 in-app license contract and RMA-192 license-review contract. The gate reconciles the selected local-model manifest, machine-readable third-party inventory, repository notices, offline in-app notices, Reachy-derived attribution/share-alike obligations, and no-endorsement disclosure. It also binds the selected Qwen3 0.6B Q4_K_M presentation to its Apache-2.0 license and externally supplied/not-bundled distribution state.

This is release-review evidence for packaged dependency/asset reconciliation, repository/app notice parity, Reachy-derived attribution treatment, no-endorsement language, and selected-model redistribution/download presentation.

## RMA-193 — documentation review

The successful RMA-193 documentation contract verifies maintained documentation covers:

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

## Exact-head companion evidence

On the same source SHA `35928d183fb48d12d228fdd4b8a2f1515db6bb0e`:

- `RMA-173 Silent Failure Regressions` run `36752801842` passed.
- A54-backed `Local Unity Android Device Validation No Artifacts` run `36752801658` passed.
- The A54 lane passed the new `Run RMA-125 A54 speech revalidation contract` step before Unity/device execution, permanently guarding the now-established API-31+ on-device-ASR/offline-TTS capability prerequisite.

These companion runs strengthen release-review context without changing the qualification boundary below. In particular, the A54 run is real physical-device evidence but is not treated as proof of the still-open full offline spoken conversation, local-LLM thermal, or representative-device long-run gates.

## Release-status guard

The same exact-head release-review job passed the RMA-194 release-status contract. That contract is intentionally conservative: it prevents release status from claiming closure for device-dependent gates that remain physically unqualified. Passing it is not evidence that those physical gates are complete.

## Qualification boundary

This report closes only the review evidence represented by RMA-191, RMA-192, and RMA-193. It deliberately makes no new claim for:

- a complete network-disabled human-spoken conversation through shipping on-device ASR, conversation, behavior, and offline TTS;
- local-LLM thermal/physics acceptance on the SM-A546E;
- RMA-184 measured default profiles or long-run representative-device qualification;
- RMA-190 scenarios not already backed by physical end-to-end evidence; or
- any other physical claim not explicitly exercised by the cited exact-head device workflow.

Those remain governed by their own acceptance evidence and must stay open until that evidence exists.
