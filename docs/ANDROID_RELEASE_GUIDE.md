# Android release and user guide

This guide is the RMA-193 release-documentation surface for the Android Reachy Mini digital twin. It is intentionally tied to repository scripts and manifests so release claims stay aligned with the authoritative TODO and validation evidence.

## Build instructions

Use `docs/BUILDING.md` for full toolchain setup. The short release-oriented path is:

```bash
./scripts/ci.sh --static-only
./scripts/build_native.sh
./scripts/build_unity_android.sh device-feasibility
./scripts/build_unity_android.sh development
./scripts/build_unity_android.sh release
```

The machine-readable pins are in `toolchain.lock.json`. Unity, Android SDK, NDK, CMake, Gradle, JDK, .NET, and lint-tool versions must match that file before a release candidate is trusted.

## Android device requirements

The application is built for Android ARM64 (`arm64-v8a`) through Unity IL2CPP. Physical-device acceptance scripts require a connected, authorized, non-emulator ARM64 device with Android API 26 or newer, selected by `REACHY_ANDROID_SERIAL` or by the workflow-level `REACHY_PHYSICAL_DEVICE_SERIAL` variable. Release qualification must record the tested device model, SDK level, ABI, APK hash, and exact commit SHA.

A device is not qualified just because it can install the APK. The release gate must also pass the required Android acceptance scripts, including camera discovery/acquisition/texture, lifecycle, application composition, authoritative rendering, and the RMA-190 end-to-end scenarios that are in scope for the release.

## Model installation/import

Model binaries are not committed to the repository. `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json` is the selected local-model manifest and records the expected identity, source revision, license, artifact hash, size, and runtime compatibility. Any installed model must match the manifest before it is used; corrupted, mismatched, missing, or unsupported artifacts must fail closed rather than silently selecting another model.

The app and release notes must state that the selected local model is not bundled in the repository. A user or release process must provide the model artifact through the supported import/install path, and the app must surface an unavailable/degraded state if no compatible local model is installed.

## Provider configuration and BYOK warning

Cloud LLM and Cloud VLM settings are bring-your-own-key paths. Enabling them can send conversation text, camera-derived image data, or scene descriptions to the configured provider endpoint. The app must disclose that boundary before cloud-bound work is authorized.

Provider profiles must use HTTPS for public endpoints. Cleartext HTTP is only allowed for trusted local-development hosts such as loopback, private-network, or `.local` endpoints; it must not become a public-network fallback. Credentials belong in the Android Keystore-backed provider secret store and must never be committed, logged, exported in diagnostic bundles, or embedded in source.

## Calibration workflow

Camera calibration is opt-in and evidence-bearing. Calibration data must come from the versioned schema under `calibration/schemas/` and from an explicit capture/import workflow. Placeholder constants, fabricated calibration, stale frames, and unlabeled temporary values must not be represented as measured calibration.

When calibration is missing, stale, invalid, or not authorized, perception features that require calibrated camera geometry must report an unavailable/degraded diagnostic. They must not silently substitute uncalibrated homography, Unity cosmetic motion, stale camera frames, or a fake calibrated profile.

## Fidelity-level explanation

The product must label itself according to evidence, not aspiration:

- **Geometric baseline** means imported Reachy geometry and attribution are present, but dynamics or physical acceptance are not release-qualified.
- **Dynamic baseline** means MuJoCo is authoritative and stable, with neutral reset and model-state evidence.
- **Servo-fidelity** means behavior, authoritative motion, camera/tracking, and visual-servo paths have device evidence for the stated scenarios.
- **Calibrated twin** requires measured camera/robot calibration evidence and calibrated physical acceptance; this label is not available from placeholder calibration.

Deferred Level 2 depth reprojection, Level 3 scene reconstruction, AR placement, movable observer camera, and VLA capabilities are future milestones and must not be half-enabled in production release builds.

## Camera Level 1 approximation and missing-pixel behavior

Level 1 camera support uses a camera-to-Reachy-eye transformation and homography-style reprojection. It is an approximation, not depth-aware scene reconstruction. The app must surface that limitation in release notes and diagnostics when camera-grounded behavior is enabled.

Frames carry validity information. Missing, invalid, or out-of-view pixels must remain explicit through the frame path; they must not be treated as real observations. Where an image encoder needs concrete bytes for a provider request, invalid pixels are composited deterministically and must remain documented as invalid/masked source data rather than fabricated scene content.

## Troubleshooting and diagnostic bundle instructions

For build failures, run `./scripts/ci.sh --static-only`, `python3 scripts/verify_toolchain.py`, and the relevant native, managed, Unity, or Android acceptance script directly. For Android device failures, confirm ADB authorization, ABI/API level, selected serial, camera permission, installed offline TTS voice, selected model availability, and network state.

Diagnostic bundles must be redacted-by-default. They may include service state, provider identifiers, fidelity level, APK/report hashes, device model, SDK level, and bounded failure diagnostics. They must not include raw private media, cloud API keys, unredacted credentials, full transcripts beyond explicitly authorized evidence, or persistent camera/audio recordings unless the user has enabled an explicit media-export path.

## Privacy and data-flow explanation

Offline launch, MuJoCo simulation, deterministic behavior replay, camera acquisition, Level 1 reprojection, and offline TTS/local-model paths are intended to operate without cloud credentials. Cloud LLM/VLM paths are optional and must require explicit provider configuration plus privacy-boundary authorization.

No optional provider may silently substitute for another provider. If ASR, TTS, local LLM, cloud LLM/VLM, camera, calibration, model loading, or network access is unavailable, the app must report unavailable/degraded/faulted status with a visible diagnostic and preserve fail-closed behavior.
