# RMA-123 / speech release-gate status — 2026-09-28

## Scope

This record preserves the observed qualification boundary for the `ralph/rma123-tts-unity-context-v2` lane while consolidating release-gate evidence in PR #91.

It does not close RMA-190 or RMA-194 speech checkboxes, and it does not transplant source from the branch in this reconciliation commit.

## Observed branch

- Branch: `ralph/rma123-tts-unity-context-v2`
- Exact head: `cd5c204505d0021c8282fba3f1c4941bcff4f47b`

## Exact-head CI observed

The following GitHub Actions runs were observed for the exact branch head above:

- `35761444267` — `RMA-124 Android system/network TTS`, success
- `35761444201` — `RMA-121 Android on-device ASR`, success
- `35761444247` — `RMA-125 speech audio-focus state machine`, success
- `35761444208` — `RMA-122 Android system ASR`, success
- `35761444226` — `RMA-123 Android offline TTS`, success
- `35761444128` — `RMA-120 speech provider contracts`, success
- `35761444309` — generic `CI`, success

## Boundary

The successful runs predate the current generic-CI infrastructure outage and are exact-head evidence for the speech branch itself, not for PR #91's current head.

This record is intentionally evidence/status-only. It does not claim that Android on-device ASR plus offline TTS is release-complete, does not claim that the RMA-190 ASR -> local LLM -> behavior -> offline TTS scenario has passed on PR #91, and does not close RMA-194's `Android on-device ASR and offline TTS work where installed` item.

Further closure requires either safely merging/transplanting the source lane onto current `master` and qualifying that exact head, or rerunning equivalent physical/release-gate evidence after the generic CI and device-validation paths are operational.
