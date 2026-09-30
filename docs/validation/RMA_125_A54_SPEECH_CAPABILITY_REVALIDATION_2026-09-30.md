# RMA-125 A54 speech capability revalidation — 2026-09-30

## Scope

This report updates the physical-device prerequisite state for RMA-125 after the Samsung Galaxy A54 (`SM-A546E`) became the pinned self-hosted Android acceptance device. It does **not** claim the remaining network-disabled spoken-conversation acceptance.

## Exact-head evidence

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `2f0f40c909034b65e25526e69d66816c203b6210`
- Workflow: `Local Unity Android Device Validation No Artifacts`
- Run: `36741114024`
- Job: `109975440245`
- Result: **success**

The workflow's RMA-195 application-composition stage runs `scripts/run_rma195_application_composition_acceptance_android.sh`. That harness is pinned to the physical Samsung A54 `SM-A546E`, requires ARM64 and API 31 or newer, grants the microphone permission, and exercises the shipping application composition rather than a test-only speech substitute.

The physical acceptance code requires `ProbeOnDeviceAsrAsync()` to report explicit on-device ASR `Available`; otherwise the stage fails. It separately exercises the shipping offline TTS path and requires an installed exact-locale voice whose `MayUseNetwork` flag is false, plus successful TTS start and completion. The report validator rejects a passing result unless `on_device_asr_state == "Available"`, `offline_tts_state == "Available"`, the selected voice is installed, `selected_tts_voice_may_use_network == false`, and TTS started and completed.

The harness also records and requires that it injected no human ASR transcript, cloud credentials, or local model. Therefore this evidence establishes speech-service **capability/readiness** on the A54 without fabricating a recognized utterance or provider success.

## Blocker correction

The historical blocker in `docs/validation/RMA_125_SPEECH_AUDIO_FOCUS_VALIDATION_2026-08-07.md` and `docs/blockers/RMA-125_PHYSICAL_OFFLINE_SPEECH_BLOCKER.md` was specifically that the then-current LG-H872/API-26 phone could not execute RMA-121's API-31 explicit on-device recognizer. That hardware prerequisite is now stale: the A54 is API-31+ and the exact-head physical composition gate proves that explicit on-device ASR and a non-network offline TTS voice are both available on the current device.

This does **not** close RMA-125. The authoritative TODO correctly requires stronger end-to-end evidence than service availability.

## Remaining RMA-125 physical acceptance

The following still require a dedicated physical acceptance run before the three RMA-125 offline-speech criteria can be checked:

1. Disable Wi-Fi and mobile data for the acceptance window and record the disabled network state.
2. Acquire listening focus through the shipping RMA-121 on-device ASR path and obtain a real human utterance result; do not inject a transcript.
3. Release listening focus before acquiring speaking focus.
4. Speak the resulting response through the installed exact-locale RMA-123 offline TTS voice and prove the microphone is not simultaneously transcribing the synthesized speech.
5. Exercise at least one representative interruption/route transition and require visible cancellation rather than silent continuation.
6. Exercise an unavailable recognition service/model or offline voice state and require visible setup guidance without activating RMA-122/RMA-124 or a cloud provider.
7. Record the exact source SHA, device/API, speech service/model, TTS engine/voice, network-disabled evidence, focus/callback sequence, APK hash, and sanitized logs.

Until those conditions pass, the RMA-125 acceptance checkboxes and the corresponding spoken RMA-190/RMA-194 release claims remain open.
