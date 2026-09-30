# RMA-173 / RMA-191 / RMA-192 / RMA-193 TODO Reconciliation Evidence — 2026-09-30

This evidence record captures exact-head validation that is newer than the stale unchecked sections in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.

## Exact commit

- Repository: `ekkus93/weachy-mini`
- Exact commit: `06a24789361a459a41ed07b683059c349b1fa689`
- Branch at validation time: `master`

## RMA-173 silent-failure regressions

Workflow run `36756177634` (`RMA-173 Silent Failure Regressions`) completed successfully on the exact commit above. Job `110026869548` passed all eight fail-closed contract steps:

1. on-device ASR failure cannot activate cloud ASR;
2. offline TTS failure cannot activate a network voice;
3. local LLM failure cannot activate OpenAI;
4. invalid camera pixels cannot reuse stale content;
5. solver failure cannot switch to Unity animation;
6. calibration mismatch cannot load placeholders as calibrated;
7. dropped command or state buffers produce visible errors;
8. malformed provider response cannot produce a fake successful intent.

The authoritative TODO still shows these eight RMA-173 boxes unchecked at this commit. They are stale relative to the successful exact-head workflow evidence above.

## RMA-191 security and privacy review

Workflow run `36756177544` (`RMA Release Review Contracts`) completed successfully on the exact commit above. Its release-review job passed:

- `Run canonical RMA-191 security/privacy contract`
- `Run RMA-191 compatibility guard`

The no-artifacts A54 workflow run `36756177401` also passed `Run RMA-191 security-review contract` before the physical Android acceptance stack.

The contracts cover no embedded production credentials, Android Keystore-backed secret storage, network-security and cleartext restrictions, visible provider disclosure before cloud-bound audio/image/text, diagnostics redaction, temporary private-media cleanup, and native handle/buffer/session lifetime safety.

The authoritative TODO still shows the RMA-191 checklist unchecked at this commit. It is stale relative to the successful exact-head contract evidence above.

## RMA-192 license and attribution review

Workflow run `36756177544` passed `Run RMA-192 license-review contract` on the exact commit above. The no-artifacts A54 workflow run `36756177401` also passed `Run RMA-192 license-review contract` before the physical Android acceptance stack.

The contract reconciles packaged dependency/asset inventory, repository and in-app notices, Reachy-derived attribution/share-alike/noncommercial obligations, no-endorsement disclosure, and selected local-model redistribution/download presentation.

The authoritative TODO still shows the RMA-192 checklist unchecked at this commit. It is stale relative to the successful exact-head contract evidence above.

## RMA-193 release documentation review

Workflow run `36756177544` passed `Run RMA-193 documentation contract` on the exact commit above. The no-artifacts A54 workflow run `36756177401` also passed `Run RMA-193 documentation contract` before the physical Android acceptance stack.

The contract covers the release guide, build instructions, Android device requirements, local model installation/import, provider configuration and BYOK warning, calibration workflow, fidelity levels, Camera Level 1 approximation and missing-pixel behavior, troubleshooting/diagnostic-bundle instructions, and privacy/data-flow explanation.

The authoritative TODO still shows the RMA-193 checklist unchecked at this commit. It is stale relative to the successful exact-head contract evidence above.

## Related exact-head Android evidence

Workflow run `36756177401` (`Local Unity Android Device Validation No Artifacts`) completed successfully on the exact commit above. Its A54 job passed Unity tests, ARM64 API-26 APK build and verification, device pinning, RMA-190 core end-to-end acceptance, RMA-090/RMA-091/RMA-092 camera acceptance, RMA-111 lightweight tracking, RMA-154 visual-servo acceptance, RMA-022 lifecycle acceptance, RMA-195 application-composition acceptance, and authoritative rendering acceptance.

The ordinary Local Unity Android Validation run `36756177575` failed later at artifact publication (`Upload RMA-090 camera discovery evidence`). The no-artifacts run proves the device path at the same exact source head; the artifact-publication failure is infrastructure, not a source-code or device-acceptance failure.

## Remaining open evidence gaps

This reconciliation evidence does not close release acceptance. The following remain open in the authoritative roadmap unless separately evidenced and reconciled:

- RMA-002 second clean developer environment build evidence;
- RMA-125 full offline spoken-conversation acceptance on a device with suitable installed services;
- RMA-135 representative on-device local-LLM thermal/physics acceptance, subject to the documented SM-A546E thermal limitation;
- RMA-184 measured representative-device default profiles and acceptance criteria;
- RMA-190 scenarios beyond the already accepted core offline-launch, MuJoCo neutral reset, and deterministic gesture replay path;
- RMA-194 release-candidate acceptance, final label, and completion record.
