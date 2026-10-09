# RMA-125 physical offline-speech acceptance blocker

**RMA:** 125  
**Status:** Hardware/API prerequisite cleared on A54; network-disabled spoken-conversation acceptance still open  
**Recorded:** 2026-08-07  
**Updated:** 2026-10-08

## Summary

The RMA-125 microphone/audio-focus implementation is code-complete and its deterministic and hosted Android gates pass on implementation SHA `e4580072edcec6a19dd73bcbe215a8476c11abbe`. The remaining TODO acceptance is a positive physical conversation test using RMA-121 explicit on-device ASR and RMA-123 offline TTS with networking disabled.

The original blocker was the then-current self-hosted physical regression device: an LG-H872 running Android 8.0/API 26. RMA-121 intentionally requires the Android API-31 explicit on-device recognition APIs:

- `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)`;
- `SpeechRecognizer.createOnDeviceSpeechRecognizer(Context)`.

`Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachyOnDeviceAsrBridge.java` rejects API levels below 31 with an actionable unavailable result. This is a locality requirement, not an incidental implementation restriction.

The application-wide Android deployment floor remains API 26. RMA-125's audio-focus implementation and the rest of the application continue to package for that floor. Only the positive RMA-121 explicit on-device ASR path requires API 31 or newer.

## Updated hardware prerequisite state

The historical API-26-device prerequisite is now cleared for the current self-hosted Android runner: the pinned physical Samsung Galaxy A54 (`SM-A546E`) is API-31+ and has repeatedly passed the no-artifacts A54 validation lane.

Exact-head revalidation evidence is recorded in `docs/validation/RMA_125_A54_SPEECH_CAPABILITY_REVALIDATION_2026-09-30.md`. On source SHA `35928d183fb48d12d228fdd4b8a2f1515db6bb0e`, workflow run `36752801658`, job `110015411312`, passed both the `RMA-125 A54 speech revalidation contract` step and the RMA-195 application-composition stage. The physical RMA-195 stage requires explicit on-device ASR availability and an installed exact-locale offline TTS voice that does not require network, and it rejects human transcript injection, cloud credentials, and local-model injection.

This updated evidence clears only the device/API/service-readiness prerequisite. It does **not** prove the network-disabled spoken-conversation acceptance sequence below.

## Evidence already accepted

On exact implementation SHA `e4580072edcec6a19dd73bcbe215a8476c11abbe`:

- dedicated RMA-125 run `31197919169` passed the warnings-as-errors managed build and all 16 deterministic speech/audio-focus contracts;
- hosted repository CI run `31197919381` passed Android lint, Java `-Xlint:all -Werror`, Android compilation/tests, managed, native, static, and Reachy-model gates;
- self-hosted run `31197920546`, job `92930627177`, successfully built and verified the ARM64 API-26 APK, pinned the physical device, and passed RMA-090 and RMA-091 before stopping at the pre-existing RMA-092 camera-texture acceptance failure.

That self-hosted run demonstrates API-26 packaging compatibility. It does not and cannot demonstrate RMA-121 positive on-device recognition. The later A54 runs demonstrate positive service capability/readiness but still do not demonstrate a spoken user utterance with networking disabled.

## Prohibited workaround

Do **not** close this blocker by:

- substituting RMA-122 Android system ASR;
- setting `RecognizerIntent.EXTRA_PREFER_OFFLINE` and treating it as proof of locality;
- choosing a network-capable recognition or TTS provider automatically;
- raising the application's global minSdk merely to make the acceptance device match RMA-121;
- claiming emulator/deterministic tests as microphone-and-speaker physical acceptance;
- claiming A54 service-capability probes as a real network-disabled spoken conversation;
- marking RMA-125 complete without the physical evidence required by the master TODO.

RMA-121 and RMA-125 are intentionally fail-closed at this boundary.

## Exit conditions

The platform/service prerequisite is cleared by the A54 evidence above. The blocker is cleared only when one physical Android device also satisfies the full positive acceptance sequence:

1. ARM64 Android device running API 31 or newer.
2. `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)` returns true.
3. The target BCP-47 recognition language is installed/supported by the explicit on-device recognition service.
4. `RECORD_AUDIO` permission is granted for the test application.
5. An exact-locale RMA-123 TTS voice is installed and reports that it does not require a network connection.
6. The device can run the acceptance with Wi-Fi and cellular data unavailable/disabled and with the offline state captured as evidence.
7. A real user utterance travels through the shipping RMA-121 recognizer and a response travels through the shipping RMA-123 offline TTS voice without activating RMA-122, RMA-124, OpenAI, or any cloud provider.

Running `bash scripts/run_rma125_speech_device_preflight.sh` proves only item 1 and physical-device/ABI suitability. The current A54 application-composition evidence additionally proves items 2 and 5 for the pinned device. The network-disabled spoken-conversation acceptance remains authoritative for items 3, 4, 6, and 7.

## Required physical acceptance sequence

On the eligible A54 device, capture evidence for this exact sequence:

1. Start the offline-default speech stack; verify it contains RMA-121 plus RMA-123 and no RMA-122/RMA-124 fallback.
2. With networking disabled, acquire the listening audio-focus lease and speak a real utterance into the device microphone.
3. Record the RMA-121 started/final event sequence and the final transcript without persisting raw microphone audio.
4. Prove the listening lease is released before the RMA-123 speaking lease is acquired.
5. Synthesize an audible response through the installed exact-locale offline voice and record the TTS started/done lifecycle.
6. Prove the microphone is not concurrently active while TTS owns the speech lease and that the synthesized response is not accepted as a new user utterance.
7. Exercise at least one representative Android interruption or route transition and prove the active operation terminates visibly rather than resuming or continuing silently.
8. Exercise missing/unavailable local ASR or offline TTS data and prove the UI/provider result supplies actionable setup guidance without selecting a network-capable provider.
9. Record device model, Android/API version, ASR service/model identity where Android exposes it safely, TTS engine/voice identity, network-disabled evidence, callback/focus sequence, APK SHA-256, implementation SHA, and sanitized relevant logs.

Only after those checks pass should the RMA-125 implementation and offline-speech acceptance boxes in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` be checked.

## Next experiment

The application-level spoken-turn harness is now implemented as
`ReachyRma190OfflineSpeechEndToEndAcceptance`,
`scripts/run_rma190_offline_speech_end_to_end_android.sh`, and the manual
`RMA-190 Offline Speech End-to-End Android Acceptance` workflow. It installs
the exact RMA-133-selected Qwen artifact through the production
`LocalModelPackageManager`, selects the shipping on-device provider preferences,
disables Wi-Fi/mobile data plus airplane mode at the device boundary, invokes
the real `ReachyMainScreen.RequestMicrophone()` path, and requires a human
utterance. The runner restores the prior network state unconditionally. It never
injects a transcript, records microphone audio, or supplies cloud credentials.

Run that manual workflow while a person is physically present at the A54 and
speak the requested utterance. A successful spoken turn can establish the
positive conversation leg, but the full RMA-125 blocker still also requires the
representative interruption/route transition and missing-service/setup-guidance
evidence listed above. Do not close the three authoritative RMA-125 acceptance
checkboxes until all required physical evidence exists.

If the explicit recognizer, language model, or exact-locale offline voice becomes unavailable, preserve the resulting setup-required evidence and configure/install the required local service/model/voice; do not change providers automatically.

## A54 reconnected regression evidence — 2026-10-08

On exact `master` SHA `709c01ddadc78b21246a00ae54dcbd8c0e3892fe`, `Local Unity Android Validation` run `37720761063` passed physical device pinning and the RMA-195 application-composition acceptance after the A54 was reconnected. This confirms the device is available again for subsequent RMA-125 acceptance work. The run did not exercise a real network-disabled human ASR utterance through offline TTS with the required audio-focus sequence, so it does not close any of the three open RMA-125 acceptance checkboxes. The next experiment remains the dedicated shipping-path spoken-turn acceptance above.

## Current device-availability blocker — 2026-10-08

At exact source head `f57184efa7afda0e26904c9549ebbbb3175fa86a`,
`Local Unity Android Validation` run `37873906455` passed
Unity edit/play test execution and production MuJoCo staging, then failed
at `Pin one physical Android device`. The companion no-artifacts physical
workflow `37873906339` failed at its device pinning step. The pinned
Samsung A54 was not enumerated as an approved, connected ADB device.
Neither failure is evidence of a regression in shipping ASR/TTS or
a failed human utterance; neither is positive offline-speech acceptance.
Hosted CI `37873906378`, release-review contracts `37873906423`,
and RMA-173 regressions `37873906349` all passed on that exact head.

The no-artifacts lane now pins the phone before its APK build, and the
standard Unity lane pins it after independent Unity tests but before
its APK build. Source and non-device validation can continue without
the phone. When ADB authorization/connection is restored, run the
already documented real-user network-disabled speech acceptance sequence;
a capability probe alone still does not close this item.
