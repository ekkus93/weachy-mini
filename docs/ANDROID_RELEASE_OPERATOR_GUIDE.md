# Android Release Operator Guide

This guide is the release-facing documentation hub for the Reachy Mini Android digital twin. It intentionally describes the shipping virtual product only; optional physical Reachy Mini hardware is not required for the Android digital twin release gate unless a separately named calibration experiment says otherwise.

## Build instructions

1. Start from a clean checkout on `master`.
2. Verify the pinned toolchain with `python3 scripts/verify_toolchain.py --manifest-only`.
3. Run static repository checks with `./scripts/ci.sh --static-only`.
4. Run native and managed checks with `./scripts/ci.sh` when the local machine has the pinned native toolchain.
5. For Android packaging, use `scripts/build_unity_android.sh development` for a development APK and `scripts/build_unity_android.sh release` for a release artifact. The pinned Unity, Android SDK, Gradle, JDK, NDK, CMake, IL2CPP, and ARM64 requirements remain sourced from `toolchain.lock.json`.
6. Treat hosted CI and Local Unity Android Validation as authoritative release qualification when available. A zero-step runner failure, artifact upload outage, or missing job log is infrastructure evidence, not source acceptance.

## Android device requirements

Supported devices must be Android ARM64 devices capable of running the Unity IL2CPP build, loading the production MuJoCo runtime, and sustaining the selected fidelity profile without unbounded memory growth or lifecycle faults.

The release matrix records low, mid, and high representative classes. Each release candidate must record SoC, Android version, RAM, graphics API, camera capability, speech-service availability, and whether the device supports the selected default profile. API-31 or newer is required for explicit Android on-device ASR; older devices may still run the app but cannot close the offline on-device ASR acceptance criterion.

## Model installation and import

The selected initial local LLM is Qwen3-0.6B Q4_K_M as described by `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`. Model acquisition must go through the manifest-bound local model package manager. The manager verifies free storage, stages downloads or imports, checks byte count and SHA-256, atomically publishes only verified artifacts, and rejects arbitrary unvalidated paths.

A fresh device may legitimately report that no compatible local model is installed. That is a visible unavailable state, not permission to fall back to a cloud model. Deleting or cleaning recoverable storage must preserve installed models unless the user explicitly chooses model deletion.

## Provider configuration and BYOK warning

Provider choices are independent for ASR, TTS, LLM, and VLM. Cloud LLM and Cloud VLM require explicit profile configuration, explicit credential storage, and explicit fallback-policy authorization before any conversation text or camera frame crosses the device boundary.

Bring-your-own-key credentials must be stored through the Android Keystore-backed provider secret store on Android. Development hosts may use trusted local-network cleartext only when the provider profile explicitly selects that mode for a trusted local address. Public provider endpoints must use HTTPS. The UI must disclose that Cloud LLM sends conversation text and Cloud VLM sends camera frames to the configured endpoint.

## Calibration workflow

Camera reprojection uses exact calibration selection. A production frame is observation-eligible only when the live camera descriptor, lens facing, output dimensions, Reachy-side dimensions, and official model compatibility match an available calibration profile.

Missing, mismatched, or stale calibration must surface `NoCalibration` or another explicit unavailable/faulted state. It must never load placeholder calibration as calibrated evidence. Calibration capture and physical calibration experiments remain opt-in flows with their own evidence requirements.

## Fidelity-level explanation

Release labels must match evidence:

- Geometric baseline: visual shape, transforms, and camera geometry are present, but no claim is made about calibrated dynamics.
- Dynamic baseline: MuJoCo is authoritative and stable for the selected model and reset paths.
- Servo-fidelity: command, behavior, and visual-servo paths are accepted against the production runtime.
- Calibrated twin: physical calibration evidence exists for the named device/profile pair.

Do not label the app above the evidence actually passed. Deferred Level 2 depth reprojection, Level 3 reconstruction, AR placement, movable observer camera, and VLA features must remain disabled unless a later milestone explicitly enables and qualifies them.

## Camera Level 1 approximation and missing-pixel behavior

The first release uses Level 1 camera reprojection. It warps a phone camera frame into the Reachy-eye view using the selected calibration and the authoritative MuJoCo camera pose. It is not depth-aware and does not reconstruct hidden surfaces.

Invalid or missing pixels are carried through the validity mask. Vision and VLM paths may consume only observation-eligible transformed frames. Invalid pixels must not reuse stale content, fill from prior frames, or be described as real visual evidence. When coverage is insufficient, the frame is rejected with visible diagnostics.

## Troubleshooting and diagnostic bundles

Use the diagnostics panel to inspect active fidelity, provider selections, camera/perception state, simulation status, thermal/resource state, and recent faults. Use `EXPORT REDACTED BUNDLE` to produce a diagnostics ZIP containing version/configuration, performance-health, redacted logs, and a manifest.

Diagnostic bundles exclude credentials, raw media, transcripts, and conversation text by default. Optional sensitive content requires explicit user selection, and the current production path supports only redacted export. If storage is low, use Recoverable Storage Cleanup and retry rather than accepting a partial bundle.

## Privacy and data-flow explanation

Offline paths stay on device: production MuJoCo simulation, camera transformation, on-device tracking, local model execution, Android on-device ASR, and exact offline TTS voices. Android system ASR and network-required TTS voices are explicitly device/provider controlled and may be network-backed; they must not be automatic fallbacks for offline paths.

Cloud provider paths are opt-in and BYOK. Cloud LLM may receive conversation text. Cloud VLM may receive transformed camera frames and prompts. Diagnostics and logs are redacted before export. Provider failures, malformed responses, network loss, solver faults, calibration mismatches, invalid frames, and local-model failures must remain visible failures rather than silent fallbacks.
