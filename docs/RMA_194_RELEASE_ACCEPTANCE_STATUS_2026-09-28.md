# RMA-194 release acceptance status — 2026-09-28

## Purpose

This document maps the still-open RMA-194 release acceptance checklist items to current `master` evidence, pending exact-head validation, and remaining blockers. It is a planning/status artifact only. It does **not** close any checkbox in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.

Current status baseline: `master` after the current reconciliation series, including the Unity/Android thermal guardrails, RMA-002/RMA-184/RMA-190/RMA-191/RMA-192/RMA-193 boundary records, and the transplanted RMA-190 permission harness.

## Current merge/qualification boundary

Several hosted workflows still fail with the repository-wide zero-step/no-log `JOB_NOT_FOUND` signature. Those failures are classified as CI infrastructure failures until a workflow exposes failed steps/logs or another actionable source diagnostic.

Long-running self-hosted Android validation lanes may remain queued while the runner is busy or unavailable. Queued status is not a pass and is not a source failure.

## RMA-194 item map

| RMA-194 item | Current `master` evidence | Status | Blocker before checkbox closure |
| --- | --- | --- | --- |
| App launches and runs offline on supported hardware. | Historical RMA-190 core/offline evidence is documented in validation records; current master has not yet produced fresh exact-head offline-launch success. | Open. | Exact-head physical Android run on supported hardware, then TODO reconciliation. |
| Android on-device ASR and offline TTS work where installed. | RMA-123/TTS and RMA-125 speech evidence-boundary/status records exist. Network-disabled speech and full spoken-turn closure remain separate. | Open. | Non-fabricated ASR -> local LLM -> behavior -> offline TTS evidence, preferably with networking disabled where required. |
| Selected benchmark-backed local LLM works without blocking physics. | RMA-134 local-LLM and RMA-184/RMA-180 performance status records exist; RMA-184 detailed measured profiles remain open. | Open. | Exact-head current-master validation and representative-device performance/profile reconciliation. |
| OpenAI and compatible providers are independently configurable. | RMA-190 provider-combination managed contracts are now preserved on current `master`; real cloud-provider success is not claimed. | Open. | Exact-head CI/provider validation and any intentionally supplied endpoint/credential evidence if a real cloud success claim is needed. |
| Behavior planner validates all AI output. | RMA-195 composition history and provider/failure boundaries preserve the behavior-intent validation design. | Open. | Exact-head test evidence that the full current path remains intact; final release reconciliation. |
| No silent provider, privacy, calibration, or kinematic fallbacks remain. | RMA-190 network/model/solver/deterministic-failure boundaries, RMA-191 security/privacy gate, and fail-closed permission harness are on current `master`. | Open. | Exact-head CI/security/privacy/failure-mode validation; no redacted source gaps for the closure claim. |
| Diagnostics identify active fidelity, providers, and failures. | RMA-170/RMA-172 references are enforced by the RMA-191 gate; RMA-193 guide documents diagnostic-bundle behavior. | Open. | Exact-head CI and final release audit that diagnostics remain wired to active fidelity/provider/failure state. |
| Representative-device performance report exists. | RMA-184 A54 profile-summary boundary is preserved, but detailed rows are not reconstructed from redacted output. | Open. | Safely readable detailed measured profiles and supported-device target reconciliation. |
| Licenses and attribution are complete. | RMA-192 review boundary is preserved, but inventory/notices were not transplanted because `third_party/inventory.json` was redacted. | Open. | Full readable inventory/notices/source transplant and exact-head license-review validation. |

## Release-gate blockers

1. Generic CI and several status-publisher workflows continue to show zero-step/`JOB_NOT_FOUND` infrastructure failures.
2. Full RMA-125/RMA-190 spoken-turn evidence remains missing: on-device ASR utterance -> local LLM -> behavior -> offline TTS with the required offline/network-disabled conditions, or an explicitly accepted non-fabricated ASR acceptance method.
3. RMA-002 still requires second independently provisioned Unity/Android build evidence.
4. RMA-184 still needs detailed measured default profiles and representative-device target reconciliation.
5. RMA-192 still needs full readable inventory/notices/source changes; partial reconstruction from redacted inventory output is not acceptable.
6. TODO checkbox closure must wait for exact current-master evidence, not merely branch history or status notes.

## Non-blocker classifications

- Artifact upload quota failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload.
- Zero-step CI failures with unavailable logs are infrastructure failures, not source failures.
- A physical Android run blocked by secure keyguard or device disconnection is a hardware availability condition, not a source failure.
- Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.

## Current-policy reminder

This file intentionally preserves the release-gate boundary. It should fail review if used to claim checklist closure without the corresponding exact-head evidence and authoritative TODO reconciliation.
