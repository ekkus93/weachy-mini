# RMA-125 speech audio-focus validation

**Status:** Candidate implementation; exact-SHA evidence pending  
**Date:** 2026-08-07

RMA-125 adds the shared microphone/audio-focus state machine required to coordinate Android ASR and TTS. The implementation is intentionally fail-closed and preserves the explicit provider/privacy boundaries established by RMA-120 through RMA-124.

## Candidate contract

The candidate requires all of the following:

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

## Evidence required before implementation acceptance

The exact implementation SHA must pass:

- `.github/workflows/rma125-speech-audio-focus.yml`, including the managed core warnings-as-errors build and all deterministic RMA-125 contracts;
- normal hosted repository CI, including Java 17 `-Xlint:all -Werror`, Android lint warnings-as-errors, Android assembly, native/managed tests, and packaging checks triggered by the Android bridge;
- the complete self-hosted Unity/API-26 regression when triggered for the implementation SHA, proving the new bridge packages without regressing the supported application path.

No analyzer suppression, lint baseline, permission expansion, focus-delay queue, retry loop, threshold relaxation, foreground-service workaround, or provider fallback may be added merely to make these gates pass.

## Physical offline-speech acceptance remains separate

Repository CI and the standard physical camera/lifecycle regression do not prove the TODO's positive offline speech acceptance. RMA-125 is not fully closed until a device with suitable installed Android services demonstrates, with networking disabled:

1. RMA-121 explicit on-device ASR acquires listening focus and returns a real utterance result;
2. listening focus is released before RMA-123 offline TTS acquires speaking focus;
3. the utterance is audibly synthesized using an installed exact-locale offline voice;
4. the microphone is not active during TTS, so the synthesized response is not transcribed as user speech;
5. a representative interruption/route transition produces a visible cancellation rather than silent continuation;
6. an unavailable recognition service/model or offline TTS voice produces visible setup guidance and does not select a network-capable provider.

The exact device, Android version, ASR service/model, TTS engine/voice, network-disabled evidence, callback sequence, and relevant logs must be recorded before checking the two RMA-125 offline-speech acceptance boxes in the master TODO.
