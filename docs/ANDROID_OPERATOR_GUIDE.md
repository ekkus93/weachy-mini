# Weachy Mini Android Operator Guide

This guide is the release-facing entry point for building, configuring, and
operating the Weachy Mini Android digital twin. It summarizes the supported
paths and links to the normative architecture and validation documents where
the implementation contract is more detailed.

Weachy Mini is a virtual Reachy Mini application. A physical Reachy Mini is not
required for the normal virtual-twin experience. Physical-unit calibration is
an optional evidence path for stronger unit-specific fidelity claims.

## Build instructions

The authoritative build instructions are in [BUILDING.md](BUILDING.md), and
machine-readable tool versions are pinned in
[`toolchain.lock.json`](../toolchain.lock.json).

Current normal development/release Android builds use the provisional **API 31
minimum**, target the pinned Android toolchain, build **ARM64 IL2CPP**, and
support Vulkan/OpenGL ES 3 according to the build configuration. The API 26
native artifacts described in the build documentation are feasibility/test
artifacts for older hardware; they do **not** lower the normal application
minimum.

At minimum, before treating a checkout as qualified:

```bash
python3 scripts/verify_toolchain.py
./scripts/ci.sh --static-only
./scripts/build_native.sh
```

The managed test executables under `managed/` are console runners. Use
`dotnet run`, not `dotnet test`; `dotnet test` can report zero tests and
still exit successfully. The exact commands and native-interop environment are
documented in [BUILDING.md](BUILDING.md).

A release/device build must additionally use the repository's pinned Unity and
Android toolchain and the ticket-specific qualification workflows applicable to
the changed paths.

## Android device requirements

For normal builds, use an **ARM64 Android device at API 31 or newer**. The
device must have enough free storage and memory for the application, generated
native libraries, and any explicitly imported local model.

Capabilities are feature-dependent:

- on-device Android speech recognition requires the corresponding Android
  on-device speech service/model to be installed and available;
- offline TTS requires an installed offline-capable Android voice;
- camera scenarios require the requested front/rear camera and user-granted
  camera permission;
- microphone scenarios require user-granted microphone permission;
- local-LLM scenarios require the selected GGUF to be present in the managed
  model store and admitted by the memory governor.

Missing capabilities fail closed and should be visible in Settings/diagnostics;
they must not silently switch to a cloud provider.

The representative-device performance matrix is a separate release gate. A
device merely satisfying API/ABI requirements does not by itself prove that it
meets performance or thermal targets.

## Model installation and import

The selected local model is **Qwen3 0.6B Q4_K_M**. Its authoritative manifest
is
[`models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`](../models/manifests/qwen3-0.6b-q4-k-m.local-llm.json).
The manifest pins the upstream source revision, Apache-2.0 license, artifact
size, SHA-256, runtime ABI, and managed relative path.

The manifest is provenance and integrity metadata; it is **not** permission to
silently download anything. The GGUF is not bundled in this repository or in
the app.

The package-management contract is documented in
[LOCAL_MODEL_PACKAGE_MANAGEMENT.md](architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md).
A model is loadable only after the package manager validates the exact manifest,
byte length, SHA-256, approved store ownership, and path containment. A
filename or `.gguf` extension is not sufficient.

**Current shipping limitation:** the Local Model settings surface still shows
`IMPORT — UNAVAILABLE`. The safe `ImportAsync`/`DownloadAsync` package
manager exists and is exercised by validation harnesses, but there is not yet a
production end-user picker/download UI that installs the selected GGUF. Do not
represent model installation as an available user workflow until that surface
is implemented and qualified.

The detailed manifest contract is in
[LOCAL_LLM_MODEL_MANIFEST.md](architecture/LOCAL_LLM_MODEL_MANIFEST.md).

## Provider configuration and BYOK warning

ASR, TTS, LLM, and VLM are independently selectable workloads. The settings
architecture is documented in
[SETTINGS_ARCHITECTURE.md](architecture/SETTINGS_ARCHITECTURE.md).

Cloud/provider credentials are **bring-your-own-key (BYOK)**. Do not commit API
keys to the repository, paste them into diagnostic bundles, or store them in
ordinary settings JSON. On Android, production provider secrets are stored
through the Android-Keystore-backed secret store; durable provider profiles
contain references rather than plaintext credentials.

Cloud/local-network provider activation is a privacy boundary. The application
must disclose the boundary and require the corresponding authorization before
cloud-bound text, images, or audio are sent. A provider failure must not
silently substitute another network provider.

Local compatible endpoints may have different transport requirements from
public cloud endpoints. Public/cloud endpoints are expected to use HTTPS;
trusted-LAN behavior is separately constrained by the application's endpoint
policy and remains subject to the RMA-191 platform network-security review.

## Calibration workflow

The calibration data/capture contract is documented in
[CALIBRATION_DATA_AND_CAPTURE.md](architecture/CALIBRATION_DATA_AND_CAPTURE.md).
Approval rules for measured profiles are documented in
[PHYSICAL_CALIBRATION_AND_PROFILE_APPROVAL.md](architecture/PHYSICAL_CALIBRATION_AND_PROFILE_APPROVAL.md).

A physical Reachy Mini is **not required** to run the virtual twin. The app can
operate with the upstream/unmeasured model. Physical-unit calibration is an
optional extension used only when making stronger claims about a particular
physical unit.

Never label a fixture, estimate, placeholder, or upstream default as measured
calibration. A calibrated profile requires the corresponding capture,
provenance, fitting, held-out validation, and approval evidence. The optional
physical runbook is
[RMA_074_PHYSICAL_CALIBRATION_RUNBOOK.md](operations/RMA_074_PHYSICAL_CALIBRATION_RUNBOOK.md).

## Fidelity-level explanation

The current model audit is
[model-parameter-audit.md](model-parameter-audit.md). Its baseline conclusion
is an **uncalibrated upstream geometric baseline**.

Release labels are evidence claims, not user-selectable marketing names:

- **Geometric baseline** — upstream/CAD-derived geometry without measured
  unit-specific dynamic calibration.
- **Dynamic baseline** — authoritative physics is active and validated, but
  stronger physical-equivalence claims are not implied.
- **Servo-fidelity** — additional measured actuator/servo behavior has passed
  the required evidence gates.
- **Calibrated twin** — the applicable physical calibration/profile has been
  measured, validated, approved, and is actually active.

The release must use only the strongest label supported by completed evidence.
Absence of a physical Reachy calibration does not stop the virtual product, but
it does prevent a unit-specific calibrated-twin claim.

## Camera Level 1 approximation and missing-pixel behavior

The Level-1 camera path is a **rotation-only reprojection approximation**. Its
coordinate contract is documented in
[CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md](architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md).

Level 1 does not reconstruct translation, parallax, newly revealed surfaces, or
occlusion changes. It therefore must not be described as a physically exact
novel viewpoint.

Valid coverage is defined in
[CAMERA_VALID_COVERAGE_POLICY.md](architecture/CAMERA_VALID_COVERAGE_POLICY.md).
Every transformed frame carries validity/coverage metadata. Pixels outside the
valid source projection are invalid; downstream tracking/VLM/world-model logic
must use the validity mask and must not reuse stale pixels or pretend missing
content was observed. Where a rendered/encoded representation needs concrete
bytes for invalid pixels, the implementation uses a defined neutral fill
rather than treating them as valid observations.

If valid coverage falls below policy thresholds, the vision-driven path must
degrade/stop explicitly rather than continue on fabricated image content.

## Troubleshooting and diagnostic bundle instructions

Start with the in-app diagnostics screen. The RMA-171 diagnostic model reports
active provider/fidelity state plus simulation, rendering, camera, memory, and
failure health:
[RMA_171_DIAGNOSTICS_SCREEN_SPEC_2026-08-14.md](RMA_171_DIAGNOSTICS_SCREEN_SPEC_2026-08-14.md).

For a shareable diagnostic package, use **EXPORT REDACTED BUNDLE**. The export
contract is
[RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md](RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md).

The bundle is deliberately bounded and user-triggered. It contains redacted
configuration/health/log evidence and excludes credentials, raw audio, raw
images/media, transcripts, and conversation text. The exporter re-applies
redaction while building the archive and will not overwrite an existing file.

When troubleshooting:

1. record the exact app/repository build or commit;
2. note the device model/API and whether networking was intentionally disabled;
3. capture the visible provider/fidelity/failure state;
4. export the redacted bundle if the issue requires sharing diagnostics;
5. for camera issues, include camera facing/session and valid-coverage state;
6. for local-model issues, record the manifest id/hash and the visible
   admission/health reason, but never attach API keys or private media.

## Privacy and data-flow explanation

The default architecture separates local data paths from explicitly authorized
network paths.

- on-device ASR keeps captured speech on device when that provider is selected
  and available;
- offline TTS uses an installed offline Android voice;
- the local LLM consumes the explicitly installed local model and does not
  require network access;
- camera transforms/tracking run locally unless the user explicitly invokes an
  authorized cloud-VLM path;
- cloud LLM/VLM or other network-backed providers receive only the workload
  data required for that explicitly selected/authorized operation.

There is no acceptable silent privacy fallback: an unavailable local provider
must report unavailable/degraded/faulted rather than quietly send content to a
cloud service.

Persistent provider profiles and secret material are separate. Android
provider secrets use the Keystore-backed secret store. Temporary private media
is delete-on-dispose/purgeable, and persistent recording/export is disabled by
default.

Diagnostics use a default-deny/redacted data boundary. The normal diagnostic
bundle excludes credentials, private media, transcripts, and conversation
text. See
[RMA_191_PRIVACY_SECURITY_REVIEW_2026-09-23.md](RMA_191_PRIVACY_SECURITY_REVIEW_2026-09-23.md)
once that review is merged; until then, the normative runtime contracts remain
the source of truth.

## Known release-facing limitations

Until the corresponding release gates close, do not overstate:

- selected-model installation as an end-user feature while
  `IMPORT — UNAVAILABLE` remains in the shipping UI;
- a device as performance-qualified merely because it can install/run the app;
- Level-1 camera reprojection as full 3-D reconstruction;
- unmeasured/default calibration as a calibrated physical twin;
- network-provider availability as permission to send data without explicit
  provider/privacy authorization.
