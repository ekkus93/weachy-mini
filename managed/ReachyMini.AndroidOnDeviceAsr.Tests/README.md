# RMA-121 Android on-device ASR contracts

This executable validates the RMA-121 provider state machine without requiring
Android hardware or microphone access. It uses a deterministic fake platform to
exercise explicit on-device availability, API-version language-support behavior,
permission gating, partial/final/no-match results, concurrency, timeout,
cancellation, service death, language-model absence, request identity, disposal,
and the no-retry/no-fallback boundary.

The suite also reads the production Unity/Java bridge source and Android manifest
to enforce the RMA-121 locality rules:

- production uses `SpeechRecognizer.createOnDeviceSpeechRecognizer`;
- production never calls the system `createSpeechRecognizer` factory;
- production never sets `RecognizerIntent.EXTRA_PREFER_OFFLINE`;
- model download is not triggered automatically;
- `RECORD_AUDIO` and recognition-service package visibility are declared;
- recognizers are destroyed explicitly.

The permanent GitHub Actions gate additionally compiles/lints the production Java
bridge through the first-party `android-plugin` project.
