# RMA-125 physical offline-speech acceptance blocker

**RMA:** 125  
**Status:** Blocked on an API-31+ physical Android device with installed explicit on-device ASR and offline TTS data  
**Recorded:** 2026-08-07

## Summary

The RMA-125 microphone/audio-focus implementation is code-complete and its deterministic and hosted Android gates pass on implementation SHA `e4580072edcec6a19dd73bcbe215a8476c11abbe`. The remaining TODO acceptance is a positive physical conversation test using RMA-121 explicit on-device ASR and RMA-123 offline TTS with networking disabled.

The current self-hosted physical regression device cannot satisfy that test. It is an LG-H872 running Android 8.0/API 26. RMA-121 intentionally requires the Android API-31 explicit on-device recognition APIs:

- `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)`;
- `SpeechRecognizer.createOnDeviceSpeechRecognizer(Context)`.

`Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachyOnDeviceAsrBridge.java` rejects API levels below 31 with an actionable unavailable result. This is a locality requirement, not an incidental implementation restriction.

The application-wide Android deployment floor remains API 26. RMA-125's audio-focus implementation and the rest of the application continue to package for that floor. Only the positive RMA-121 explicit on-device ASR path requires API 31 or newer.

## Evidence already accepted

On exact implementation SHA `e4580072edcec6a19dd73bcbe215a8476c11abbe`:

- dedicated RMA-125 run `31197919169` passed the warnings-as-errors managed build and all 16 deterministic speech/audio-focus contracts;
- hosted repository CI run `31197919381` passed Android lint, Java `-Xlint:all -Werror`, Android compilation/tests, managed, native, static, and Reachy-model gates;
- self-hosted run `31197920546`, job `92930627177`, successfully built and verified the ARM64 API-26 APK, pinned the physical device, and passed RMA-090 and RMA-091 before stopping at the pre-existing RMA-092 camera-texture acceptance failure.

That self-hosted run demonstrates API-26 packaging compatibility. It does not and cannot demonstrate RMA-121 positive on-device recognition.

## Prohibited workaround

Do **not** close this blocker by:

- substituting RMA-122 Android system ASR;
- setting `RecognizerIntent.EXTRA_PREFER_OFFLINE` and treating it as proof of locality;
- choosing a network-capable recognition or TTS provider automatically;
- raising the application's global minSdk merely to make the acceptance device match RMA-121;
- claiming emulator/deterministic tests as microphone-and-speaker physical acceptance;
- marking RMA-125 complete without the physical evidence required by the master TODO.

RMA-121 and RMA-125 are intentionally fail-closed at this boundary.

## Exit conditions

The blocker is cleared only when one physical Android device is available that meets all of these preconditions:

1. ARM64 Android device running API 31 or newer.
2. `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)` returns true.
3. The target BCP-47 recognition language is installed/supported by the explicit on-device recognition service.
4. `RECORD_AUDIO` permission is granted for the test application.
5. An exact-locale RMA-123 TTS voice is installed and reports that it does not require a network connection.
6. The device can run the acceptance with Wi-Fi and cellular data unavailable/disabled and with the offline state captured as evidence.

Running `bash scripts/run_rma125_speech_device_preflight.sh` proves only item 1 and physical-device/ABI suitability. The application-level provider probes remain authoritative for items 2-5.

## Required physical acceptance sequence

On an eligible device, capture evidence for this exact sequence:

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

Attach or provision one physical ARM64 Android API-31+ device to the `weachy-mini-android-device` self-hosted runner, then run:

```bash
bash scripts/run_rma125_speech_device_preflight.sh
```

If the preflight passes, implement/run the application-level RMA-125 offline conversation acceptance on that device. If the explicit recognizer or exact-locale offline voice is unavailable, preserve the resulting setup-required evidence and configure/install the required local service/model/voice; do not change providers automatically.
