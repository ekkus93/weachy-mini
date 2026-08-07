# RMA-125 speech audio-focus validation

**Status:** Implementation gates accepted on `e4580072edcec6a19dd73bcbe215a8476c11abbe`; physical offline-speech acceptance blocked on current API-26 device  
**Date:** 2026-08-07

RMA-125 adds the shared microphone/audio-focus state machine required to coordinate Android ASR and TTS. The implementation is intentionally fail-closed and preserves the explicit provider/privacy boundaries established by RMA-120 through RMA-124.

## Accepted implementation contract

The implementation requires all of the following:

1. One speech-audio lease at a time; listening and speaking never overlap and are not queued.
2. Listening and speaking acquire Android audio focus before the selected provider begins.
3. ASR/TTS terminal events are not exposed until the exact focus lease has been released.
4. Permanent focus loss, transient loss, duck requests, route changes, becoming-noisy events, phone/communication modes, and microphone mute events cancel the active operation visibly where Android exposes them.
5. Focus gain after interruption never resumes an utterance automatically.
6. Stale callback session IDs cannot cancel the current operation.
7. Focus release failure becomes a visible failure and leaves the coordinator faulted until recreation.
8. The single phone microphone is explicitly represented as one non-concurrent capture path.
9. The Android bridge uses API-26 `AudioFocusRequest`, transient-exclusive focus for listening, transient focus for speaking, no delayed focus, exact abandonment, and no legacy focus API.
10. No `READ_PHONE_STATE`, call-log permission, telephony inspection, automatic foreground-service launch, provider fallback, automatic retry, or network promotion is introduced.
11. The offline-default application stack wires RMA-121 explicit on-device ASR and RMA-123 offline TTS only.
12. Existing RMA-121/RMA-123 missing-service/setup failures remain visible instead of redirecting to RMA-122/RMA-124.

The deterministic suite contains 16 cases and requires no Android device, microphone, speaker, speech service, network connection, API key, or stored speech payload.

## Exact-SHA implementation evidence

The accepted implementation SHA is `e4580072edcec6a19dd73bcbe215a8476c11abbe`.

- Dedicated RMA-125 workflow run `31197919169` completed successfully. The managed core warnings-as-errors build and all 16 deterministic RMA-125 contracts passed.
- Hosted repository CI run `31197919381` completed successfully on the same SHA. Android lint, Java 17 `-Xlint:all -Werror`, Android compilation/tests, managed tests, native/sanitizer tests, static checks, and Reachy-model checks passed.
- Self-hosted Unity/Android run `31197920546`, job `92930627177`, reached and passed Unity tests, ARM64 API-26 APK build/verification, physical-device pinning, RMA-090, and RMA-091 on the same SHA. The broader legacy workflow then failed at the pre-existing RMA-092 camera-texture acceptance step, so later unrelated physical gates were skipped. RMA-125 is not described as having a completely green self-hosted legacy run.

The successful self-hosted steps establish that the new RMA-125 Android bridge compiles in Unity and packages into the supported API-26 ARM64 application. They do not establish positive on-device speech recognition.

No analyzer suppression, lint baseline, permission expansion, focus-delay queue, retry loop, threshold relaxation, foreground-service workaround, or provider fallback was added to obtain these results.

## Physical offline-speech acceptance blocker

Repository CI and the standard physical camera/lifecycle regression do not prove the TODO's positive offline speech acceptance. RMA-125 is not fully closed until a device with suitable installed Android services demonstrates the offline conversation path with networking disabled.

The current physical regression device is an LG-H872 running Android 8.0/API 26. RMA-121 deliberately requires the API-31 explicit on-device speech-recognition APIs `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)` and `SpeechRecognizer.createOnDeviceSpeechRecognizer(Context)`. Its production bridge fails visibly below API 31 instead of using the ambiguous system recognizer.

Therefore the current phone cannot legitimately perform the remaining positive RMA-125 acceptance. Substituting RMA-122 system ASR, `EXTRA_PREFER_OFFLINE`, or any network-capable provider would invalidate the locality requirement rather than clear it.

The application-wide deployment floor remains API 26. This blocker applies only to positive RMA-121 on-device recognition; it is not a reason to raise the global minSdk.

The formal blocker, prohibited workarounds, exit conditions, and next experiment are recorded in `docs/blockers/RMA-125_PHYSICAL_OFFLINE_SPEECH_BLOCKER.md`.

## Device preflight

`scripts/run_rma125_speech_device_preflight.sh` is the fail-closed first step for the next physical device. It:

- requires exactly one selected/connected physical device;
- requires ARM64 and Android API 31 or newer;
- records model, Android release/API, ABI, and only a SHA-256 of the adb serial in its JSON evidence;
- exits nonzero when the platform floor is not met;
- explicitly does **not** claim that the on-device ASR service, language model, offline TTS voice, network-disabled state, or RMA-125 end-to-end acceptance has been proven.

After this preflight passes, the RMA-121 and RMA-123 runtime availability probes remain authoritative.

## Physical offline-speech acceptance still required

On an eligible device with networking disabled, capture evidence that:

1. RMA-121 explicit on-device ASR acquires listening focus and returns a real utterance result;
2. listening focus is released before RMA-123 offline TTS acquires speaking focus;
3. the utterance is audibly synthesized using an installed exact-locale offline voice;
4. the microphone is not active during TTS, so the synthesized response is not transcribed as user speech;
5. a representative interruption/route transition produces a visible cancellation rather than silent continuation;
6. an unavailable recognition service/model or offline TTS voice produces visible setup guidance and does not select a network-capable provider.

The exact device, Android version, ASR service/model, TTS engine/voice, network-disabled evidence, callback/focus sequence, APK SHA-256, implementation SHA, and sanitized relevant logs must be recorded before checking the RMA-125 implementation/offline-speech acceptance boxes in the master TODO.

Until that evidence exists, RMA-125 remains physically blocked and Phase 14/RMA-130 should not be treated as the next completed ordered roadmap task unless the roadmap order is explicitly waived.
