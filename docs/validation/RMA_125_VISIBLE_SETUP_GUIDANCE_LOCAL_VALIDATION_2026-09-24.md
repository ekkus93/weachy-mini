# RMA-125 Visible Offline Speech Setup Guidance Local Validation — 2026-09-24

## Scope

This note records local validation for the `ralph/rma125-visible-speech-setup-guidance` branch at commit `4ebbfc00691dec572d6dffff60cd55ca02ba26ae`.

The branch covers the RMA-125 sub-requirement that missing offline speech services produce visible setup guidance instead of silently falling back or hiding unavailable ASR/TTS capability. This is source/local validation evidence only. It does not claim that a physical Android device can complete the full offline spoken conversation loop.

## Local validation performed

From the uploaded repository checkout corresponding to current `master` plus the `ralph/rma125-visible-speech-setup-guidance` branch:

```text
python3 scripts/tests/test_rma125_visible_setup_guidance.py
# exit 0
```

The source contract verifies that:

- optional audio-health events are reported into the settings diagnostics path;
- unavailable TTS setup state remains visible to the user;
- ASR setup/model availability states are surfaced rather than hidden; and
- no network fallback is introduced for the offline speech path.

## Completion boundary

The broader RMA-125 physical acceptance remains open until an Android device with the required explicit on-device ASR support and installed offline TTS voice passes the full no-network conversation scenario on an exact commit. This validation note must not be used to close that physical acceptance checkbox.

## Hosted CI status

At validation time, GitHub Actions for this repository is failing jobs before any step starts, with zero job steps and unavailable logs. That condition is treated as CI infrastructure failure rather than source failure. The branch remains subject to exact-head hosted qualification before merge.
