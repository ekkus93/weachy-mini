# RMA-124 Android system/network TTS validation

**Status:** Candidate implementation; exact-SHA evidence pending  
**Date:** 2026-08-07

RMA-124 adds Android system/network `TextToSpeech` as an explicit `DeviceService + ProviderControlled` TTS provider. It preserves per-voice network-required status and prohibits automatic selection of network-required voices.

## Candidate contract

The candidate implementation requires:

- exact provider/request identity;
- exact BCP-47 locale and exact Android voice identity;
- per-voice `None` versus `Required` network classification;
- an exact provider-instance network-voice selection before any network-required voice may synthesize;
- a second explicit `networkVoiceApproved` check in the Java bridge;
- no network voice auto-selection when offline TTS is unavailable;
- no `setLanguage()` closest-match fallback;
- no alternate voice/provider/cloud fallback;
- cancellation, timeout, busy/no-queue, request identity, visible terminal errors, and deterministic teardown;
- no automatic utterance retry.

The permanent deterministic suite contains 27 RMA-124 contracts and requires no Android TTS engine, speaker, network, API key, or stored speech payload.

## Evidence pending

Do not treat RMA-124 as accepted until all of the following pass on the exact implementation SHA:

1. `.github/workflows/rma124-android-system-tts.yml`, including the managed core warnings-as-errors build and all deterministic RMA-124 contracts;
2. normal hosted repository CI, including the production Java bridge under Java 17 `-Xlint:all -Werror` and Android lint warnings-as-errors;
3. the complete self-hosted `kawa` Unity/API-26 regression, including Unity tests, APK build/verification, physical camera/tracking/lifecycle/rendering checks, evidence uploads, and APK upload.

After those implementation-SHA gates pass, replace this candidate section with the exact commit, run, job, and artifact evidence, create an evidence-only documentation commit, and require the same gates again on that final evidence SHA.

## Physical coverage boundary

The standard physical regression proves packaging and no regression; it does not invoke RMA-124 or prove audible system/network TTS. Do not claim live speech output until a dedicated Phase-13 physical speech acceptance records the selected Android engine, exact voice and locale, network-required status, callbacks, and audio/output evidence.
