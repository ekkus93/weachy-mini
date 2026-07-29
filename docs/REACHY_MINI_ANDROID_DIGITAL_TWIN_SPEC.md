# Reachy Mini Android Digital Twin — Implementation Specification

**Repository:** `ekkus93/weachy-mini`  
**Document path:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md`  
**Companion implementation plan:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`  
**Status:** Initial implementation specification  
**Date:** 2026-07-28

## 1. Purpose

This document specifies a free Android application that presents and operates a virtual Reachy Mini robot. The application shall run locally on an Android phone, use Unity for presentation and Android integration, and use an embedded MuJoCo runtime as the authoritative mechanical dynamics simulator.

The product is not merely an animated avatar. Its long-term target is a calibrated, high-fidelity digital twin of Reachy Mini's closed-loop head mechanism, body rotation, antennas, actuator behavior, contacts, and failure modes. The first usable releases may begin with the official Reachy Mini MuJoCo model and progressively replace approximations with measured parameters.

The application shall also provide an offline-capable conversational stack using Android speech services and a small local language model, with optional OpenAI and OpenAI-compatible cloud providers for ASR, TTS, LLM, and VLM functions.

Normative terms such as **MUST**, **MUST NOT**, **SHOULD**, and **MAY** describe implementation requirements.

## 2. Product principles

1. **Android-first and self-contained.** Normal operation MUST NOT require a desktop computer, Python daemon, or remote physics server.
2. **Authoritative dynamics.** MuJoCo state is authoritative. Unity transforms MUST render MuJoCo results and MUST NOT secretly replace or correct the simulated mechanism.
3. **Offline by default.** Android on-device ASR, Android offline TTS, local perception where practical, and a local sub-1B-class LLM form the default configuration.
4. **Explicit cloud use.** No subsystem may silently switch from local processing to a network provider.
5. **Provider independence.** ASR, TTS, LLM, and VLM providers are independently selectable.
6. **Visible failures.** Permission errors, unsupported devices, provider failures, invalid model files, simulation overruns, unavailable camera pixels, and calibration deficiencies MUST be visible and diagnosable.
7. **Measured fidelity.** Unknown physical parameters MUST be recorded as uncalibrated values, not presented as exact constants.
8. **Deterministic control boundary.** AI systems issue high-level intentions. They MUST NOT directly write raw motor torque, arbitrary MuJoCo state, or unconstrained joint targets.
9. **No dangerous fallback logic.** A failed subsystem MUST enter a defined degraded or unavailable state. It MUST NOT fabricate success or switch providers without user authorization.
10. **Future-compatible interfaces.** Depth-assisted reprojection, scene reconstruction, AR, and experimental learned policies shall be addable without replacing the core interfaces.

## 3. Scope

### 3.1 Initial product scope

The initial implementation shall include:

- Unity 6 LTS Android application using IL2CPP and ARM64.
- Embedded MuJoCo native runtime compiled for Android.
- Reachy Mini rigid-body model based on the official MJCF and permitted associated assets.
- Closed-loop Stewart-platform head mechanism, body yaw, and two antenna joints.
- Native fixed-step simulation independent of Unity rendering cadence.
- Fixed Unity presentation camera; no user orbit, pan, or free-fly camera in the first release.
- Front and rear Android camera selection.
- Level 1 rotation-only phone-camera reprojection using simulated head yaw, pitch, and roll.
- Valid-pixel mask and coverage measurement for reprojected images.
- Lightweight on-device visual tracking where supported.
- Optional local or remote VLM analysis of the transformed Reachy-eye image.
- Android on-device ASR as the preferred default.
- Android offline TTS as the preferred default.
- Local GGUF LLM approximately 1B parameters or smaller through an Android-native inference runtime, initially llama.cpp.
- Optional OpenAI and OpenAI-compatible ASR, TTS, LLM, and VLM providers.
- Deterministic behavior planner for gaze, gestures, speech, idle behavior, listening, and error states.
- Persistent settings, secure credential storage, diagnostics, performance monitoring, and test support.

### 3.2 Explicitly deferred features

The following are out of scope for the first release but MUST be documented as future extension points:

- Level 2 depth-assisted camera reprojection.
- Level 3 persistent 3D scene reconstruction.
- AR placement and ARCore world anchoring.
- User-controlled observer camera orbit, pan, zoom, or free flight.
- Continuous full-frame VLM inference.
- VLA or end-to-end learned motor policy.
- Direct AI torque control.
- Multi-microphone direction-of-arrival simulation from a single phone microphone.
- Multi-device synchronization or cloud account system.
- iOS support.

## 4. Definition of mechanical fidelity

“Mechanically exact” shall be treated as an engineering target, not an absolute claim. The implementation shall use the term **calibrated high-fidelity digital twin** in user-facing and technical material unless validation data supports a narrower claim.

The simulator shall progress through these fidelity levels:

1. **Geometric baseline:** official model topology, dimensions, joint limits, masses, and inertias load and run correctly.
2. **Dynamic baseline:** stable closed-loop dynamics, gravity, contacts, actuator saturation, and repeatable fixed-step execution.
3. **Servo fidelity:** measured controller timing, torque-speed behavior, current limits, quantization, friction, and backlash.
4. **Unit-calibrated twin:** parameters fitted to a particular physical Reachy Mini.
5. **Population model:** optional parameter distributions representing manufacturing and wear variation.

The application MUST identify the active fidelity level and calibration profile.

### 4.1 Proposed validation targets

These are project targets to be confirmed through measurement:

| Measurement | Baseline target | Mature target |
|---|---:|---:|
| Static head-position error | < 2 mm | < 0.5 mm |
| Static orientation error | < 1 degree | < 0.25 degree |
| Step-response settling-time error | < 15% | < 5% |
| Peak overshoot error | < 15% | < 5% |
| Motor-current prediction error | < 20% | < 10% |
| Free-decay trajectory error | < 15% | < 5% |
| Contact-force prediction error | < 25% | < 10–15% |

Validation results MUST state test conditions, robot identity, temperature, supply voltage, calibration version, and software commit.

## 5. System architecture

```text
Android application
└── Unity runtime
    ├── Presentation layer
    │   ├── Fixed robot view
    │   ├── UI, settings, diagnostics
    │   ├── Audio playback
    │   └── Optional camera/debug previews
    │
    ├── Android integration layer
    │   ├── CameraX bridge
    │   ├── SpeechRecognizer bridge
    │   ├── TextToSpeech bridge
    │   ├── Android Keystore bridge
    │   ├── Permissions and lifecycle
    │   └── Device capability discovery
    │
    ├── Application services
    │   ├── Conversation orchestrator
    │   ├── Provider registry
    │   ├── Perception/world model
    │   ├── Behavior planner
    │   ├── Model manager
    │   └── Persistence and telemetry
    │
    └── Native ARM64 plug-ins
        ├── MuJoCo simulation core
        ├── Reachy model and actuator extensions
        ├── llama.cpp local inference
        └── Optional native image-processing helpers
```

### 5.1 Threading model

The minimum threading separation shall be:

- **Physics thread:** fixed-step MuJoCo simulation; highest application scheduling importance.
- **Unity main thread:** rendering and UI only; must not block on network, model inference, camera conversion, or long native calls.
- **Camera analysis thread:** frame acquisition and reprojection scheduling.
- **Inference worker:** local LLM/VLM work with cancellable jobs and bounded queues.
- **Network worker:** HTTP/WebSocket requests, streaming parsers, and retries.
- **Audio control path:** Android callbacks marshaled into application events without blocking platform callback threads.

Physics shall degrade visual/inference throughput before missing authoritative simulation deadlines.

## 6. Unity and Android platform requirements

### 6.1 Build target

- Unity 6 LTS version selected and pinned by the project.
- Android ARM64 (`arm64-v8a`) required.
- IL2CPP required for release builds.
- Universal Render Pipeline recommended.
- Vulkan preferred, with OpenGL ES 3 fallback only after validation.
- Minimum Android API level shall be chosen after the first compatibility spike; the initial target SHOULD support modern on-device speech and CameraX while avoiding unnecessarily excluding devices.
- Release output shall support Android App Bundle and local APK test builds.

### 6.2 Lifecycle

The application MUST correctly handle:

- cold start;
- pause and resume;
- screen rotation policy;
- activity recreation;
- audio focus loss;
- camera interruption;
- microphone interruption;
- backgrounding;
- low-memory callback;
- thermal throttling;
- app termination during model download;
- restoration after incomplete downloads;
- native plug-in initialization failure.

Physics MUST pause or enter a documented controlled mode when application execution is suspended. Simulation time MUST NOT jump forward by wall-clock duration after resume.

## 7. MuJoCo integration

### 7.1 Native build

MuJoCo shall be cross-compiled for Android ARM64 with a reproducible CMake/NDK toolchain. The build shall disable unnecessary desktop viewer dependencies. The exact MuJoCo release and source commit MUST be pinned.

A native wrapper shall expose a stable, versioned C ABI. C++ symbols MUST NOT cross the Unity boundary.

Minimum ABI concept:

```c
typedef struct ReachySimHandle ReachySimHandle;

typedef struct {
    uint32_t abi_version;
    double physics_timestep_seconds;
    uint32_t state_buffer_capacity;
    uint32_t flags;
} ReachySimConfig;

ReachySimHandle* reachy_sim_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    char* error_buffer,
    size_t error_buffer_size);

void reachy_sim_destroy(ReachySimHandle* sim);
int reachy_sim_step(ReachySimHandle* sim, uint32_t step_count);
int reachy_sim_set_commands(ReachySimHandle* sim, const void* commands, size_t bytes);
int reachy_sim_read_state(ReachySimHandle* sim, void* state, size_t bytes);
int reachy_sim_apply_body_wrench(ReachySimHandle* sim, int32_t body_id,
                                 const double force[3], const double torque[3]);
const char* reachy_sim_last_error(ReachySimHandle* sim);
```

All structs crossing the ABI MUST have explicit widths, layout tests, and ABI-version checks.

### 7.2 Simulation ownership

- MuJoCo owns body transforms, velocities, constraints, contacts, and actuator state.
- Unity reads immutable state snapshots.
- Unity MUST NOT call `Transform` setters to modify simulated bodies except during initialization from a stopped state through an explicit reset API.
- UI commands enter through a command queue and are applied at simulation-step boundaries.
- State exchange shall use double or triple buffering without per-frame heap allocation.

### 7.3 Timing

Initial timing targets:

- physics timestep: 0.002 seconds / 500 Hz;
- control update: 50–500 Hz depending on controller emulation;
- Unity render: 30 FPS default, optional 60 FPS;
- camera analysis: 10–30 FPS depending on device;
- state publication: 20–100 Hz.

The native runtime MUST detect and report deadline misses, accumulated lag, invalid state, NaN/Inf values, and constraint divergence. It MUST NOT silently skip unstable steps.

### 7.4 Reset and snapshots

The simulator shall support:

- deterministic reset to named poses;
- snapshot save/restore with version checks;
- seeded deterministic test runs where supported;
- controlled transition between torque-enabled and torque-disabled state;
- explicit hard reset after unrecoverable solver failure.

## 8. Reachy Mini mechanical model

### 8.1 Baseline model

The project shall begin from the official Reachy Mini MJCF and associated permitted assets. The imported model shall preserve:

- body yaw joint;
- six actuated Stewart joints;
- passive ball joints and rods;
- loop-closing equality constraints;
- head rigid body and camera mounting frame;
- left and right antenna joints;
- mass and inertia data;
- mechanical ranges;
- visual and collision geometry distinction.

The source version, license, transformation scripts, and all modifications MUST be recorded.

### 8.2 Model audit

Before claiming fidelity, the implementation shall audit and classify each parameter as:

- source CAD-derived;
- source simulator approximation;
- manufacturer specification;
- directly measured;
- fitted from experiment;
- assumed placeholder.

Placeholder parameters MUST be prominently labeled and MUST NOT be included in a “calibrated” profile.

### 8.3 Actuator model

The official generic position actuators shall be replaced progressively by a servo model containing, where supported by measurement:

- command sampling and bus latency;
- encoder resolution and quantization;
- position and velocity profiles;
- controller gains and saturation;
- current limiting;
- torque-speed curve;
- supply-voltage dependence;
- motor and reflected gear inertia;
- Coulomb and viscous friction;
- stiction and breakaway torque;
- backlash/hysteresis;
- gear compliance;
- thermal state and derating;
- shutdown conditions;
- torque-enable/disable behavior.

A simple position actuator MAY be retained as a clearly named baseline mode for comparison, but it MUST NOT be called mechanically exact.

### 8.4 Contacts and stops

The model shall add performance-appropriate physical collision geometry for moving internal components and external surfaces. Collision geometry SHOULD use validated convex or primitive approximations rather than detailed visual triangle meshes where that improves stability.

The implementation shall model:

- head/body shell contact;
- rods and motor-arm interference;
- antenna contact where meaningful;
- joint limits and mechanical hard stops;
- external contact with scene objects;
- overload and constraint-force reporting.

## 9. Calibration and system identification

A physical Reachy Mini is required to achieve unit-calibrated fidelity. The project shall provide a calibration data format and tooling independent of Unity rendering.

### 9.1 Required measurements

Calibration runs SHOULD capture synchronized:

- command timestamp and commanded position;
- measured joint position and velocity;
- measured current or load indication where available;
- supply voltage;
- IMU acceleration and angular velocity where available;
- external head pose from a calibrated camera or tracker;
- force/torque measurements for contact tests where available;
- motor and ambient temperature;
- firmware and control-register configuration.

### 9.2 Required experiment families

- unloaded single-actuator sweeps;
- gravity-loaded static poses;
- small and large step responses;
- sinusoidal frequency sweeps;
- direction reversals for backlash;
- torque-disabled free decay;
- external impulse response;
- instrumented contact;
- simultaneous multi-actuator motion;
- cold and warm operating conditions;
- repeated trials for uncertainty estimates.

### 9.3 Calibration artifacts

Each calibration profile MUST include:

- unique profile ID;
- robot serial or user-defined identity;
- source dataset hashes;
- fitted parameter values and units;
- fitting method and software version;
- held-out validation results;
- confidence/uncertainty where available;
- compatibility with model and simulator versions.

## 10. Presentation and interaction

### 10.1 Fixed presentation camera

The first release shall use a fixed Unity camera framing Reachy in a stable front or three-quarter view. The application shall not provide orbit, pan, zoom, or free-flight controls in normal mode.

The camera exists only to show the virtual robot. Moving Reachy's head does not move this presentation camera.

### 10.2 User interface

Minimum screens or panels:

- main conversation/simulation view;
- provider settings;
- camera and microphone settings;
- local model management;
- simulation fidelity and calibration settings;
- permissions and device capabilities;
- diagnostics and logs;
- open-source licenses and asset attribution.

The main view SHOULD show concise state such as listening, thinking, speaking, camera source, offline/cloud provider status, and simulation health.

## 11. Phone camera and Reachy-eye reprojection

### 11.1 Camera source

The app shall use CameraX or an equivalent Android camera integration that provides preview and image-analysis frames. The user shall explicitly select front or rear camera. The application MUST NOT unexpectedly switch cameras.

### 11.2 Level 1 reprojection

The first release shall implement rotation-only reprojection. It uses the phone camera frame as the source and applies the simulated Reachy camera orientation derived from MuJoCo head yaw, pitch, and roll.

Conceptually:

```text
Phone frame
  -> orientation/lens normalization
  -> phone camera intrinsics
  -> relative rotation from neutral phone view to simulated Reachy view
  -> virtual Reachy camera intrinsics
  -> GPU perspective warp
  -> valid-pixel mask
  -> transformed Reachy-eye frame
```

For ideal pinhole cameras sharing an optical center, the mapping is based on:

```text
H = K_reachy * R_reachy_phone * inverse(K_phone)
```

The implementation MUST define coordinate conventions and test rotation signs, image orientation, front-camera mirroring, and device rotation.

### 11.3 Intentional approximation

The mechanical simulation still computes all six head degrees of freedom. Level 1 vision uses rotation only and intentionally ignores X/Y/Z head translation because a single RGB frame cannot reproduce translation-induced parallax or reveal occluded surfaces.

This limitation MUST be visible in documentation and frame metadata.

### 11.4 Missing pixels

When the requested virtual view extends outside the source image:

- pixels MUST be marked invalid;
- invalid pixels MUST NOT be silently synthesized;
- detectors and VLM requests MUST receive either the validity mask or a cropped/annotated valid region;
- valid coverage percentage MUST be computed;
- low-coverage conditions MUST be exposed to the behavior planner;
- tests MUST verify that stale pixels are not reused as valid content.

### 11.5 Vision frame contract

```csharp
public sealed record ReachyVisionFrame(
    long TimestampNanos,
    VisionSource Source,
    ReprojectionMode ReprojectionMode,
    Texture Image,
    Texture ValidityMask,
    float ValidCoverage,
    CameraIntrinsics PhoneIntrinsics,
    CameraIntrinsics ReachyIntrinsics,
    Quaternion PhoneOrientation,
    Quaternion ReachyHeadOrientation);
```

The production type may differ, but it MUST carry equivalent information without forcing CPU readback for local GPU consumers.

## 12. Perception and VLM

### 12.1 Vision provider architecture

```text
Transformed Reachy-eye frame
├── continuous lightweight perception
│   ├── face/person tracking
│   ├── basic object/motion tracking
│   └── gaze target coordinates
└── selective semantic analysis
    ├── local VLM provider
    ├── OpenAI vision-capable provider
    └── OpenAI-compatible provider
```

A VLM is optional and MUST NOT be required for basic conversation or face tracking.

### 12.2 Invocation policy

VLM analysis SHOULD occur only when:

- the user asks a visual question;
- the behavior planner requests semantic inspection;
- a significant scene change occurs;
- a new tracked entity appears;
- the user manually requests analysis;
- a configurable slow interval is enabled.

The app MUST bound request frequency and expose cloud usage before requests are sent.

### 12.3 World model

The perception subsystem shall maintain bounded tracked entities containing:

- stable local ID;
- class or description;
- image-space position;
- estimated direction;
- confidence;
- first/last seen timestamps;
- latest semantic description and age;
- source provider;
- validity/coverage context.

Stale observations MUST expire. The LLM MUST NOT be told that an entity is currently visible after tracking has expired.

## 13. Audio, ASR, and TTS

### 13.1 Microphone limitation

The phone microphone is treated as a single-channel source. The app shall not claim four-microphone direction-of-arrival capability. Direction MAY be inferred from visible conversational focus, user selection, or future external hardware, but MUST be labeled as inferred rather than measured.

### 13.2 ASR providers

Required provider types:

1. Android on-device SpeechRecognizer.
2. Android system SpeechRecognizer, explicitly marked as potentially network-backed.
3. OpenAI `/v1/audio/transcriptions`.
4. Configurable OpenAI-compatible transcription endpoint.

Default selection logic:

- Prefer explicit Android on-device recognition when available.
- If the on-device recognizer or language is unavailable, show a resolvable unavailable state.
- Do not silently use Android system/cloud or OpenAI.

ASR sessions shall be utterance-oriented with explicit lifecycle, cancellation, timeout, and end-of-speech handling.

### 13.3 TTS providers

Required provider types:

1. Android offline TextToSpeech voice.
2. Android system TextToSpeech voice, with network requirement displayed.
3. OpenAI `/v1/audio/speech`.
4. Configurable OpenAI-compatible speech endpoint.

Default selection logic:

- Select an installed voice whose metadata indicates no network requirement.
- If none is available, explain how to install voice data or select another provider.
- Do not silently choose a network voice.

Generated speech shall be played through a Unity/Android audio path associated with Reachy's speaker position. The conversation orchestrator shall receive start, progress where available, completion, cancellation, and error events.

## 14. Local LLM

### 14.1 Runtime

The initial local inference runtime shall be llama.cpp or an equivalently portable native runtime supporting Android ARM64 and GGUF. It shall be isolated behind an interface so the runtime can later be replaced.

### 14.2 Default model class

The initial model candidate shall be approximately 1B parameters or smaller, with Qwen3-0.6B-class models as an initial benchmark candidate. The final bundled or recommended model MUST be chosen through device testing, license review, quality evaluation, and thermal measurement.

A model need not be bundled in the APK. The app SHOULD support an explicit model download/import flow with:

- license and source display;
- expected file size;
- available-storage check;
- resumable or safely restartable download;
- SHA-256 integrity verification;
- atomic installation;
- deletion and cleanup;
- compatibility manifest;
- chat-template and tokenizer metadata.

### 14.3 Resource policy

- Physics has priority over local inference.
- Token generation MUST be cancellable.
- Context length, threads, batch sizes, and memory use MUST be bounded by a device profile.
- Thermal pressure MUST reduce inference load before reducing physics correctness.
- Out-of-memory and model-load failures MUST be visible and recoverable.

## 15. Cloud and OpenAI-compatible providers

### 15.1 OpenAI providers

The application may support current OpenAI endpoints, including:

- Responses API for LLM and vision-capable requests;
- `/v1/audio/transcriptions` for ASR;
- `/v1/audio/speech` for TTS.

Model IDs MUST be configuration values rather than hard-coded assumptions. Provider capability discovery or validation SHOULD be used where possible.

### 15.2 Compatibility modes

The configurable provider layer shall support, as separate adapters:

- OpenAI Responses-style endpoint;
- OpenAI Chat Completions-style endpoint;
- OpenAI-compatible transcription endpoint;
- OpenAI-compatible speech endpoint;
- OpenAI-compatible vision requests through the selected text endpoint style.

Provider configuration may include:

- base URL;
- model ID;
- authentication mode;
- secret reference;
- additional headers;
- timeout;
- streaming support;
- TLS policy;
- feature capabilities.

Arbitrary base URLs MUST be treated as security-sensitive. Plain HTTP SHOULD be rejected by default except for an explicitly enabled local-development mode.

### 15.3 Credentials

- No developer API key may be embedded in source, resources, APK, or AAB.
- Bring-your-own-key secrets shall use Android Keystore-backed encryption/storage.
- Secrets MUST NOT appear in logs, crash reports, analytics, exported settings, or screenshots.
- Clearing app data or deleting a provider shall remove associated credentials.
- The UI shall warn that a client device cannot guarantee secrecy on a compromised/rooted system.

## 16. Conversation and behavior

### 16.1 Conversation state machine

Minimum states:

```text
Idle
Listening
Transcribing
Thinking
PreparingSpeech
Speaking
Interrupted
Unavailable
Error
```

Transitions MUST be explicit, cancellable where appropriate, and logged without private content by default.

### 16.2 High-level AI output

The LLM shall produce structured behavior intent, for example:

```json
{
  "speech": "That looks interesting.",
  "gaze_target": { "type": "tracked_entity", "id": "entity-12" },
  "expression": "curious",
  "gesture": "small_head_tilt",
  "urgency": "normal"
}
```

The schema MUST be validated. Invalid or unsupported fields MUST NOT be executed.

### 16.3 Deterministic behavior planner

The planner shall:

- resolve gaze targets;
- enforce joint/workspace limits;
- select predefined parameterized gestures;
- coordinate speech, head motion, body yaw, and antennas;
- avoid collisions and excessive actuator load;
- account for visual coverage limits;
- provide interruption and safe-rest behavior;
- convert valid intent into controller targets.

The LLM/VLM MUST NOT directly write MuJoCo state or raw torques.

### 16.4 Baseline behaviors

- neutral idle breathing/micro-motion;
- listening posture;
- speaking motion synchronized loosely with audio energy or timing;
- acknowledgment/nod;
- curiosity/head tilt;
- surprise/recoil;
- gaze acquisition and centering;
- gaze loss/search within bounded limits;
- provider unavailable/error expression;
- safe rest and wake sequence.

Behaviors shall be deterministic for a fixed command stream and seed.

## 17. Persistence and data handling

The core app shall work without an account.

Persisted data may include:

- provider selections and non-secret configuration;
- encrypted secret references;
- local model manifests;
- calibration profiles;
- camera calibration;
- device performance profile;
- user-selected voice and language;
- optional bounded conversation history;
- diagnostics settings.

Conversation audio, camera frames, transcripts, and images MUST NOT be retained by default. Any recording/export feature requires explicit opt-in, retention controls, and visible status.

Settings migrations MUST be versioned and tested. Corrupt settings MUST produce a visible recovery path rather than silent reset.

## 18. Error handling and observability

### 18.1 Required error categories

- native library unavailable or ABI mismatch;
- MJCF/model load failure;
- solver instability or invalid state;
- physics deadline overrun;
- missing camera/microphone permission;
- camera or microphone occupied;
- unsupported on-device ASR/TTS;
- provider authentication/rate-limit/network failure;
- model download/integrity/load failure;
- low storage or memory pressure;
- low valid image coverage;
- unsupported device capability;
- calibration profile mismatch.

### 18.2 No silent fallback rule

Every fallback shall be one of:

1. explicitly selected by the user;
2. previously authorized through a named policy visible in settings; or
3. a local quality/performance degradation that does not change privacy, provider, or data destination.

Examples of prohibited behavior:

- on-device ASR failing and automatically sending audio to OpenAI;
- offline TTS failing and automatically selecting a network voice;
- invalid camera pixels being filled with old frames;
- MuJoCo instability being hidden by directly setting Unity transforms;
- local LLM failure being replaced with fabricated canned “success” output;
- calibration mismatch silently loading default values and calling them calibrated.

### 18.3 Diagnostics

The diagnostics view shall expose:

- simulation frequency, step duration, missed deadlines, and solver warnings;
- render FPS and thermal status;
- memory use and local-model state;
- camera frame rate, reprojection time, and valid coverage;
- active ASR/TTS/LLM/VLM providers;
- request timing and categorized errors without secrets;
- calibration and fidelity mode;
- app, model, native ABI, MuJoCo, and asset versions.

A diagnostic bundle export MAY be implemented, but MUST redact secrets and private media by default.

## 19. Performance and thermal policy

The app shall define device profiles rather than assuming flagship performance.

Priority order:

1. simulation correctness and stability;
2. audio interaction responsiveness;
3. camera acquisition and lightweight tracking;
4. UI responsiveness;
5. local LLM/VLM throughput;
6. graphical quality and optional previews.

Under load, the app MAY:

- reduce render FPS from 60 to 30;
- reduce camera analysis resolution/rate;
- suspend VLM jobs;
- reduce LLM context or generation speed;
- disable nonessential visual effects.

It MUST NOT silently enlarge the physics timestep, skip arbitrary physics steps, or switch to a kinematic animation mode while reporting calibrated dynamics.

## 20. Testing strategy

### 20.1 Native tests

- C ABI layout/version tests;
- model-load and error-path tests;
- deterministic stepping tests;
- NaN/Inf and constraint-divergence detection;
- joint-limit and contact tests;
- snapshot round trips;
- actuator unit tests;
- calibration parser and compatibility tests;
- long-running stability tests.

### 20.2 Unity tests

- state-buffer interpolation;
- coordinate conversion;
- no-authoritative-transform-write enforcement;
- behavior schema validation;
- gesture and planner limit tests;
- settings migrations;
- provider registry and cancellation;
- UI error-state tests.

### 20.3 Android instrumentation tests

- permissions denied/granted/revoked;
- pause/resume and activity recreation;
- CameraX front/rear lifecycle;
- SpeechRecognizer availability and cancellation;
- TTS voice filtering;
- Keystore-backed secret lifecycle;
- model download interruption and integrity failure;
- network loss and TLS errors;
- low-memory and thermal paths where testable.

### 20.4 Camera reprojection tests

- identity transform;
- known yaw/pitch/roll transforms;
- front-camera mirroring;
- portrait/landscape/device rotation;
- intrinsic scaling after resolution changes;
- validity-mask correctness;
- no stale-pixel reuse;
- GPU/CPU reference comparison;
- low-coverage behavior.

### 20.5 End-to-end acceptance tests

At minimum:

1. App launches offline on a supported Android phone.
2. MuJoCo loads and advances the full closed-loop model.
3. Unity renders body transforms sourced only from MuJoCo.
4. A deterministic gesture produces repeatable state traces.
5. Front or rear camera frames are reprojected from actual simulated head rotation.
6. Android on-device ASR and offline TTS work when installed and fail visibly when unavailable.
7. Local LLM produces a validated high-level intent without blocking physics.
8. Optional cloud providers can be configured independently.
9. Permission, network, model, and solver failures do not trigger silent cloud or animation fallbacks.
10. A diagnostic report identifies active versions, fidelity level, and performance.

## 21. Security and privacy

- Camera and microphone access require runtime permission and visible active state.
- Cloud-bound audio, text, or images require explicit provider selection.
- The app shall indicate which data type is about to leave the device.
- Logs shall be structured and redacted.
- Custom endpoint URLs and certificates shall be validated.
- Network security configuration shall reject cleartext traffic by default.
- Imported model and calibration files shall be treated as untrusted input with bounded parsing.
- Native APIs shall validate buffer sizes, versions, enum ranges, and object lifetime.

## 22. Licensing and attribution

The app is intended to be free and noncommercial, but all third-party obligations remain applicable.

The repository and release shall maintain a dependency/asset inventory covering at least:

- MuJoCo — Apache License 2.0;
- Unity runtime and packages — applicable Unity terms;
- Reachy Mini software-derived material — applicable Apache-2.0 notices;
- Reachy Mini hardware/model assets — applicable Creative Commons attribution, noncommercial, and share-alike terms where those assets are used;
- llama.cpp and local model licenses;
- Android/Google libraries and ML models;
- all other native and managed dependencies.

The app shall include an Open-Source Licenses and Asset Attribution screen. Modified Reachy-derived assets shall be clearly identified and distributed under the required terms. The project shall not imply endorsement by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI.

## 23. Versioning and reproducibility

The project shall pin or record:

- Unity editor version;
- Android Gradle Plugin, Gradle, JDK, NDK, and CMake versions;
- MuJoCo source release and commit;
- Reachy source asset commit;
- native compiler flags;
- llama.cpp source commit;
- local model ID, revision, quantization, and SHA-256;
- calibration profile version;
- provider API adapter version.

Build scripts MUST be reproducible enough for a clean checkout to build without relying on undocumented local files. Large or license-sensitive assets may use a documented fetch/import process with integrity checks rather than being committed directly.

## 24. Implementation gates

The project shall not proceed past a gate until its acceptance criteria are met:

1. **Android native feasibility gate:** MuJoCo ARM64 library loads and steps a constrained mechanism reliably on a physical phone.
2. **Model integrity gate:** official Reachy topology and transforms match reference outputs.
3. **Authoritative rendering gate:** Unity cannot silently diverge from MuJoCo state.
4. **Dynamics baseline gate:** stable 500 Hz target or a justified measured alternative.
5. **Camera gate:** rotation-only reprojection and validity masking pass reference tests.
6. **Offline interaction gate:** Android ASR/TTS plus local LLM function without network access.
7. **Provider gate:** cloud adapters are independent, secure, cancellable, and do not silently activate.
8. **Behavior gate:** validated high-level intent drives bounded deterministic trajectories.
9. **Calibration gate:** measured profile and held-out validation report exist before “calibrated twin” labeling.
10. **Release gate:** lifecycle, privacy, licenses, diagnostics, and representative-device performance pass.

## 25. Authoritative implementation plan

The companion TODO file at `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` defines the ordered implementation tasks, subtasks, tests, and acceptance criteria for this specification. Both documents are intended to be handed to an implementation agent together. No assistant-created companion file is required beyond these two paths.

## 26. Primary references

These references are informative; pinned source revisions in the repository take precedence during implementation.

- Reachy Mini source: https://github.com/pollen-robotics/reachy_mini
- MuJoCo source: https://github.com/google-deepmind/mujoco
- MuJoCo Unity documentation: https://mujoco.readthedocs.io/en/latest/unity.html
- Unity Android native plug-ins: https://docs.unity3d.com/Manual/AndroidNativePlugins.html
- Android CameraX image analysis: https://developer.android.com/media/camera/camerax/analyze
- Android SpeechRecognizer: https://developer.android.com/reference/android/speech/SpeechRecognizer
- Android TextToSpeech: https://developer.android.com/reference/android/speech/tts/TextToSpeech
- OpenAI Audio API: https://platform.openai.com/docs/api-reference/audio
- OpenAI Responses API: https://platform.openai.com/docs/api-reference/responses
