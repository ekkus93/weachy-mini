# Reachy Mini Android Digital Twin — Implementation TODO

**Repository:** `ekkus93/weachy-mini`  
**Document path:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`  
**Normative specification:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md`  
**Status:** Ready for implementation  
**Date:** 2026-07-28

## 0. How to use this TODO

This is the authoritative ordered implementation plan for the initial Android Reachy Mini digital twin. Read the entire specification before changing code.

### 0.1 Ralph-loop execution rules

- Work in phase and task order unless a task explicitly states that it may run in parallel.
- Do not mark a task complete merely because code exists. Mark it complete only when its acceptance criteria and required tests pass.
- Keep the application runnable at the end of each task.
- Prefer small, reviewable commits whose message includes the task ID.
- Do not create silent fallback paths.
- Do not replace MuJoCo state with cosmetic Unity animation when physics is incorrect.
- Do not invent calibration constants. Label temporary values as placeholders and keep them out of calibrated profiles.
- Do not embed cloud API keys.
- Do not add Level 2 depth reprojection, Level 3 scene reconstruction, AR, a movable observer camera, or a VLA while this TODO is active.
- When blocked, record the blocker, evidence, and next experiment in this file or a repository issue. Do not quietly work around the requirement.
- Any new assistant-created file referenced by this TODO must be committed at the exact path before the reference is added.

### 0.2 Definition of done for every task

A task is done only when:

1. implementation is committed;
2. tests named in the task pass;
3. failure paths are tested;
4. logs do not expose secrets or private media;
5. documentation/configuration changes are included;
6. no new compiler warnings or static-analysis errors are introduced;
7. Android lifecycle behavior is considered where relevant.

---

# Phase 1 — Repository, governance, and reproducible toolchain

## RMA-001 — Establish repository structure

- [ ] Create the initial directory layout:

```text
Assets/
  ReachyMini/
    Runtime/
    Editor/
    Tests/
  Plugins/
    Android/
Packages/
ProjectSettings/
android-plugin/
  src/main/java/
native/
  reachy_sim/
    include/
    src/
    tests/
  llama_runtime/
  cmake/
models/
  manifests/
calibration/
  schemas/
docs/
scripts/
third_party/
```

- [ ] Add a root `README.md` explaining the project goal, current maturity, supported platform, and build entry points.
- [ ] Add `.gitignore` rules for Unity, Gradle, Android Studio, NDK/CMake output, model binaries, downloaded assets, local credentials, and generated calibration datasets.
- [ ] Add `.gitattributes` appropriate for text normalization and any Git LFS-managed binary assets.
- [ ] Decide which large assets are committed, fetched, or imported by the developer.

**Acceptance criteria**

- [ ] A clean checkout has an understandable layout.
- [ ] No local model, API key, generated Unity library directory, or machine-specific SDK path is tracked.
- [ ] README links to both spec and TODO files.

## RMA-002 — Pin the build toolchain

- [ ] Select and record a Unity 6 LTS editor version.
- [ ] Pin Android Gradle Plugin, Gradle wrapper, JDK, Android SDK compile/target level, NDK, and CMake versions.
- [ ] Configure Android release builds for IL2CPP and ARM64.
- [ ] Configure development APK and release AAB build commands.
- [ ] Add a machine-readable toolchain manifest, for example `toolchain.lock.json`.
- [ ] Add a script that verifies installed tool versions and fails with actionable messages.

**Acceptance criteria**

- [ ] Two clean developer environments can build the same minimal Android application.
- [ ] Build output identifies all pinned tool versions.
- [ ] Unsupported NDK/CMake versions fail early rather than producing linker errors later.

## RMA-003 — Add baseline quality gates

- [ ] Configure C# formatting and analyzer rules.
- [ ] Configure C/C++ formatting, warnings, sanitizer-capable desktop test builds, and static analysis where practical.
- [ ] Add unit-test commands for native and Unity code.
- [ ] Add CI for documentation link checks, native desktop tests, managed tests, and asset/license manifest validation.
- [ ] Ensure CI does not require proprietary model downloads or secrets for default jobs.

**Acceptance criteria**

- [ ] CI runs on the initial scaffold.
- [ ] Warning policy is documented.
- [ ] A deliberately failing native and managed test is detected in a local dry run before being reverted.

---

# Phase 2 — Licensing and source-asset provenance

## RMA-010 — Create third-party inventory

- [ ] Add `third_party/THIRD_PARTY_NOTICES.md`.
- [ ] Record MuJoCo, Unity packages, Reachy-derived assets/code, llama.cpp, candidate local models, Android libraries, and any CV packages.
- [ ] Record license, source URL, source revision, modification status, redistribution status, and required notice for each entry.
- [ ] Add a machine-readable inventory used by the in-app license screen.

**Acceptance criteria**

- [ ] Every dependency and imported asset has an owner, license, source, and revision.
- [ ] The inventory distinguishes Apache-licensed software from CC BY-NC-SA Reachy hardware/model assets.
- [ ] No asset with unclear redistribution permission is packaged in the APK.

## RMA-011 — Implement Reachy asset import pipeline

- [ ] Pin the upstream Reachy Mini source commit used for the baseline MJCF and meshes.
- [ ] Create an import script that retrieves or accepts local source assets, verifies hashes, converts formats if required, and writes generated Unity assets to a deterministic location.
- [ ] Preserve upstream notices and record modifications.
- [ ] Do not manually edit generated files without updating the source transformation.
- [ ] Generate a model provenance report listing every imported source file and output.

**Acceptance criteria**

- [ ] A clean import produces deterministic hashes except for explicitly documented nondeterministic Unity metadata.
- [ ] Missing or changed upstream files fail visibly.
- [ ] Generated assets carry attribution metadata.

## RMA-012 — Add in-app licenses and unofficial-project notice

- [ ] Add a Licenses and Attribution screen.
- [ ] Include the MuJoCo Apache 2.0 notice and all required third-party notices.
- [ ] Include Reachy/Pollen Robotics attribution for derived assets.
- [ ] State that the project is unofficial and not endorsed by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI.
- [ ] State that the app is intended to be free and noncommercial.

**Acceptance criteria**

- [ ] Notices are readable offline from the installed app.
- [ ] The release artifact contains the same notice set as the repository inventory.

---

# Phase 3 — MuJoCo Android feasibility gate

## RMA-020 — Pin and build MuJoCo for Android ARM64

- [ ] Select and pin a MuJoCo release and source commit.
- [ ] Create an Android NDK CMake toolchain build for `arm64-v8a`.
- [ ] Disable desktop viewer/rendering dependencies not needed by the embedded solver.
- [ ] Produce a shared library suitable for Unity Android loading.
- [ ] Record compiler flags and exported symbol list.
- [ ] Add a reproducible script such as `scripts/build_mujoco_android.sh`.

**Acceptance criteria**

- [ ] `libmujoco.so` or the wrapped equivalent builds from a clean checkout/toolchain.
- [ ] The library loads on at least one physical ARM64 Android phone.
- [ ] License notices are included.
- [ ] No desktop-only dynamic library dependency remains.

## RMA-021 — Build a minimal constrained-mechanism native test

- [ ] Create a small MJCF with at least one closed-loop/equality constraint.
- [ ] Load and step it through a minimal C wrapper.
- [ ] Run at a 0.002-second timestep for at least 30 simulated minutes.
- [ ] Detect NaN/Inf, constraint divergence, and step failures.
- [ ] Record median, p95, and maximum step time on the test phone.

**Acceptance criteria**

- [ ] The model remains stable for the full run.
- [ ] Average execution leaves sufficient headroom for 500 Hz stepping.
- [ ] A deliberately malformed model returns a structured error instead of crashing.

## RMA-022 — Prove Unity IL2CPP native loading

- [ ] Create a minimal Unity scene that calls a version function from the native wrapper.
- [ ] Verify symbol resolution in Editor-compatible test mode and Android release mode as applicable.
- [ ] Verify repeated app pause/resume.
- [ ] Verify controlled native initialization failure.
- [ ] Verify library unload/destruction during application shutdown.

**Acceptance criteria — Android native feasibility gate**

- [ ] A physical Android phone loads the wrapper and steps the constrained model.
- [ ] Pause/resume does not leak handles or advance simulation by suspended wall time.
- [ ] Native failure appears in the UI and logs without an app crash.
- [ ] The measured result and tested phone model are documented.

---

# Phase 4 — Versioned simulation ABI and authoritative thread

## RMA-030 — Define the stable C ABI

**Status:** Complete (2026-07-30)

- [x] Add `native/reachy_sim/include/reachy_sim.h`.
- [x] Use explicit-width integer types and versioned structures.
- [x] Prevent C++ types, STL containers, exceptions, or ownership ambiguity from crossing the boundary.
- [x] Add create/destroy, model load, reset, step, command, state, wrench, snapshot, and error APIs.
- [x] Add capability and version query APIs.

Suggested starting point:

```c
#pragma once
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ReachySimHandle ReachySimHandle;

enum { REACHY_SIM_ABI_VERSION = 1 };

typedef struct {
    uint32_t abi_version;
    uint32_t struct_size;
    double timestep_seconds;
    uint32_t max_command_count;
    uint32_t flags;
} ReachySimConfig;

typedef struct {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t sequence;
    double simulation_time;
    uint32_t body_count;
    uint32_t joint_count;
    uint32_t actuator_count;
    uint32_t contact_count;
    uint32_t health_flags;
} ReachySimStateHeader;

ReachySimHandle* reachy_sim_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    char* error_buffer,
    size_t error_buffer_size);

void reachy_sim_destroy(ReachySimHandle* handle);
int32_t reachy_sim_reset(ReachySimHandle* handle, uint32_t reset_id);
int32_t reachy_sim_step(ReachySimHandle* handle, uint32_t step_count);
int32_t reachy_sim_submit_commands(ReachySimHandle* handle,
                                   const void* bytes, size_t byte_count);
int32_t reachy_sim_copy_state(ReachySimHandle* handle,
                              void* bytes, size_t byte_capacity,
                              size_t* required_size);
const char* reachy_sim_last_error(const ReachySimHandle* handle);
uint32_t reachy_sim_abi_version(void);

#ifdef __cplusplus
}
#endif
```

- [x] Define error codes and whether each error is recoverable.
- [x] Ensure `last_error` lifetime and thread-safety are documented.

**Tests**

- [x] Native layout/size tests.
- [x] C and C++ compilation tests.
- [x] Invalid pointer, undersized buffer, stale handle, and ABI mismatch tests.
- [x] Fuzz or property tests for command/state parsers where practical.

**Completion evidence**

- The production boundary is ABI version 2 and uses an opaque 64-bit generation-tagged handle, explicit-width structures, copied error records, typed recoverability, bounded variable-size output contracts, and capability/version queries.
- Native contract targets cover fixed layouts, C and C++ compatibility, malformed versions and sizes, null and undersized buffers, stale generations, double destroy, exact command byte counts and sequencing, duplicate actuator identifiers, reserved fields, finite/range validation, snapshots, errors, and 1,000 create/destroy cycles.
- Caller-owned buffers and size outputs remain unchanged for invalid, undersized, and busy results.
- Every public operation acquires an exclusive per-handle lease. Concurrent same-handle operations return `REACHY_SIM_STATUS_HANDLE_BUSY`; independent handles continue to make progress. Blocking-backend and eight-thread contention tests verify the contract.
- The deterministic malformed-input matrix is the property-test implementation for the current small stateful parsers. It runs under strict warnings and ASan/UBSan in supported desktop CI. A randomized fuzz corpus is deferred until it provides reproducible additional coverage beyond the maintained invariant matrix.
- The contract and ownership rules are documented in `docs/SIMULATION_ABI.md`.

## RMA-031 — Implement C# P/Invoke boundary

- [x] Add a single managed interop assembly.
- [x] Mirror native layouts with explicit `StructLayout` and packing tests.
- [x] Wrap the native pointer in `SafeHandle` or an equivalent deterministic lifetime abstraction.
- [x] Convert native error codes to typed managed results without losing original diagnostics.
- [x] Ensure no managed callback is invoked from the high-frequency physics thread unless explicitly designed and tested.

Suggested pattern:

```csharp
internal static class NativeReachySim
{
    private const string Library = "reachy_sim";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint reachy_sim_abi_version();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ReachySimSafeHandle reachy_sim_create(
        IntPtr modelBytes,
        nuint modelSize,
        in ReachySimConfig config,
        IntPtr errorBuffer,
        nuint errorBufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int reachy_sim_step(
        ReachySimSafeHandle handle,
        uint stepCount);
}
```

**Acceptance criteria**

- [x] ABI mismatch prevents simulation startup with a clear error.
- [x] Create/destroy survives 1,000 cycles in a stress test without leaks.
- [x] Managed and native structure sizes match on Android ARM64.

## RMA-032 — Implement authoritative simulation thread

- [x] Create a native or managed-owned dedicated simulation thread; choose one design and document why.
- [x] Use monotonic time and a fixed-step accumulator.
- [x] Apply command batches only at step boundaries.
- [x] Publish immutable state snapshots using double/triple buffering.
- [x] Record step duration, deadline misses, accumulated lag, solver warnings, and health flags.
- [x] Do not catch and discard solver errors.
- [x] Define pause, resume, reset, and shutdown handshakes.

Pseudocode:

```text
while running:
    wait until next fixed deadline or command/reset signal
    drain bounded command queue
    apply commands scheduled for this step
    mj_step(model, data)
    validate finite state and constraint health
    publish next immutable snapshot
    record timing
```

**Acceptance criteria**

- [x] Unity frame-rate changes do not alter simulation time or trajectory.
- [x] Rendering stalls do not corrupt the physics state.
- [x] Command queue overflow is visible and does not overwrite newer/older commands silently.
- [x] Resume restarts from paused simulation time without a catch-up burst.

**Completion evidence**

- `ReachySimulationWorker` owns one dedicated managed thread while native MuJoCo
  remains the sole owner of mutable physics state. The design rationale and lifecycle
  contract are documented in
  `docs/architecture/AUTHORITATIVE_SIMULATION_WORKER.md`.
- The worker uses `Stopwatch.GetTimestamp()` and a 0.002-second accumulator, drains
  bounded command batches only at native step boundaries, and publishes immutable
  state/timing values through a versioned triple buffer.
- Diagnostics publish total steps, last/maximum native-step duration, scheduler
  deadline misses, accumulated lag, command overflow/discard counts, exact health
  flags, and MuJoCo-warning episodes. Sleeping health is not mislabeled as a warning.
- Native step, command, reset, and state-copy failures become retained typed worker
  faults. A stale command test proves boundary-only application and visible
  `CommandFormatError` retention before deterministic shutdown.
- Managed-native acceptance covers 30 Hz and 60 Hz readers, a stalled reader,
  trajectory invariants, visible queue overflow, pause stability, reset discard,
  resume without suspended-time catch-up, and a controlled 40 ms native step that
  must publish an over-budget duration and deadline miss.
- Hosted quality run `30539234220` passed native warnings/sanitizers, managed
  warnings-as-errors and native-backed worker tests, static checks, Android tests,
  and official-model validation on `ac961fce3067c3724ba2638646251c52c78e62d3`.

## RMA-033 — Add snapshots and deterministic reset

- [x] Define named reset poses, including sleep/rest and neutral awake.
- [x] Add snapshot version, model hash, and calibration-profile ID.
- [x] Reject incompatible snapshots.
- [x] Test save/restore trajectory equivalence.

**Acceptance criteria**

- [x] Restoring a snapshot and replaying the same command stream reproduces the state within documented tolerances.

**Completion evidence**

- Stable reset identifiers are `SleepRest` (`0`) and `NeutralAwake` (`1`).
  Production neutral reset is supported. The pinned official model has no
  `sleep_rest`, `sleep`, or `rest` keyframe, so production returns typed
  `UNSUPPORTED` rather than fabricating a calibrated pose.
- Snapshot format version `1` carries the ABI/header size, exact model hash,
  authoritative sequence/time, payload size, and calibration-profile ID.
  Production additionally binds the private payload to timestep, model format,
  command/reset state, health, pending wrench state, and the complete MuJoCo
  integration state.
- Restore rejects version, model, calibration, configuration, payload, and state
  incompatibility transactionally. Failed restore preserves the previous live
  state and does not publish a replacement snapshot.
- `ReachySimulationWorker` now owns capture and restore as paused control
  requests. Successful restore visibly discards queued future commands,
  republishes the restored immutable state, remains paused, and requires an
  explicit resume.
- Native contract and production MuJoCo tests cover command and finite-duration
  wrench replay, transactional rejection, and byte-identical recapture. Managed
  acceptance covers paused capture, foreign-model rejection, queue discard,
  immutable restored publication, paused stability, and exact recapture bytes.
- Hosted run `30561792617` passed native warnings/sanitizers, managed
  warnings-as-errors and native-backed snapshot acceptance, static checks,
  Android tests, and official-model validation on `1606bb55`.
- Self-hosted run `30561792261` passed production staging, Unity tests, ARM64
  API-26 IL2CPP build/verification, installed lifecycle acceptance,
  authoritative-rendering acceptance, evidence uploads, and APK upload on the
  identical commit. The first device attempt had an isolated
  `WaitingForSnapshots` timeout; the exact job rerun passed without source or
  artifact changes.

---

# Phase 5 — Reachy model import and integrity gate

## RMA-040 — Load the official Reachy Mini MJCF baseline

**Status:** Complete (2026-07-30)

- [x] Import the pinned official MJCF and required mesh/collision assets.
- [x] Preserve body yaw, six Stewart actuators, passive ball joints, loop closures, head body, camera frame, and antennas.
- [x] Remove or isolate desktop-only cameras/rendering elements not required by the solver.
- [x] Do not alter source ranges or inertias without a tracked transformation and rationale.
- [x] Generate a machine-readable body/joint/actuator map.

**Acceptance criteria**

- [x] The Android runtime loads the full model.
- [x] The expected body, joint, actuator, and equality-constraint counts match the pinned reference.
- [x] Every named transform required by Unity is present.

**Completion evidence**

- The solver input is the clean Pollen Robotics checkout at
  `a739a6e461eb6d722901f1cfc225265ffc85c28d`; the pinned MJCF SHA-256 is
  `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`.
- The deterministic importer copies the MJCF, every referenced visual/collision
  mesh, and the upstream license without modifying source bytes. Per-file sizes
  and SHA-256 digests are recorded in `PROVENANCE.json`.
- The source lock requires all 17 named bodies, the complete ordered 18-body
  hierarchy, all 16 joints and types, all 9 actuator-to-joint mappings, 13 sites,
  2 source cameras, and all 5 equality-constraint pairs. A regression test proves
  that body reparenting fails even when counts and names remain unchanged.
- `MODEL_MAP.json` is the machine-readable body/joint/actuator/equality/site/camera
  map. Compiled MuJoCo 3.9.0 validation requires 19 bodies including world,
  16 joints, 9 actuators, 5 equalities, 13 sites, `nq=37`, and `nv=30`.
- MuJoCo cameras `studio_close` and `eye_camera` remain solver/model metadata but
  are explicitly excluded from the Unity prefab. The independent Unity camera and
  versioned visual-mesh conversion do not modify ranges, inertias, collision
  geometry, actuators, or equality constraints.
- Hosted run `30567896524` passed native warnings/sanitizers, managed tests,
  Ruff/actionlint/ShellCheck/static policy, Android tests, pinned-source topology,
  Unity visual conversion, official-model compile/step, and reference-trace
  generation on `d096796c422e9d7e0353a1dca89295e490665b84`.
- Self-hosted run `30567896601` passed production ARM64 MuJoCo staging, Unity
  EditMode/PlayMode tests, ARM64/API-26 IL2CPP build/verification, installed LG G6
  lifecycle acceptance, authoritative rendering, evidence uploads, and APK upload
  on the same exact commit.
- Detailed contracts and evidence are in
  `docs/architecture/OFFICIAL_REACHY_MODEL_IMPORT.md` and
  `docs/validation/RMA_040_MODEL_IMPORT_VALIDATION_2026-07-30.md`.

## RMA-041 — Audit mechanical parameters

**Status:** Complete (2026-07-30)

- [x] Add `docs/model-parameter-audit.md` or an equivalent generated report.
- [x] Classify each parameter as CAD-derived, upstream approximation, manufacturer specification, measured, fitted, or placeholder.
- [x] Explicitly flag the generic/perfect actuator parameters as baseline approximations.
- [x] Record joint-limit provenance.
- [x] Record any source comments indicating uncertain values.

**Acceptance criteria**

- [x] No placeholder is included in a profile labeled calibrated.
- [x] The diagnostics screen can eventually display the model fidelity classification from machine-readable data.

**Completion evidence**

- `docs/model-parameter-audit.md` defines the human-readable fidelity vocabulary,
  source-derived geometry/inertia scope, joint-limit inventory, actuator-default
  audit, equality-solver classification, calibration-label rule, and required
  follow-on evidence.
- `models/reachy-mini/model-parameter-audit.json` schema version `2` identifies
  contract `rma041_parameter_fidelity_v2`. Every parameter group, joint range,
  actuator, retained actuator-default class, and equality setting has an explicit
  classification. Manufacturer, measured, fitted, and calibrated evidence remain
  explicitly absent rather than inferred.
- The active `chosen_actuator` class inherits the upstream `perfect_actuator`
  defaults and remains a calibration-blocking `placeholder`. The retained
  `xc330m288t` class is also a placeholder; the two retained STS3215 defaults are
  upstream approximations. No profile may be labeled calibrated while any
  placeholder remains.
- `joint_limit_provenance` binds all ranges to the exact pinned MJCF commit and
  SHA-256, the `joint.range` attribute, and radian units. Each joint points to its
  applicable range policy. The two antenna hinges are explicitly recorded as
  lacking encoded hard stops, and the seven passive ball joints remain
  unrestricted upstream approximations.
- `source_uncertainties` binds each audited upstream comment to its exact actuator
  class or collision-mesh scope. Full-source validation requires actuator comments
  to remain inside their matching `<default class=...>` block; moving identical
  text elsewhere does not satisfy the contract.
- The display-ready `diagnostics` object is an exact validator-enforced projection
  of authoritative source and fidelity fields. It reports warning severity,
  `uncalibrated_upstream_baseline`, `calibrated=false`, the source-model hash, and
  the classifications that block calibration.
- Regression tests reject missing/wrong classifications, false measured or
  calibrated claims, joint-provenance drift, new unrecorded ranges, reassigned or
  relocated uncertainty comments, and diagnostics/fidelity disagreement.
- Focused validation run `30587841758` passed Ruff, all 11 parameter-audit tests,
  and static audit validation before publishing artifact
  `rma041-validated-patch-b2f116049df652307af45d7dd90f23bf7473fb8f` with digest
  `0bca4d4a13903d76bb513670be8bd15c80789fcf8fda90c4bc9e7bb95357859f`.
- The official-model job in hosted run `30588235631` passed the exact pinned
  upstream source/topology/parameter audit, Unity visual conversion, MuJoCo
  compile/step, and reference generation with the permanent contract. The run's
  unrelated static job saw a temporary patch helper; that helper and its workflow
  were removed in cleanup commit `a44d1f883e94515c24338b1a7ecb2fcb55430c4e`.
- Detailed validation evidence is in
  `docs/validation/RMA_041_MODEL_PARAMETER_AUDIT_VALIDATION_2026-07-30.md`.

## RMA-042 — Build reference-state comparison tests

**Status:** Complete (2026-07-30)

- [x] Produce desktop reference traces using the pinned upstream model and MuJoCo version.
- [x] Compare Android qpos, qvel, body transforms, and constraint residuals for reset and representative command sequences.
- [x] Define numeric tolerances and explain platform floating-point differences.
- [x] Store compact reference fixtures with hashes.

**Acceptance criteria — model integrity gate**

- [x] Android and desktop reference results agree within documented tolerances.
- [x] Loop-closure residuals stay bounded.
- [x] Coordinate conventions are documented and covered by tests.

**Completion evidence**

- `reference-scenario.json` pins the Reachy source/model hash, MuJoCo 3.9.0,
  0.002-second timestep, ordered actuator/body identities, four command phases,
  ten checkpoints, compiled dimensions, and per-field tolerances.
- Desktop generation and the native Android runner execute the same generated
  scenario. The committed compact lock binds the exact desktop trace bytes to the
  scenario, model, runtime, generator, serialization format, checkpoint count,
  and total step count using strict lowercase hexadecimal SHA-256 values.
- The comparator verifies exact platform/scenario/model/runtime/count identities,
  scenario-clock timing, all 37 qpos values, all 30 qvel values, all 17 named body
  positions and normalized wxyz quaternions, warning counts, and the maximum
  absolute residual across every MuJoCo equality row. Quaternion q/-q equivalence
  is accepted; malformed matching traces are rejected.
- Physical Android MuJoCo Feasibility run `30583271127` passed the AArch64 build,
  provenance checks, locked desktop regeneration, and LG-H872 API-26 comparison on
  `d229235d73851f58088b7c142e469ef6cfaeaefb`. Maximum qpos error was
  `3.784299262843405e-15`, maximum qvel error was
  `2.6852825518730583e-13`, and maximum observed equality residual was
  `3.84603861668803e-06` against the `0.001` bound, with zero warnings.
- Hosted Quality Gates run `30583907077` passed Ruff, actionlint, ShellCheck,
  static policy, native warnings/sanitizers, managed tests, Android tests, and
  official-model desktop trace generation on
  `da6fb1fd3e13afe2b2269ee2dd85ba0a0f2826de`.
- Detailed contracts and evidence are in `docs/reference-state-comparison.md` and
  `docs/validation/RMA_042_REFERENCE_STATE_VALIDATION_2026-07-30.md`.

---

# Phase 6 — Unity rendering from authoritative state

## RMA-050 — Build the Reachy Unity prefab

**Status:** Complete (2026-07-30)

- [x] Import visual meshes and materials through the asset pipeline.
- [x] Create one Unity transform per rendered MuJoCo body or a documented mapped subset.
- [x] Preserve model scale and coordinate conversion.
- [x] Add the fixed presentation camera and basic lighting.
- [x] Keep the presentation camera independent of Reachy's simulated camera frame.

**Completion evidence**

- The deterministic asset path imports the pinned MJCF, meshes, license,
  provenance, and model map; converts source STL triangles into Unity-coordinate
  OBJ files; emits `UNITY_RENDER_MAP.json`; and generates the prefab and scene.
- The prefab contains all 18 non-world MuJoCo bodies in canonical index and parent
  order, 161 visual instances, 41 referenced visual meshes, and 41 materials. The
  anonymous source body is represented only by deterministic presentation identity
  `__body_15`.
- Every mesh entry records source/output hashes, source scale, triangle count, and
  that scale was baked into vertices. Generated body and visual transforms retain
  unit local scale, preventing double application.
- The basis contract maps MuJoCo right-handed Z-up positions and `wxyz`
  quaternions into Unity left-handed Y-up coordinates, reverses mesh winding, and
  never modifies the solver model.
- The generated scene contains one root-level fixed front-three-quarter Unity
  camera and one root-level directional key light. The MuJoCo `studio_close` and
  `eye_camera` definitions remain excluded metadata and are not presentation
  objects.
- Hosted run `30591010118` and self-hosted `kawa` run `30591010149` passed on
  exact clean commit `28c582e9e23c5b61e0c8dfac0d8b6f423064ac40`, covering
  Python conversion tests, official-model conversion, Unity EditMode/PlayMode
  tests, ARM64 API-26 IL2CPP build, installed lifecycle acceptance, physical
  authoritative rendering, and artifact uploads.
- Detailed contracts and evidence are in
  `docs/architecture/AUTHORITATIVE_UNITY_RENDERING.md` and
  `docs/validation/RMA_050_UNITY_PREFAB_VALIDATION_2026-07-30.md`.

## RMA-051 — Implement state-to-render mapping

- [x] Read the latest two authoritative state snapshots.
- [x] Interpolate render transforms by simulation timestamps without feeding results back to physics.
- [x] Handle reset/discontinuity without interpolating through impossible poses.
- [x] Eliminate per-frame allocations.
- [x] Add an optional debug overlay of body axes and joint names.

**Acceptance criteria**

- [x] Rendering at 30 and 60 FPS displays the same underlying trajectory.
- [x] A test detects any script that attempts to authoritatively drive simulated transforms.
- [x] Sleep/wake and antenna motion align with MuJoCo bodies.

**Completion evidence — 2026-07-30**

- The production pose source rotates three preallocated authoritative-state
  frames and copies the latest ordered pair into two renderer-owned reusable
  pose frames. The immutable snapshot API remains available for diagnostics,
  but the production `LateUpdate` path no longer constructs pose arrays or
  snapshots.
- Timestamp interpolation, render-cadence independence, ordered publication,
  and reset/discontinuity snapping are covered by focused managed and Unity
  tests. The 30/60 FPS criterion is represented by rendering the same target
  simulation timestamp through different render-call cadences and requiring
  identical transforms.
- Allocation regressions require zero managed bytes across 128 production
  source-pair copies and 128 generated-prefab render iterations after warmup.
- The generated optional diagnostics overlay starts disabled and retains all
  18 body-axis bindings and all 16 joint labels without writing transforms.
- External transform writes fault instead of being overwritten, while
  Rigidbody, ArticulationBody, Animator, Animation, Timeline, and joint writers
  remain prohibited on the authoritative hierarchy.
- Hosted quality run `30593459422` passed managed, native, official-model,
  static, and Android gates on exact commit
  `09d5f6d3cf48a5b167f09629de520112ae60d5a6`.
- Self-hosted `kawa` run `30593459413` passed generated Unity tests, ARM64
  API-26 IL2CPP build/verification, installed HOME/resume lifecycle acceptance,
  and physical authoritative-rendering acceptance on the same exact commit.
  Device evidence verifies body/head motion, both antennas, all six Stewart
  links, ordered simulation time, reset continuity, renderer health, and no
  hidden kinematic fallback.
- The official pinned model provides no named sleep/rest keyframe. `SleepRest`
  therefore remains typed `UNSUPPORTED` rather than inventing a pose; real
  device sleep/wake lifecycle behavior and neutral/reset mapping are verified
  without fabricating unsupported model state.
- Detailed evidence is recorded in
  `docs/validation/RMA_051_STATE_TO_RENDER_MAPPING_VALIDATION_2026-07-30.md`.

## RMA-052 — Add authoritative-rendering invariant checks

**Status:** Complete (2026-07-30)

- [x] Add development-build assertions comparing Unity rendered
  transforms to the mapped MuJoCo snapshot.
- [x] Report drift above tolerance.
- [x] Ensure animation, Timeline, Animator, and physics components cannot
  write mapped transforms.
- [x] Disable or reject Unity Rigidbody/ArticulationBody components on
  authoritative robot bodies.

**Acceptance criteria — authoritative rendering gate**

- [x] Forced transform modification is detected in tests/development builds.
- [x] Production rendering contains no hidden kinematic fallback.

**Completion evidence — 2026-07-30**

- The renderer records expected Unity world transforms, authoritative
  sequence, interpolation target time, continuity identity, and configured
  tolerances after every mapped MuJoCo pose.
- `Application.onBeforeRender` performs the final frame-boundary comparison.
  Editor and development players emit an assertion before entering the same
  fail-closed renderer fault used by release players.
- `ReachyAuthoritativeInvariantReport` preserves expected/actual transforms,
  drift, body identity, sequence/time/continuity, and both tolerances.
  Invalid, zero, negative, NaN, and infinite tolerances are rejected.
- Tests force transform drift and require the assertion, retained report,
  renderer fault, and disabled motion authority. Descendant tests reject
  Rigidbody, Rigidbody2D, ArticulationBody, Animator, legacy Animation,
  and PlayableDirector/Timeline writers.
- Hosted run `30594656829` passed managed, native, official-model, static,
  and Android gates on exact commit
  `5d5bc2cb078ef5432c0ad6f95599890150330da6`.
- Self-hosted `kawa` run `30594656835` passed Unity tests, production ARM64
  MuJoCo staging, API-26 IL2CPP build/verification, installed lifecycle and
  physical authoritative-rendering acceptance, and artifact uploads on the
  same exact commit.
- Physical evidence retained renderer status `Rendering`, runtime status
  `Running`, all 18 canonical body bindings, canonical motion/reset checks,
  and `hidden_kinematic_fallback=false`.
- Detailed evidence is in
  `docs/validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md`.

---

# Phase 7 — Dynamics baseline and actuator fidelity

## RMA-060 — Establish stable baseline dynamics

**Status:** Complete (2026-07-30)

- [x] Run the official generic actuator model at 500 Hz as a named `upstream_baseline` mode.
- [x] Add finite-state, constraint residual, energy, joint-limit, and contact monitoring.
- [x] Test neutral, sleep, maximum valid head rotations, body yaw limits, and antennas.
- [x] Run long-duration stability tests on representative Android hardware.

**Acceptance criteria**

- [x] No unexplained divergence during the stability suite.
- [x] Any required deviation from 500 Hz is backed by measurements and recorded as a gate decision.

**Completion evidence**

- `models/reachy-mini/upstream-baseline-stability.json` defines the named
  `upstream_baseline` contract at an exact `0.002`-second / 500 Hz timestep.
  Its generated native header binds the exact profile bytes, pinned Reachy
  model SHA-256, MuJoCo 3.9.0, dimensions, actuator order, monitoring bounds,
  command schedule, and intentional sleep-command range exceptions.
- The 20-phase cycle covers neutral, the upstream sleep request, both body-yaw
  limits, positive and negative boundaries for all six Stewart actuators,
  both antenna extremes, and final neutral. Boundary probes use a declared
  `1e-9` radian inward inset to avoid platform-dependent one-ulp control-range
  classification while remaining effectively at the audited limits.
- Every solver step checks finite qpos/qvel/qacc, commands and actuator forces,
  body poses, equality residuals, scalar joint-limit excursions, contacts and
  penetration, total energy, and MuJoCo warning counters. The runner also
  retains mean/median/p95/maximum step timing and visible 2 ms deadline misses.
- Successful workflow run `30599288952` passed all focused profile tests,
  exact pinned-model import, the full 900,000-step desktop reference schedule,
  Android ARM64 cross-build and AArch64 verification, and the same 900,000-step
  schedule on physical LG-H872 API-26 hardware at commit
  `85dd886c398088946a2cc2ae61890aa94ad0294a`.
- The Android run completed `1,799.9999999712595` simulated seconds in
  `257.879618182` wall seconds for a solver real-time factor of
  `6.980001027847417`. Maximum equality residual was
  `0.00010839801784859326`, maximum penetration was
  `0.004506368083200441` m, maximum absolute energy was
  `1.2885171070884491` J, scalar joint-limit violation was zero, and MuJoCo
  warnings were zero. No timestep deviation was required.
- The physical harness also proved a structured invalid-cycle failure and
  retained the runtime/model/profile desktop artifact plus the device report,
  device identity, failure-path report, and attempted thermal captures.
- Detailed contracts and evidence are in
  `docs/architecture/UPSTREAM_BASELINE_DYNAMICS.md` and
  `docs/validation/RMA_060_UPSTREAM_BASELINE_STABILITY_VALIDATION_2026-07-30.md`.

## RMA-061 — Define pluggable servo model

**Status:** Complete (2026-07-30)

- [x] Create a servo model interface independent of Unity.
- [x] Represent command sample time, mode, target, profile velocity/acceleration, encoder quantization, current limit, torque-speed behavior, voltage, temperature, and fault state.
- [x] Add per-actuator parameter sets for body, Stewart, and antenna motors.
- [x] Add explicit `placeholder`, `manufacturer_estimate`, and `calibrated` parameter quality labels.

**Completion evidence**

- `native/reachy_sim/include/reachy_servo_model.hpp` defines the native
  C++17 command, observation, result, role, mode, fault, quality, validation,
  and replaceable `ServoModel` contracts without Unity ownership.
- `models/reachy-mini/servo-model-parameters.json` is the authoritative
  `rma061_servo_model_v1` registry. It binds the pinned source/audit and
  explicitly maps all nine official actuators to distinct body-yaw, Stewart,
  or antenna role parameter sets.
- All command timing, encoder, current, torque-speed, voltage, temperature,
  and fault parameters are represented as evidence-bearing qualified scalars.
  Unknown values remain null `placeholder` values; null manufacturer estimates
  and incomplete calibrated sets are rejected.
- The deterministic generator emits the committed native registry and rejects
  stale output, missing/reordered/cross-role bindings, unknown quality labels,
  and unsupported calibrated claims.
- Hosted run `30601191456` passed exact regeneration, eight schema failure
  tests, Unity-dependency rejection, GNU 13.3 strict warnings, ASan/UBSan,
  native library/test compilation, and the complete CTest contract on exact
  commit `68c035ab20ec20a28c8b287914d43dcaf7ad1c67`.
- Detailed design and evidence are in
  `docs/architecture/PLUGGABLE_SERVO_MODEL.md` and
  `docs/validation/RMA_061_PLUGGABLE_SERVO_MODEL_VALIDATION_2026-07-30.md`.

Suggested native concept:

```cpp
struct ServoInput {
    double target_position_rad;
    double target_velocity_rad_s;
    bool torque_enabled;
};

struct ServoObservation {
    double position_rad;
    double velocity_rad_s;
    double applied_torque_nm;
    double estimated_current_a;
    double temperature_c;
    uint32_t fault_flags;
};

class ServoModel {
public:
    virtual ~ServoModel() = default;
    virtual double ComputeTorque(const ServoInput& input,
                                 const ServoObservation& state,
                                 double supply_voltage,
                                 double dt) = 0;
};
```

## RMA-062 — Implement electrical and controller timing baseline

**Status:** Complete (2026-07-30)

- [x] Model command update interval and configurable latency.
- [x] Model encoder quantization and target quantization.
- [x] Implement bounded position control with saturation.
- [x] Add torque-speed limiting and voltage dependence from documented specifications as an explicitly noncalibrated baseline.
- [x] Add current-limit and torque-disable behavior.
- [x] Verify unit consistency.

**Tests**

- [x] zero-error torque;
- [x] saturation signs;
- [x] voltage scaling;
- [x] quantization boundaries;
- [x] delayed command application;
- [x] torque-disable gravity response;
- [x] fault transition behavior.

**Completion evidence**

- `ElectricalServoModel` is a Unity-independent native C++17
  implementation of the RMA-061 plug-in contract. It serializes commands
  at the pinned 100 Hz SDK cadence, applies configurable latency without
  moving the 500 Hz physics clock, and fails closed on sequence regression,
  queue overflow, or non-finite data.
- The source-bound `rma062_electrical_controller_v1` contract maps the
  custom body XC330-M288-PG proxy, six XL330-M288-T Stewart motors, and two
  XL330-M077-T antenna motors to distinct noncalibrated role baselines.
- Position/velocity encoders and targets are quantized from documented
  Dynamixel units. Position, velocity, and torque modes are bounded by
  profile limits, a linear torque-speed envelope, voltage scaling, and a
  current-derived torque ceiling.
- The model uses the documented 3.7-6.0 V servo domain and does not confuse
  the robot's 6.8-7.6 V input with an undocumented internal servo rail.
  Continuous-current ratio, peak-current duration, latency, and controller
  gains remain explicit engineering estimates rather than calibrated claims.
- Torque disable returns zero motor torque/current while passive gravity
  remains active. Voltage faults are transient; over-current,
  over-temperature, encoder, communication, and model-rejected faults latch.
- Hosted run `30605184722` passed exact generation, eight schema/failure
  tests, Unity/calibration rejection, GNU 13.3 strict warnings, ASan/UBSan,
  and the complete behavioral CTest suite on exact commit
  `699c7b0adcc56263b307b76cc24b4f642dbe5f04`.
- Detailed design and evidence are in
  `docs/architecture/ELECTRICAL_CONTROLLER_BASELINE.md` and
  `docs/validation/RMA_062_ELECTRICAL_CONTROLLER_BASELINE_VALIDATION_2026-07-30.md`.

## RMA-063 — Implement friction, backlash, and compliance models

**Status:** Complete (2026-07-30)

- [x] Add Coulomb and viscous friction.
- [x] Add stiction/breakaway behavior without numerical chatter.
- [x] Add backlash/hysteresis state that handles direction reversal.
- [x] Add configurable gear/joint compliance where supported.
- [x] Add parameter-identification hooks.
- [x] Keep each effect independently disableable for experiments.

**Acceptance criteria**

- [x] Direction-reversal tests show expected dead-zone behavior.
- [x] Disabling each effect returns to the prior baseline.
- [x] Parameters are never silently copied between dissimilar motor types.

**Completion evidence**

- `MechanicalServoModel` is a Unity-independent native C++17 decorator
  around the RMA-061 `ServoModel` interface and can wrap the RMA-062
  electrical/controller implementation without changing the public C ABI.
- Role-specific body-yaw, Stewart, and antenna parameter sets add Coulomb
  and viscous friction, breakaway/stiction hysteresis, backlash play, and
  bounded torsional compliance. Every scalar retains explicit evidence and
  no value is labeled calibrated.
- The stiction state uses separate entry/exit speed thresholds; the backlash
  play operator retains position output through direction reversal until the
  opposite half-width is crossed; compliance torque and elastic deflection
  are stateful and bounded.
- Friction, stiction, backlash, and compliance are independently switchable.
  The all-disabled configuration preserves the wrapped command and complete
  step result exactly, and configuration changes clear transient experiment
  state.
- Copyable per-step identification samples and bounded accumulators expose
  electrical/compliance/friction/output torque, reversal count, stuck count,
  and maximum elastic deflection without invoking callbacks on the physics
  thread.
- The deterministic generator rejects missing evidence, calibrated claims,
  invalid breakaway/stiction ordering, unit drift, cross-role bindings, and
  an identical complete parameter fingerprint copied between dissimilar
  actuator roles.
- Hosted run `30606712074` passed exact generation, eight schema/failure
  tests, Unity/calibration rejection, GNU 13.3 strict warnings, ASan/UBSan,
  and the complete mechanical behavior CTest on exact commit
  `a15a1154e62a95999b482ed6b2e6f62f51379929`.
- Detailed design and evidence are in
  `docs/architecture/MECHANICAL_EFFECTS_BASELINE.md` and
  `docs/validation/RMA_063_MECHANICAL_EFFECTS_BASELINE_VALIDATION_2026-07-30.md`.

## RMA-064 — Add power and thermal model

**Status:** Complete (2026-07-30)

- [x] Model shared supply voltage and source impedance.
- [x] Compute voltage sag under simultaneous load.
- [x] Add per-servo thermal state and configurable derating/shutdown.
- [x] Expose estimated current, voltage, and temperature in state/diagnostics.
- [x] Add fault-clear rules that match the chosen fidelity model.

**Acceptance criteria**

- [x] Simultaneous high-load commands cannot unrealistically use independent unlimited peak torque.
- [x] Thermal shutdown is visible and does not silently re-enable.

**Completion evidence**

- `PowerThermalModel` is a Unity-independent native C++17 fleet-level
  coordinator for nine wrapped `ServoModel` channels. It uses one common
  bus voltage, aggregates all requested current, enforces one shared source
  budget, and applies source-impedance voltage sag without actuator-order
  dependence.
- The source-bound `rma064_power_thermal_v1` contract defines a visibly
  noncalibrated 5 V, 0.12 ohm, 5 A shared-bus engineering baseline and
  distinct body-yaw, Stewart, and antenna thermal parameter sets. It does
  not mistake the documented 6.8-7.6 V robot input for the unidentified
  internal Dynamixel rail.
- Per-servo lumped thermal state uses manufacturer-derived winding
  resistance, `I^2 R` heating, ambient cooling, role-specific thermal
  resistance/capacitance estimates, linear warning-to-shutdown derating,
  and latched zero-torque/current shutdown.
- Cooling never silently re-enables a channel. Explicit clear succeeds only
  below the recovery threshold with torque disabled, performs a safe wrapped
  model reset, removes the thermal bit, and preserves other observed faults.
- Per-channel diagnostics expose requested/delivered current, shared voltage,
  temperature, heating/cooling power, derating, latch, and faults; bus
  diagnostics expose source/evaluation/final voltage, voltage drop,
  aggregate current, allocation scales, and undervoltage.
- Hosted run `30607760504` passed exact generation, eight schema/failure
  tests, Unity/calibration rejection, GNU 13.3 strict warnings, ASan/UBSan,
  and the complete shared-current, sag, thermal derating, shutdown, explicit
  clear, diagnostics, and role-mismatch CTest on exact commit
  `a9a8d4b172a8484cc01167051d5779ce892e6355`.
- Detailed design and evidence are in
  `docs/architecture/POWER_THERMAL_BASELINE.md` and
  `docs/validation/RMA_064_POWER_THERMAL_BASELINE_VALIDATION_2026-07-30.md`.

## RMA-065 — Add collision and hard-stop model

**Status:** Complete (2026-07-31)

- [x] Audit existing collision geometry.
- [x] Add coarse validated collision shapes for motor arms, rods, moving platform, head shell, body shell, and antennas where required.
- [x] Add mechanical hard stops distinct from soft command limits.
- [x] Expose contact pairs, impulses/forces, and overload events.
- [x] Benchmark contact cost on Android.

**Acceptance criteria — dynamics baseline gate**

- [x] Representative internal and external contacts are stable.
- [x] Invalid commands cannot pass through hard stops without a reported fault.
- [x] Collision complexity remains within the measured device budget.

**Completion evidence**

- The immutable pinned source model now generates 17 named coarse collision
  primitives plus the retained source shell colliders, producing 25 active
  collision geoms across 17 bodies and 9 explicit limited joints.
- Shell, moving, and external masks are explicit. Topology exclusions are
  source-bound and validated exactly; neutral simulation remains contact-free.
- Soft actuator command ranges are inset from separate hard joint ranges.
  Yaw and antenna outward-motion trials observe their limit constraints and
  remain inside the hard range with zero MuJoCo warnings.
- State-format-v2 diagnostics expose contact geom/body pairs, position,
  normal, penetration, normal/tangent force, impulse, contact classification,
  overload flags, hard-stop observations, events, and health flags while the
  state-format-v1 ABI remains compatible.
- Permanent run `30654822714` passed schema/failure tests, strict native
  compilation, ASan/UBSan, real MuJoCo contact and hard-stop validation,
  state-v2 telemetry, Android API-26 ARM64 build, AArch64 verification, and
  the physical LG-H872 benchmark on exact implementation commit
  `08bf637a12dbe77591d3827412a752d3d4e28fba`.
- The phone completed 50,000 source and 50,000 enhanced steps with zero
  warnings and zero penetration. Realtime factors were `9.180208968594009`
  and `9.97500021157112`; enhanced p95 was `222.2909824922681` us and the
  p95 overhead ratio was `-0.06812249472499832`, below the `0.35` budget.
- Collision shapes, thresholds, and antenna ranges remain explicit
  engineering estimates and are not labeled calibrated.
- Detailed evidence is in
  `docs/validation/RMA_065_COLLISION_HARD_STOP_VALIDATION_2026-07-31.md`.

---

# Phase 8 — Calibration and validation infrastructure

## RMA-070 — Define calibration data schema

**Status:** Complete (2026-07-31)

- [x] Add versioned JSON/CBOR/Parquet-compatible schemas for command, joint, current/load, voltage, IMU, external pose, force, and temperature samples.
- [x] Include monotonic timestamps and clock/source metadata.
- [x] Include robot identity, firmware, register configuration, ambient conditions, and dataset hash.
- [x] Validate untrusted imports with size and range limits.

**Completion evidence**

- `rma070_calibration_dataset_v1` defines a strict JSON envelope and all eight
  sample tables. The companion column manifest maps the same records to CBOR
  and one Parquet table per stream with fixed-size vectors and nullable
  optional fields.
- The validator rejects unknown members, duplicate identifiers, non-finite
  values, nonmonotonic timestamps, sequence reuse, invalid ranges, malformed
  hashes, missing clock mappings, false synchronization claims, and resource
  ceilings before accepting an imported dataset.
- Canonical SHA-256 covers the complete dataset with only the self-referential
  digest field omitted. Robot register configuration and schema files carry
  independent hashes.
- Synthetic fixtures cover every sample type without claiming physical
  calibration. Focused tests and CLI validation passed in workflow run
  `30662958335` for implementation commit `bba44600441165bc9b264ee211e7db25d2ababc4`.
- Detailed design and evidence are in
  `docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md` and
  `docs/validation/RMA_070_CALIBRATION_DATA_SCHEMA_VALIDATION_2026-07-31.md`.

## RMA-071 — Build calibration capture tooling

**Status:** Complete (2026-07-31)

- [x] Create tooling to capture physical Reachy telemetry.
- [x] Support external camera/pose data import.
- [x] Support force/torque test data import.
- [x] Synchronize sources or estimate clock offset explicitly.
- [x] Never treat unsynchronized data as synchronized without uncertainty metadata.

**Completion evidence**

- The capture CLI consumes bounded JSONL from a file or live standard input,
  preserving source timestamps, clock IDs, immutable stream metadata, source
  hashes, firmware/register metadata, and the final dataset hash.
- Strict external-pose and force/torque CSV importers reject header drift,
  malformed or excessive rows, and undeclared source clocks.
- The clock estimator uses the median paired-event offset and maximum residual
  as conservative uncertainty. Excess uncertainty fails closed unless the
  caller explicitly accepts an `unsynchronized` result with nonzero
  uncertainty.
- Synthetic integration tests exercise all eight stream types, metadata-drift
  failure, CSV failure, synchronized and unsynchronized clock outcomes, and
  final RMA-070 validation. They passed in workflow run `30662958335` for
  implementation commit `bba44600441165bc9b264ee211e7db25d2ababc4`.
- No physical Reachy dataset was fabricated. Physical unit capture and a
  calibrated held-out profile remain RMA-074 work.
- Detailed evidence is in
  `docs/validation/RMA_071_CALIBRATION_CAPTURE_TOOLING_VALIDATION_2026-07-31.md`.

## RMA-072 — Implement experiment runner

**Status:** Complete (2026-07-31)

- [x] Implement scripted unloaded sweeps.
- [x] Implement gravity-loaded static-pose tests.
- [x] Implement step and frequency-response tests.
- [x] Implement backlash direction-reversal tests.
- [x] Implement torque-disabled/free-decay tests.
- [x] Implement multi-actuator and warm/cold tests.
- [x] Add safety notes for physical test execution.

**Completion evidence**

- `rma072_calibration_experiment_plan_v1` defines a versioned, canonical-hash
  bound plan for robot identity, monotonic timing, actuator soft/profile
  limits, resource ceilings, live electrical/thermal abort limits, and ordered
  experiments.
- The deterministic compiler covers unloaded sweeps, gravity-loaded static
  poses, step response, sinusoidal frequency response, backlash reversals,
  torque-disabled free decay, simultaneous multi-actuator commands, and
  warm/cold thermal cycles. The committed synthetic smoke plan compiles to 347
  actions over 23.4 seconds with schedule SHA-256
  `96fa8b8131765b0f1d7c3ef61ba95c8038c5ee6cf52fcfd615902df776bfdcfd`.
- Dry-run output includes a versioned manifest, the complete action schedule,
  and RMA-070-shaped command JSONL. The permanent gate imported 312 generated
  command samples through the RMA-071 capture tool and validated dataset
  SHA-256
  `138652fd99c1ccc54e081c6fca81260cf09681f35d4f144617751aaa5bdc035b`.
- Physical execution remains behind the `ExperimentAdapter` boundary. Exact
  plan hash, robot identity, operator presence, workspace clearance,
  emergency-stop verification, acknowledgement, and explicit motion
  authorization are required before startup. Voltage, current, temperature,
  emergency-stop availability, and robot fault state are checked before every
  action; any violation or adapter exception invokes emergency stop and
  records an aborted run.
- The schedule explicitly disables torque for free-decay release and for every
  used actuator during final safe shutdown. The CLI has no generic physical
  motion switch.
- Permanent workflow run `30667664533` passed Python compilation, all 39
  calibration regression tests, all eight experiment families, the dry-run
  CLI, the RMA-070/RMA-071 bridge, and evidence hashing on exact implementation
  commit `de8b95eee5ffdae90c9409fa49887d3d603d6913`.
- Artifact `8807575014`,
  `rma072-experiment-runner-evidence-de8b95eee5ffdae90c9409fa49887d3d603d6913`,
  has ZIP SHA-256
  `be4aaea8262b240d10a57f6c75f63666a83eac5202c352657a60c921f2a9bb06`.
- The committed fixture and automated execution are synthetic orchestration
  evidence only. A production Reachy adapter and physical unit data remain
  RMA-074 work.
- Detailed design, safety, and evidence are in
  `docs/architecture/CALIBRATION_EXPERIMENT_RUNNER.md`,
  `docs/operations/CALIBRATION_EXPERIMENT_SAFETY.md`, and
  `docs/validation/RMA_072_EXPERIMENT_RUNNER_VALIDATION_2026-07-31.md`.

## RMA-073 — Implement parameter fitting and held-out validation

**Status:** Complete (2026-07-31)

- [x] Separate training/fitting datasets from held-out validation datasets.
- [x] Fit friction, backlash, latency, controller, voltage, compliance, and thermal parameters where data supports them.
- [x] Report confidence or sensitivity.
- [x] Generate a signed/hashed calibration profile manifest.
- [x] Reject profiles incompatible with model or simulator versions.

**Completion evidence**

- `rma073_calibration_fit_plan_v1` binds each RMA-070 dataset by canonical
  SHA-256 and immutable `fitting` or `heldout` role. IDs, paths, and hashes
  cannot be reused across the split, all datasets must describe the same robot
  and register configuration, and unsafe or out-of-root paths fail closed.
- The fitting stage consumes only fitting-role datasets and freezes its output
  before held-out data is loaded. Friction, backlash, command latency,
  controller gains, supply voltage/source impedance, compliance, and thermal
  parameters are estimated only when their required streams and observation
  counts exist; unsupported families retain an explicit reason and no value.
- Every fitted family reports observation count, training error or robust
  spread, a dataset-qualified confidence label, and leave-one-out or robust
  sensitivity. Held-out validation records the independent metric, threshold,
  sample count, and pass/fail result for each supported family.
- `rma073_calibration_profile_manifest_v1` preserves exact fit-plan, fitting
  dataset, held-out dataset, model, MuJoCo, ABI, and RMA-061 through RMA-064
  contract identities. It carries a canonical SHA-256 and Ed25519 signature.
  Verification rejects content drift, the wrong public key, or any exact
  compatibility mismatch.
- RMA-073 can emit only `fit_candidate_unapproved` manifests with
  `calibrated=false`; attempts to sign a calibrated claim fail closed. The
  committed key pair is an explicitly non-secret synthetic test fixture.
- Deterministic synthetic training and held-out data validate all seven
  estimators without claiming physical Reachy measurements. Physical data,
  unit-specific fitting, profile approval, and the calibrated label remain
  RMA-074 work.
- Detailed design and accepted automated evidence are in
  `docs/architecture/CALIBRATION_PARAMETER_FITTING.md` and
  `docs/validation/RMA_073_PARAMETER_FITTING_VALIDATION_2026-07-31.md`.

## RMA-074 — Produce first calibrated profile

- [ ] Acquire data from a physical Reachy Mini.
- [ ] Fit a unit-specific profile.
- [ ] Run held-out motions and compare position, orientation, settling, overshoot, current, decay, and contact metrics.
- [ ] Publish a validation report in the repository without private/unredistributable data.

**Acceptance criteria — calibration gate**

- [ ] A real calibration profile and held-out report exist.
- [ ] The UI labels uncalibrated and calibrated modes accurately.
- [ ] No mature accuracy claim is made unless the corresponding threshold passes.

---

# Phase 9 — Main Unity application shell and fixed presentation

## RMA-080 — Create application state architecture

**Status:** Complete (2026-07-31)

- [x] Define app-level services and dependency construction.
- [x] Separate simulation, camera, audio, provider, perception, behavior, persistence, and UI interfaces.
- [x] Ensure services are explicitly initialized and disposed.
- [x] Add a top-level health/status model.

**Completion evidence**

- `ReachyApplicationComposition` validates exactly one service for each of the
  eight application boundaries, rejects incomplete/duplicate/cyclic graphs,
  constructs in deterministic dependency order, and restricts factories to
  explicitly declared dependencies.
- `ReachyApplicationHost` separates construction from initialization, rolls
  back failures in reverse order, disposes exhaustively and idempotently, and
  publishes immutable application/service health snapshots with monotonic
  revisions and required-versus-optional degradation rules.
- The shared contracts live under `Assets/ReachyMini/Runtime/Core/Application`
  without a Unity dependency. `ReachyApplicationHostBehaviour` is the narrow
  explicit Unity lifecycle bridge and has no fallback composition.
- The managed warnings-as-errors contract covers valid dependency order,
  malformed graphs, undeclared dependencies, factory mismatches, startup
  rollback, exhaustive disposal, health aggregation, immutable snapshots, and
  one-shot lifecycle behavior.
- Self-hosted run `30677109292` passed 71 Unity edit-mode tests, one play-mode
  test, the ARM64 API-26 IL2CPP build, installed LG-phone native lifecycle
  acceptance, and installed authoritative-rendering acceptance on exact commit
  `da0418c95bd1278976b5cacc4683775a78f1a395`.
- Detailed design and evidence are in
  `docs/architecture/APPLICATION_STATE_ARCHITECTURE.md` and
  `docs/validation/RMA_080_APPLICATION_STATE_ARCHITECTURE_VALIDATION_2026-07-31.md`.

## RMA-081 — Build the main screen

**Status:** Complete (2026-07-31)

- [x] Display Reachy using the fixed front/three-quarter camera.
- [x] Show concise state: idle, listening, transcribing, thinking, speaking, interrupted, unavailable, error.
- [x] Show active camera and local/cloud provider indicators.
- [x] Add microphone, camera selector, settings, and diagnostics controls.
- [x] Do not add orbit/pan/free-camera gestures.

**Completion evidence**

- `ReachyMainScreenStateStore` publishes immutable revisioned snapshots for the
  complete interaction-state vocabulary, active camera, provider location,
  capability availability, and mutually exclusive settings/diagnostics panels.
- `ReachyMainScreenBootstrap` installs exactly one production shell only when
  the generated Reachy root, authoritative runtime, tagged main camera, and
  fixed non-navigable presentation metadata are present. It creates no fallback
  camera or alternate scene.
- The production composition supplies all eight RMA-080 boundaries. Missing
  audio, provider, perception, and behavior capabilities are explicitly
  unavailable optional services, so the application accurately reports
  `Degraded` rather than falsely reporting `Ready`.
- Microphone and camera-selector requests surface actionable unavailable
  diagnostics. Settings and diagnostics controls open visible panels; no
  request is silently discarded.
- Static and Unity contracts reject camera-navigation paths and prove that all
  controls leave the presentation camera position, rotation, and
  `AcceptsUserNavigation == false` unchanged.
- Self-hosted run `30679297685` passed 74 Unity edit-mode tests, one play-mode
  test, the ARM64 API-26 IL2CPP build, installed LG-phone native lifecycle
  acceptance, and installed authoritative-rendering acceptance on exact commit
  `61737fe03b370181430f8ecd93a2a240cc9a47b2`.
- Detailed design and evidence are in
  `docs/architecture/MAIN_SCREEN_APPLICATION_SHELL.md` and
  `docs/validation/RMA_081_MAIN_SCREEN_VALIDATION_2026-07-31.md`.

## RMA-082 — Build settings screens

**Status:** Complete (2026-08-03)

- [x] Providers: independent ASR/TTS/LLM/VLM selection.
- [x] Camera: front/rear, preview, calibration, reprojection diagnostics.
- [x] Speech: language, voice, offline/network status.
- [x] Local model: install/import/select/delete and resource settings.
- [x] Simulation: fidelity mode, calibration profile, reset, diagnostic controls.
- [x] Privacy: cloud-bound data indicators, history/retention options.
- [x] Licenses and attribution.

**Acceptance criteria**

- [x] Every provider or capability unavailable state is visible and actionable.
- [x] Settings do not imply offline operation when a network-backed Android service is selected.

**Completion evidence**

- `ReachySettingsStateStore` publishes immutable, revisioned settings for all
  seven sections and independent ASR, TTS, LLM, and VLM selections. Android
  service and cloud choices are structurally required to declare
  `NetworkRequired`; privacy summaries identify every off-device selection.
- `ReachySettingsPersistenceApplicationService` writes schema-versioned durable
  JSON, sanitizes unsupported values, uses temporary/backup replacement, and
  quarantines invalid files with visible degraded health.
- `ReachySettingsApplicationCompositionProvider` supplies all eight RMA-080
  boundaries. A stored preference never upgrades an unavailable runtime
  integration into false ready health.
- Camera preview/calibration/reprojection and local-model package actions remain
  enabled explanatory entry points and publish explicit unavailable reasons.
  Simulation reset routes through the authoritative runtime, and all settings
  actions preserve the fixed non-navigable presentation camera.
- Hosted run `30851077541`, job `91810969892`, passed managed
  warnings-as-errors and the permanent settings-state, persistence,
  service-boundary, privacy, network-truthfulness, and fixed-camera contracts on
  exact commit `96c7113eccca7eec4afc8fb5d346a56e0782126f`.
- Self-hosted run `30851077505`, job `91811041976`, passed deterministic Unity
  import, production MuJoCo staging, all Unity edit-mode/play-mode tests, ARM64
  API-26 IL2CPP build and verification, installed LG-phone lifecycle acceptance,
  installed authoritative-rendering acceptance, and all evidence uploads on the
  same exact commit.
- The validation artifacts and accepted evidence are recorded in
  `docs/architecture/SETTINGS_ARCHITECTURE.md` and
  `docs/validation/RMA_082_SETTINGS_VALIDATION_2026-08-03.md`.

---

# Phase 10 — Android CameraX bridge

## RMA-090 — Implement camera permissions and capability discovery

**Status:** Complete (2026-08-03)

- [x] Request camera permission only when needed.
- [x] Enumerate front/rear camera availability and characteristics.
- [x] Report supported analysis resolutions and orientations.
- [x] Record camera intrinsics when available; define calibration fallback when unavailable.
- [x] Handle permission denial, permanent denial, revocation, and camera-in-use errors.

**Completion evidence**

- `ReachyAndroidCameraDiscovery` keeps startup at `NotRequested`, requests
  access only from an explicit camera action, persists prior grant history,
  and publishes denied, permanently denied, revoked, unsupported, faulted,
  and camera-availability states without fabricating frame readiness.
- The Android Camera2 bridge enumerates lens facing, sensor orientation,
  hardware level, YUV analysis sizes, active-array geometry, platform
  intrinsics when present, and a documented uncalibrated fallback when
  calibration metadata is absent.
- Settings and the main application shell expose permission, inventory,
  availability, calibration provenance, and actionable unavailable states
  while preserving the fixed presentation camera. Frame acquisition remains
  intentionally unavailable until RMA-091.
- Hosted run `30874543837`, job `91883241667`, passed the permanent managed
  permission, inventory, error-state, and integration-policy contracts on
  exact implementation commit
  `8ce02564bed817ed215478180ee1c4468def8baa`.
- Self-hosted run `30874543829`, job `91883242040`, passed Unity tests,
  ARM64 API-26 IL2CPP build and verification, installed LG-H872 physical-
  device camera acceptance, lifecycle acceptance, authoritative-rendering
  acceptance, and all evidence uploads on that same commit.
- The physical device proved startup without a permission request,
  `NotRequested -> Granted -> Revoked` persistence, three available cameras
  (two rear and one front), valid orientations and YUV analysis sizes, and
  explicit calibration fallback because that device did not expose usable
  platform intrinsics.
- Detailed design and evidence are in
  `docs/architecture/ANDROID_CAMERA_CAPABILITY_DISCOVERY.md` and
  `docs/validation/RMA_090_CAMERA_DISCOVERY_VALIDATION_2026-08-03.md`.

## RMA-091 — Implement CameraX frame acquisition

**Status:** Complete (2026-08-04)

- [x] Bind preview and `ImageAnalysis` lifecycle-aware use cases.
  (2026-08-16: the discarded-surface `Preview` use case was removed and the binding is now
  `ImageAnalysis`-only. The SM-A546E vendor camera HAL attaches an internal ZSL stream to
  the IMPL_DEF preview stream and aborts on an undersized ZSL crop, rebooting the device;
  no consumed frame ever came from that surface. See
  docs/validation/RMA_184_SM_A546E_CAMERA_HAL_FINDING_2026-08-16.md.)
- [x] Use a bounded backpressure strategy that discards stale analysis frames.
- [x] Carry timestamp, sensor orientation, lens facing, crop, pixel format, and intrinsics with each frame.
- [x] Close every `ImageProxy` exactly once.
- [x] Avoid copying to CPU formats unless a consumer requires it.
- [x] Support explicit front/rear switching with orderly teardown.

**Tests**

- [x] rapid start/stop;
- [x] repeated front/rear switch;
- [x] pause/resume;
- [x] permission revoke;
- [x] analyzer overrun;
- [x] device rotation;
- [x] camera unavailable.

**Completion evidence**

- CameraX 1.6.1 binds an exact-camera `ImageAnalysis` use case to an explicit
  lifecycle owner (originally alongside a discarded-surface `Preview`, removed
  2026-08-16 for the SM-A546E vendor-HAL defect recorded in
  docs/validation/RMA_184_SM_A546E_CAMERA_HAL_FINDING_2026-08-16.md). Analysis remains `YUV_420_888`, uses
  `STRATEGY_KEEP_ONLY_LATEST`, and publishes metadata without accessing image
  planes or copying pixels into Unity.
- Generation and session identities reject callbacks from stopped or replaced
  streams. `ImageProxy.close()` has one explicit call site in the analyzer
  `finally` block, and orderly teardown clears the analyzer, unbinds both use
  cases, destroys lifecycle state, closes the private preview surface, and
  stops its executor.
- Permanent managed, Unity, and static contracts cover rapid start/stop,
  repeated front/rear switching, pause/resume, revocation, stale/analyzer
  metadata, unavailable cameras, exact Camera2 selection, and the no-CPU-copy
  boundary retained for RMA-092.
- Self-hosted run `30934825724`, job `92078267747`, passed 85 edit-mode tests,
  one play-mode test, ARM64/API-26 IL2CPP build and verification, RMA-090
  discovery, RMA-091 physical acquisition, RMA-022 lifecycle acceptance,
  authoritative rendering, and APK upload on exact implementation commit
  `25b496917d47f53e217d67ae7d996b91fa5dce81`.
- The LG-H872/API-26 sequence recorded four sessions and 58 frame observations,
  rear and front frames, continuous analyzer progress, pause/resume recovery,
  orderly stop/restart and switching, output rotation changing from 90 to 0
  degrees after display rotation, zero stale/faulted transitions, and final
  `PermissionRevoked` state.
- Camera evidence artifact `8902852311` has digest
  `sha256:5e5251a39a8dc14bf7da91f828ad0d56cbe0ee1667a49950c4273f671a73e462`.
- Detailed design and evidence are in
  `docs/architecture/ANDROID_CAMERAX_FRAME_ACQUISITION.md` and
  `docs/validation/RMA_091_CAMERA_ACQUISITION_VALIDATION_2026-08-04.md`.

## RMA-092 — Create GPU texture bridge

**Status:** Complete (2026-08-04)

- [x] Convert CameraX frames to a Unity-consumable GPU texture with minimal copies.
- [x] Correct YUV conversion, color range, rotation, and front-camera mirroring.
- [x] Maintain timestamp correspondence.
- [x] Add a CPU reference conversion for tests only.

**Acceptance criteria**

- [x] Preview and analysis show correct orientation and color on representative devices.
- [x] No stale or closed camera buffer is sampled.

**Completion evidence**

- CameraX `YUV_420_888` planes are packed once into a bounded three-slot
  detached direct-buffer ring. Unity leases tokenized Y/U/V buffers, uploads
  reusable linear R8 textures, and performs BT.601/BT.709 limited/full-range
  conversion, crop, rotation, and front mirroring into one authoritative RGB
  `RenderTexture`; preview and analysis share that output without a production
  CPU RGB copy or GPU readback.
- Generation, session, camera, sequence, timestamp, and lease-token checks
  reject stale frames and prevent overwrite while leased. The CameraX analyzer
  remains the sole `ImageProxy` owner and closes it exactly once after detached
  publication. Stop, switch, pause, revocation, fault, and destruction clear
  sampleable RGB state.
- CPU known-answer conversion remains restricted to tests/development builds.
  Permanent managed and static contracts cover plane sizes, stride-aware packing,
  crop/rotation/mirror mapping, timestamp correspondence, shader retention,
  direct-buffer release, production no-readback policy, and physical evidence
  requirements.
- Hosted run `30952901855`, job `92139068169`, passed the permanent
  RMA-091/RMA-092 contract on exact implementation commit
  `21cdff23da91fd53bdd81b689f93d78e395d7c99`.
- Self-hosted run `30952901895`, job `92139068851`, passed Unity tests,
  ARM64/API-26 IL2CPP build and verification, RMA-090 discovery, RMA-091
  acquisition, RMA-092 physical GPU texture acceptance, RMA-022 lifecycle,
  authoritative rendering, and APK upload on the same exact commit.
- The LG-H872/API-26 Vulkan/Adreno 530 evidence changed rear output rotation
  from 90 to 0 degrees, proved neutral dark rear YUV values converted to matching
  black only after a deterministic physical-GPU shader probe produced opaque
  RGB 0–255, and produced a real non-uniform 1280x960 front RGB capture with
  front-mirror metadata and exact timestamp correspondence. Zero stale texture
  frames were accepted.
- RMA-092 artifact `8910067968` has digest
  `sha256:b3fa33bc814c153a2440d6928f04e64fc15f04410090d37e46c7b397aa7a8394`.
- Detailed architecture and exact evidence are in
  `docs/architecture/ANDROID_CAMERA_GPU_TEXTURE_BRIDGE.md` and
  `docs/validation/RMA_092_GPU_TEXTURE_BRIDGE_VALIDATION_2026-08-04.md`.

---

# Phase 11 — Level 1 rotation-only Reachy-eye reprojection

## RMA-100 — Define coordinate systems and calibration

**Status:** Complete (2026-08-04)

- [x] Document Android sensor, camera image, Unity, MuJoCo, and virtual Reachy camera axes.
- [x] Define neutral relationship between phone camera forward direction and Reachy neutral camera direction.
- [x] Define front-camera mirror handling separately from physical camera orientation.
- [x] Define phone and virtual Reachy intrinsic matrices.
- [x] Add camera calibration persistence and versioning.

**Completion evidence**

- `docs/architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md` is the normative
  coordinate contract for RMA-101 through RMA-104. It fixes column-vector
  composition; Android source and RMA-092 normalized pixel spaces; phone and
  Reachy optical frames; Unity and MuJoCo frames; proper basis conversions; and
  the rotation-only homography operands.
- `ReachyCameraReprojectionCalibration.cs` provides finite/invertible 3x3 math,
  normalized proper rotations, crop-then-rotation-then-mirror image
  normalization, complete phone and virtual-Reachy projection matrices,
  explicit provenance, versioned profiles, exact fail-closed selection, and
  neutral homography construction.
- Front-camera preview mirroring is retained as a pixel-space reflection and is
  never inserted into physical orientation. Uncalibrated estimates remain
  explicitly labeled and are never reported as calibrated.
- `ReachyCameraCalibrationPersistenceStore` uses the independent versioned file
  `reachy-camera-calibration-v1.json`, atomic replacement with backup, and
  quarantine of invalid or unsupported data without installing a silent
  default. `ReachySettingsPersistenceApplicationService` owns the calibration
  boundary and reports settings and calibration health together.
- Permanent hosted run `30959984801`, job `92161575744`, passed the managed
  RMA-090/RMA-091/RMA-100 contracts and the static integration/truthfulness gate
  on exact implementation SHA `e809e98522585dd207de1f0bef831a1fcdd7c462`.
- Self-hosted run `30959984789`, job `92161627492`, passed generated presentation,
  MuJoCo staging, Unity tests, ARM64/API-26 APK build and verification, RMA-090,
  RMA-091, RMA-092, RMA-022 lifecycle, authoritative rendering, all evidence
  uploads, APK upload, and final status publication on the same exact SHA.
- The uploaded Unity result artifact `8912623164` has digest
  `sha256:7ec9253df8f7a09c109f58349e5cbbce826932e7e28703116f05bce2fa598a6e`
  and records 102/102 edit-mode tests passing, including matrix/quaternion
  direction preservation, versioned calibration round-trip, unsupported-schema
  quarantine without silent calibration, and settings-service ownership.
- Detailed evidence is recorded in
  `docs/validation/RMA_100_CAMERA_CALIBRATION_VALIDATION_2026-08-04.md`.

## RMA-101 — Compute relative rotation from MuJoCo state

**Status:** Complete (2026-08-04)

- [x] Extract the actual head-camera rotation from the authoritative MuJoCo body/site transform.
- [x] Do not substitute requested target orientation for actual simulated orientation.
- [x] Remove the translational component for Level 1 only.
- [x] Combine device/camera orientation and simulated head rotation consistently.
- [x] Unit-test signs for yaw, pitch, and roll.

**Completion evidence**

- `ReachyCameraMujocoOpticalBinding` pins the upstream Reachy Mini MJCF,
  named `camera_optical` site, `eye_camera`, canonical generated body
  `__body_15` / MuJoCo body ID 15, expected body layout, fixed
  camera-body-to-optical rotation, and neutral optical frame.
- `ReachyAuthoritativeCameraRotationSource` consumes only the immutable
  solved authoritative body pose published from MuJoCo. Requested head
  targets, Unity presentation transforms, and interpolation are not inputs.
- `ReachyCameraRelativeRotationCalculator` composes actual optical
  rotation, the selected RMA-100 neutral phone-to-Reachy calibration, and
  an explicit timestamped phone orientation. Its API accepts no body
  position, so Level 1 translation is excluded by construction.
- Capture fails closed for model/calibration mismatch, zero model hash,
  layout drift, unavailable state, missing or duplicate camera body,
  invalid quaternion/rotation, and stale sequence within one continuity.
  A continuity change permits a sequence reset.
- Managed and Unity tests cover positive optical yaw, pitch, and roll
  signs; actual-versus-requested orientation; translation independence;
  generated body binding; stale rejection; same-source continuity reset;
  and missing/duplicate-body failures.
- Permanent run `30964753440`, job `92176195712`, passed managed camera
  contracts, exact pinned-MJCF hierarchy and hash verification,
  authoritative-state integration policy, sign contracts, fail-closed
  behavior, and repository cleanliness on exact implementation SHA
  `ffeb02af405cac3131a4d69fe816fdf3e6908db7`.
- Hosted CI run `30964753430` passed Reachy-model, native, static,
  Android, and managed jobs on the same exact SHA.
- Self-hosted run `30964753429` passed on unchanged-SHA rerun job
  `92182165671`: 106/106 Unity edit-mode tests, 1/1 play-mode test,
  ARM64/API-26 APK build and verification, RMA-090, RMA-091, RMA-092,
  RMA-022 lifecycle, authoritative rendering, evidence uploads, APK
  upload, and final status publication.
- The first device attempt encountered a one-off CameraX critical error
  during RMA-091 after Unity and APK validation had passed. The unchanged
  implementation SHA reran successfully through all downstream gates;
  no source or recovery-path change was justified.
- Detailed architecture and exact evidence are in
  `docs/architecture/AUTHORITATIVE_CAMERA_RELATIVE_ROTATION.md` and
  `docs/validation/RMA_101_AUTHORITATIVE_CAMERA_ROTATION_VALIDATION_2026-08-04.md`.

## RMA-102 — Implement GPU homography warp

**Status:** Complete (2026-08-05)

- [x] Compute:

```text
H = K_reachy * R_reachy_phone * inverse(K_phone)
```

- [x] Pass the inverse mapping required by the shader to avoid holes from forward splatting.
- [x] Sample only valid source coordinates.
- [x] Emit transformed color and a validity mask.
- [x] Support output resolution independent from source resolution.
- [x] Avoid CPU readback for local trackers that can consume GPU input.

**Completion evidence**

- `ReachyCameraHomographyCalculator` builds the exact phone-to-Reachy
  homography and its inverse from RMA-100 calibration and timestamp-matched
  RMA-101 authoritative rotation. Camera, model, dimensions, timestamps,
  rotation, and matrix round-trip mismatches fail closed.
- `ReachyCameraHomographyWarpRenderer` performs inverse GPU gathering into
  reusable color and validity render textures. Invalid rays are rejected before
  source sampling, output resolution is independent, and runtime code performs
  no image readback.
- The validation harness was repaired to require a real graphics device rather
  than accepting `NullGfxDevice` evidence. Shader math and the `> 0.9` identity
  threshold were not weakened, and active render targets are unbound before
  release.
- Hosted CI run `31008738003` passed static, native, sanitizer, managed,
  Reachy-model, and Android jobs on exact implementation SHA
  `b5aabd8f4e937867ec72e75539a96a6182ecd89b`.
- Self-hosted run `31009555103` passed OpenGL Core Unity tests on `kawa`
  (`110/110` EditMode and `1/1` PlayMode), ARM64 API-26 APK build and
  verification, RMA-090/RMA-091/RMA-092 physical camera acceptance, RMA-022
  lifecycle acceptance, authoritative rendering, evidence uploads, and final
  status publication on the same exact SHA.
- Detailed architecture and evidence are in
  `docs/architecture/GPU_HOMOGRAPHY_WARP.md` and
  `docs/validation/RMA_102_GPU_HOMOGRAPHY_WARP_VALIDATION_2026-08-04.md`.

## RMA-103 — Implement valid-coverage policy

**Status:** Complete (2026-08-05)

- [x] Calculate valid coverage percentage.
- [x] Ensure invalid pixels are never filled from prior frames.
- [x] Propagate validity metadata to tracking, VLM, world model, behavior, and diagnostics.
- [x] Define thresholds for normal, degraded, and unusable coverage.
- [x] Ensure the behavior planner can stop vision-driven turning before coverage becomes unusable.

**Completion evidence**

- `ReachyCameraValidCoverageCalculator` counts the exact integer output
  pixels accepted by the same five affine half-plane predicates used by
  the RMA-102 shader. It uses bounded row-interval searches, performs no
  GPU readback or full-image CPU scan, and retains camera, calibration,
  timestamp, model, authoritative-sequence, and continuity identity.
- `ReachyCameraCoverageStateMachine` rejects stale ordering, timestamp
  conflict, model/camera identity drift, and conflicting duplicate
  coverage. New camera sessions or simulation continuities explicitly
  permit sequence restart; rejected publication clears color, validity,
  and coverage rather than retaining a previous-frame fallback.
- The engineering baseline uses hysteresis: unusable entry at `<= 25%`,
  unusable exit at `>= 35%`, normal exit below `65%`, and normal entry at
  `>= 75%`. Vision-driven turning is stopped at `<= 35%`, before the
  image reaches unusable coverage.
- Immutable frame metadata exposes validity-mask availability, coverage
  class, observation eligibility, degradation disclosure, and the
  planner-facing turning-stop signal for future tracking, VLM, world-model,
  behavior, and diagnostics interfaces.
- Permanent workflow run `31014441081` passed managed camera contracts,
  exact shader-predicate coverage, hysteresis, stale/conflict rejection,
  continuity reset, consumer policy, no-readback rules, fail-closed
  clearing, and repository cleanliness on exact implementation SHA
  `9bcacfec7d4395e3e83e5f599402066f0d184718`.
- Hosted CI run `31014441299` passed static, managed warnings-as-errors,
  native and sanitizer, Android, and pinned Reachy-model jobs on the same
  exact SHA.
- Self-hosted Local Unity Android Validation run `31014441080`, attempt 2,
  passed `112/112` EditMode and `1/1` PlayMode tests under OpenGL Core
  with Mesa llvmpipe, ARM64 API-26 APK build and verification, RMA-090,
  RMA-091, RMA-092, RMA-022 lifecycle, authoritative rendering, all
  evidence uploads, APK upload, and final commit-status publication.
- Attempt 1 encountered a CameraX `camera_fatal_error` during the second
  rear-camera start in RMA-092 after valid Vulkan output and zero stale
  frames. The unchanged-SHA attempt 2 passed the complete sequence, so no
  production fallback or source change was justified.
- Detailed architecture and evidence are in
  `docs/architecture/CAMERA_VALID_COVERAGE_POLICY.md` and
  `docs/validation/RMA_103_VALID_COVERAGE_POLICY_VALIDATION_2026-08-05.md`.

## RMA-104 — Build reprojection test suite

**Status:** Complete (2026-08-05)

- [x] Identity transform golden image.
- [x] Known yaw/pitch/roll synthetic grid images.
- [x] Camera-intrinsic scaling tests.
- [x] Front-camera mirroring tests.
- [x] Portrait/landscape tests.
- [x] GPU output versus double-precision CPU reference.
- [x] Invalid-mask boundary and stale-pixel tests.
- [x] Actual-versus-target head orientation test.

**Acceptance criteria — camera gate**

- [x] Actual MuJoCo head rotation changes the transformed image correctly.
- [x] X/Y/Z translation is intentionally ignored and labeled `rotation_only`.
- [x] Invalid coverage is explicit and testable.
- [x] CV/VLM receive the transformed frame, not the raw phone frame, unless a debug tool explicitly requests raw input.

**Completion evidence**

- An asymmetric deterministic image and a test-only CPU oracle cover identity,
  positive X/Y/Z rotations, nonuniform intrinsic scaling, front mirroring,
  90/270-degree orientation, invalid boundaries, stale target poisoning, and
  actual authoritative MuJoCo orientation versus a different requested target.
- The CPU oracle consumes the same float matrix payload as the Unity shader and
  performs projection in double precision. Production rendering retains no GPU
  readback. The identity homography is canonicalized only within `1e-12` of
  `0`, `1`, or `-1` and must report all `187/187` pixels valid.
- RMA-103 coverage now counts the final float shader payload, preventing
  coverage metadata from disagreeing with the emitted validity texture at
  numerical boundaries.
- `ReachyVisionFrameRoutingPolicy` requires transformed, validity-bearing,
  observation-eligible Reachy-eye frames for tracking, VLM, world-model,
  behavior, and diagnostics. Raw phone frames are limited to the distinct
  `ExplicitRawDebug` purpose.
- Repeated physical RMA-092 failures exposed that CameraX stop previously
  published `Stopped` before the device reached `CLOSED`. Java and Unity now
  preserve `Stopping`, retain teardown ownership and the camera observer until
  `CLOSED`, fail critical close errors visibly, and queue switches rather than
  racing a closing camera. No sleep, blind retry, or silent fallback was added.
- Hosted CI run `31035832714` passed static, managed warnings-as-errors, native
  and sanitizer, Android, and pinned Reachy-model jobs on accepted
  implementation SHA `90a9a5390ce8c893899779c89d035eb3262965e6`.
- Self-hosted run `31035832853`, job `92407563209`, passed real OpenGL Core
  Unity tests (`125/125` EditMode and `1/1` PlayMode), ARM64 API-26 APK build
  and verification, RMA-090, RMA-091, repaired RMA-092 rear rotation restart
  and front switch, RMA-022 lifecycle, authoritative rendering, every evidence
  upload, APK upload, and final status publication on the same SHA.
- Physical evidence recorded CameraX `CLOSED` before both subsequent starts,
  valid Vulkan rear output at 90 and 0 degrees, a non-uniform mirrored front
  capture, monotonic metadata, exact timestamp correspondence, and zero stale
  accepted or uploaded texture frames.
- Detailed design and evidence are in
  `docs/architecture/REPROJECTION_TEST_SUITE.md` and
  `docs/validation/RMA_104_REPROJECTION_TEST_SUITE_VALIDATION_2026-08-05.md`.

---

# Phase 12 — Lightweight perception, world model, and VLM

## RMA-110 — Define vision provider contracts

**Status:** Complete (2026-08-05)

- [x] Separate frame source, lightweight tracker, and semantic VLM interfaces.
- [x] Include cancellation, timeouts, capability metadata, and provider identity.
- [x] Include validity mask/coverage with requests.

Suggested managed interfaces:

```csharp
public interface IVisualTracker : IAsyncDisposable
{
    TrackerCapabilities Capabilities { get; }
    ValueTask<TrackingResult> AnalyzeAsync(
        ReachyVisionFrame frame,
        CancellationToken cancellationToken);
}

public interface IVisionLanguageProvider : IAsyncDisposable
{
    ProviderDescriptor Descriptor { get; }
    ValueTask<VisionLanguageResult> AnalyzeAsync(
        VisionLanguageRequest request,
        CancellationToken cancellationToken);
}
```


**Completion evidence**

- `ReachyMini.Perception` defines separate frame-source, lightweight-tracker,
  and semantic-VLM boundaries with explicit identities, capabilities, locality,
  bounded requests, and monotonic provider-selection epochs.
- Normal perception consumes owned transformed Reachy-eye frame leases carrying
  color, validity mask, coverage, calibration/model provenance, timestamps,
  continuity, orientation, and mirror metadata. Raw phone frames remain limited
  to `ExplicitRawDebug`.
- Cancellation, timeout, provider failure, invalid frame, unavailable provider,
  contract violation, and supersession are typed and visible. The executor does
  not retry, substitute a fallback provider, reuse stale output, or silently
  cross the raw/transformed privacy boundary.
- The managed RMA-110 suite is awaited from the project's real async `Main`.
  Fake providers, frame leases, and frame resources use deterministic
  `await using` ownership; the permanent gate rejects the former synchronous
  module-initializer bootstrap and tracked repair artifacts.
- Permanent RMA-110 run `31050417256`, job `92456020415`, passed on exact SHA
  `64587bae3b977ff16f6a9d3f7b416af0b1f64a62`.
- Hosted CI run `31050417844` passed static, managed warnings-as-errors,
  native/sanitizer, Android, and pinned Reachy-model jobs on that SHA.
- Self-hosted run `31050417574`, job `92456090409`, passed `125/125` EditMode,
  `1/1` PlayMode, ARM64 API-26 build/verification, RMA-090, RMA-091, RMA-092,
  lifecycle, authoritative rendering, every evidence upload, APK upload, and
  final status publication on that SHA.
- RMA-092 recorded CameraX `CLOSED` before both subsequent starts, physical
  Vulkan output, rear rotations at 0 and 90 degrees, mirrored front output at
  270 degrees, exact timestamp correspondence, and zero stale texture frames.
- Detailed evidence is in `docs/architecture/VISION_PROVIDER_CONTRACTS.md` and
  `docs/validation/RMA_110_VISION_PROVIDER_CONTRACTS_VALIDATION_2026-08-05.md`.

## RMA-111 — Implement on-device lightweight tracking

**Status:** Complete (2026-08-05)

- [x] Select and document ML Kit, MediaPipe, LiteRT, or another mobile-compatible approach.
- [x] Implement face/person tracking first.
- [x] Add basic object or motion tracking only if performance supports it.
  - Object and generic motion tracking remain disabled because RMA-111 has no
    measured physical-device evidence that they fit the bounded mobile path.
- [x] Convert detections to the transformed Reachy-eye coordinate system.
- [x] Do not report detections centered in invalid pixels.
- [x] Add stable local IDs and expiry.

**Completion evidence**

- The production provider uses bundled Google ML Kit face detection 16.1.7 and
  selfie segmentation 16.0.0-beta6 behind the RMA-110 `IVisualTracker`
  boundary. It consumes owned transformed Reachy-eye frames and validity
  metadata; it does not invoke a VLM or download a model at runtime.
- Managed contracts cover ownership, bounded concurrency, cancellation,
  provider failure, transformed coordinates, validity filtering, deterministic
  local IDs, expiry, and continuity reset behavior.
- Physical validation run `31078197317`, job `92540794256`, passed the complete
  Unity/Android suite on an LG-H872 running Android 8.0.0/API 26/arm64-v8a.
- RMA-111 artifact `8958559677` reports bundled face/person inference, one face
  and one person on both frames, stable `face-000001` and `person-000001` IDs,
  invalid-center suppression, zero VLM invocations, and no network model
  download.
- The same exact run passed RMA-090, RMA-091, RMA-092, RMA-022 lifecycle, and
  authoritative rendering. The authoritative gate verified the installed APK
  SHA-256 against the candidate before reuse, cleared application data, and
  completed with no hidden kinematic fallback.
- Detailed evidence and the rejected-candidate history are in
  `docs/validation/RMA_111_LIGHTWEIGHT_TRACKING_VALIDATION_2026-08-05.md`.

## RMA-112 — Implement bounded world model

**Status:** Complete (2026-08-06)

- [x] Store entity ID, class/description, position, estimated direction, confidence, first/last seen, provider, description age, and coverage context.
- [x] Expire stale entities deterministically.
- [x] Deduplicate VLM descriptions for a tracked entity.
- [x] Limit memory and history growth.
- [x] Expose immutable snapshots to the conversation/behavior layers.

**Completion evidence**

- `ReachyBoundedWorldModel` is a Unity-independent managed core that consumes
  RMA-110/RMA-111 transformed-frame tracking results and publishes immutable
  current/recent entity snapshots for later conversation and behavior layers.
- Entity state retains generation, tracker/provider and complete frame
  provenance, classification, confidence, first/last seen, source bounds,
  coverage context, bounded observations, and bounded semantic descriptions.
  Metric position remains explicitly unknown for two-dimensional tracking;
  direction is labeled as a normalized non-metric Reachy-eye image ray.
- Exact-boundary expiry is deterministic. Stable IDs update one entity while
  session/continuity reuse creates a new generation. Expired entities cannot be
  presented as currently visible.
- All entity, observation, description, text, and ordering-cursor growth is
  bounded. Capacity and history drops are visible; no entity or cursor is
  silently overwritten. Cursor capacity is at least entity capacity.
- Duplicate/stale ordering checks and retained-scope cursor preflight occur
  before clock, expiry, or visibility mutation. A later-timestamp stale frame is
  non-mutating, and ordering cursors protecting retained entities cannot be
  evicted to admit a new scope.
- Semantic duplicates are normalized and deduplicated, retain confirmation
  count and latest-provider provenance, and cannot cross entity generations.
  No stale observation, semantic result, or fallback provider is substituted.
- Permanent run `31085246677`, job `92563054033`, passed warnings-as-errors and
  all 18 bounded-world-model contracts on accepted implementation SHA
  `4e5d08d9dc917b5e7a22a0dada0a34ab5ed11f7f`. Artifact `8961118733` has digest
  `sha256:599b2c8918cc850a43445e719e57a43dbd004bf3dedf57d813101ec72e9134c7`.
- Hosted CI run `31085246701` passed on the same SHA. Self-hosted run
  `31085246680`, job `92563129531`, passed `129/129` EditMode and `1/1`
  PlayMode tests, ARM64/API-26 APK build and verification, physical RMA-090,
  RMA-091, RMA-092, RMA-111, RMA-022 lifecycle, authoritative rendering, every
  evidence upload, APK upload, and final status publication on an LG-H872.
- Detailed design, rejected-candidate history, exact artifact digests, and
  physical evidence are in
  `docs/validation/RMA_112_BOUNDED_WORLD_MODEL_VALIDATION_2026-08-06.md`.

## RMA-113 — Implement VLM scheduling policy

**Status:** Complete (2026-08-06)

- [x] Trigger only for user visual questions, explicit planner requests, significant scene changes, new entities, manual requests, or a configured slow interval.
- [x] Add per-provider rate and concurrency limits.
- [x] Never run VLM continuously at camera frame rate by default.
- [x] Cancel obsolete requests when the scene or question changes.
- [x] Surface cost/network disclosure for cloud requests.

**Completion evidence**

- `ReachyVlmScheduler` is a Unity-independent managed admission policy over the
  existing RMA-110 VLM provider/executor contracts. It schedules exact provider
  instances and does not invoke models, duplicate provider execution, or select
  a fallback provider.
- The trigger vocabulary is limited to user visual questions, explicit planner
  requests, significant scene changes, new entities, manual requests, and an
  optional slow interval. There is no camera-frame trigger, and the slow
  interval is disabled by default.
- Sliding-window rate limits, active-request concurrency leases, trigger-sequence
  deduplication, bounded provider-policy state, and immutable diagnostics are
  enforced independently for each provider.
- Monotonic scene and question revisions cancel obsolete work. Cancellation
  callbacks run without the scheduler or lease monitor held, while concurrent
  completion releases the scheduler monitor before waiting for cancellation
  disposal. A deterministic threaded regression rejects the prior lock
  inversion.
- Cloud providers require non-empty network disclosure plus acknowledgement.
  Providers configured as potentially billable also require non-empty cost
  disclosure plus separate acknowledgement before admission.
- Permanent run `31091982708`, job `92584899909`, passed warnings-as-errors and
  all 25 scheduling contracts on accepted implementation SHA
  `b29db6abbd41c6e1c3dee0ea5f5b2a2bbc90aa09`. Artifact `8963824901` has digest
  `sha256:4967e21791c153a5512120d2807c02181e3da20bf08e5f0ed0a7a417fb5c5ab3`.
- Hosted CI run `31091981878` passed static, managed, native/sanitizer, Android,
  and pinned Reachy-model jobs on the same SHA.
- Self-hosted run `31091982334`, job `92584898645`, passed `129/129` EditMode and
  `1/1` PlayMode tests, ARM64/API-26 APK build and verification, physical
  RMA-090, RMA-091, RMA-092, RMA-111, RMA-022 lifecycle, authoritative
  rendering, every evidence upload, APK upload, and final status publication on
  an LG-H872.
- Detailed architecture, rejected-candidate history, exact artifact digests,
  and physical evidence are in
  `docs/validation/RMA_113_VLM_SCHEDULING_POLICY_VALIDATION_2026-08-06.md`.

## RMA-114 — Implement local VLM extension point

**Status:** Complete (2026-08-06)

- [x] Define a local VLM adapter interface and model manifest fields.
- [x] Do not require a local VLM for the first release.
- [x] Add a stub/unavailable implementation that reports capability honestly.
- [x] Benchmark candidate sub-1B-class VLMs only after core physics and LLM performance is stable.

**Completion evidence**

- `ILocalVisionLanguageAdapter` is a Unity-independent extension boundary over
  the existing RMA-110 `IVisionLanguageProvider` contract. An adapter may
  create only the exact on-device provider described by a validated manifest;
  it cannot download a model, open a network fallback, or substitute another
  provider.
- Schema version 1 records bounded identity, runtime, execution limits,
  provenance/distribution, capabilities, and per-artifact path, SHA-256, and
  size fields. The C# contract and Draft 2020-12 JSON Schema reject missing,
  duplicate, malformed, oversized, or network-dependent definitions.
- Local artifact roots fail closed. Only absolute local filesystem paths,
  hostless and credential-free file URIs, and authority-bearing Android
  content URIs are accepted. Relative paths, UNC/network shares, remote file
  URIs, credentials, ports, and network schemes are rejected before adapter
  creation.
- `UnavailableLocalVisionLanguageAdapter` advertises no operational capability
  and returns typed `Unavailable`, `Cancelled`, or disposed failures. It does
  not claim success, read image data, invoke a runtime, download a model, or
  fall back to a cloud provider.
- `LocalVlmReleasePolicy` keeps a local VLM optional for the first release,
  disables automatic model download and provider fallback, and explicitly
  defers sub-1B-class candidate benchmarking until physics and LLM performance
  are stable. No model payload or runtime was added in RMA-114.
- Permanent run `31100461712`, job `92612496688`, passed the warnings-as-errors
  managed-core build, all 45 local-VLM contracts, schema parsing, evidence
  generation, and status publication on implementation SHA
  `1a1488229526cb5abfa03e321bf05bfb0d798ed9`.
- RMA-114 artifact `8967238772` has digest
  `sha256:788c17c20cce1f55f1ca05bccc86651620b4ebcb4efdead4cb059b94ddcb60bf`.
- Hosted CI run `31100461578` passed static, managed, native/sanitizer, Android,
  and pinned Reachy-model jobs on the same SHA.
- Self-hosted run `31100461740`, job `92612565252`, passed `129/129` EditMode
  and `1/1` PlayMode tests, ARM64/API-26 APK build and verification, physical
  RMA-090, RMA-091, RMA-092, RMA-111, RMA-022 lifecycle, authoritative
  rendering, every evidence upload, APK upload, and final status publication
  on an LG-H872.
- Detailed architecture, rejected-candidate history, exact source and artifact
  digests, and physical evidence are in
  `docs/validation/RMA_114_LOCAL_VLM_EXTENSION_VALIDATION_2026-08-06.md`.

## RMA-115 — Implement OpenAI and compatible VLM adapters

**Status:** Complete (2026-08-06)

- [x] Reuse the selected Responses- or Chat-style provider transport.
- [x] Encode only transformed valid image content.
- [x] Define image resizing and quality policy.
- [x] Include prompt context stating coverage limitations where relevant.
- [x] Validate structured results and preserve provider error detail without secrets.

**Acceptance criteria**

- [x] Basic face tracking works without a VLM.
- [x] VLM requests are selective and cancellable.
- [x] Stale entities are not presented to the LLM as currently visible.

**Completion evidence**

- Responses-style and Chat Completions-style providers share one explicit transport boundary. Endpoint style, model ID, output limit, image policy, and provider identity are configuration values; mismatches fail construction rather than trying another protocol.
- Only observation-eligible transformed Reachy-eye frames with validity masks reach the encoder. Encoded results must prove identity, mask application before resize, valid-only content, exact policy application, bounded dimensions and bytes, and no upscaling before one transport call is permitted.
- The default policy is aspect-preserving 1024x1024 maximum, 4 MiB maximum, JPEG quality 85, automatic detail, black replacement for invalid pixels, and no upscaling. The owned encoded payload is copied and zeroed on disposal.
- Coverage context states normal or degraded coverage and the valid-pixel fraction, prohibits inference outside valid regions, and explicitly excludes world-model history and stale entities from current visual evidence.
- Structured outcomes retain safe category, code, HTTP status, provider request ID, and bounded detail. Credential-, data-URL-, payload-, and opaque-token-like detail is redacted; uncaught exceptions expose only their type.
- Automatic retry, provider fallback, response storage, and streaming are disabled. Concurrency overflow and cancellation are typed and visible; no request is silently queued or rerouted.
- RMA-111 face/person tracking remains independent and bundled on device. RMA-113 remains the selective admission policy; these adapters contain no frame-rate loop, timer, or automatic invocation path.
- The permanent 60-case suite and exact-SHA gate are documented in `docs/architecture/OPENAI_COMPATIBLE_VLM_ADAPTERS.md` and `docs/validation/RMA_115_OPENAI_COMPATIBLE_VLM_ADAPTERS_VALIDATION_2026-08-06.md`.

---

# Phase 13 — Android ASR and TTS defaults

## RMA-120 — Define independent speech provider contracts

- [ ] Define `IAsrProvider` and `ITtsProvider` separately.
- [ ] Include availability, locality/network requirement, language/voice capability, start/cancel/dispose, and structured errors.
- [ ] Prevent provider implementation from initiating an unauthorized fallback.

Suggested contract shape:

```csharp
public interface IAsrProvider : IAsyncDisposable
{
    ProviderDescriptor Descriptor { get; }
    ValueTask<ProviderAvailability> CheckAvailabilityAsync(
        AsrOptions options,
        CancellationToken cancellationToken);
    IAsyncEnumerable<AsrEvent> RecognizeAsync(
        AsrRequest request,
        CancellationToken cancellationToken);
}

public interface ITtsProvider : IAsyncDisposable
{
    ProviderDescriptor Descriptor { get; }
    ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
        CancellationToken cancellationToken);
    IAsyncEnumerable<TtsEvent> SpeakAsync(
        TtsRequest request,
        CancellationToken cancellationToken);
}
```

## RMA-121 — Implement Android on-device ASR

- [ ] Discover whether explicit on-device recognition is available.
- [ ] Create the on-device recognizer only after microphone permission.
- [ ] Support configured language and recognition support checks where available.
- [ ] Handle partial/final results, no-match, busy, timeout, cancellation, service death, and language-model absence.
- [ ] Marshal callbacks safely to application state.
- [ ] Destroy the recognizer on teardown.

**Prohibited**

- [ ] Do not use `EXTRA_PREFER_OFFLINE` as proof that processing is local.
- [ ] Do not fall back to system/cloud recognition silently.

## RMA-122 — Implement Android system ASR as explicit option

- [ ] Clearly label it as device-provider controlled and potentially network-backed.
- [ ] Keep it distinct from the on-device provider in settings and diagnostics.
- [ ] Apply the same lifecycle/error tests.

## RMA-123 — Implement Android offline TTS

- [ ] Initialize TextToSpeech asynchronously.
- [ ] Enumerate voices and filter for voices not requiring a network connection.
- [ ] Select a voice by locale and user preference.
- [ ] Handle missing voice data and installation guidance.
- [ ] Report start, done, stop, and error events.
- [ ] Release the engine on teardown.

## RMA-124 — Implement Android system/network TTS as explicit option

- [ ] List network-required status per voice.
- [ ] Require explicit selection for network-required voices.
- [ ] Do not auto-select one when offline TTS is unavailable.

## RMA-125 — Add microphone/audio focus state machine

- [ ] Request and release audio focus.
- [ ] Coordinate listening and speaking to avoid self-transcription.
- [ ] Handle phone call, alarm, Bluetooth route, headphone changes, and other focus loss where exposed.
- [ ] Keep the single-microphone limitation explicit.

**Acceptance criteria — offline speech portion**

- [ ] On a device with installed services, the user can converse using Android on-device ASR and offline TTS with networking disabled.
- [ ] Missing services produce visible setup guidance.
- [ ] No audio is sent to a cloud provider unless explicitly selected.

---

# Phase 14 — Local LLM runtime and model management

## RMA-130 — Build llama.cpp Android native plug-in

**Status:** Complete (2026-08-08)

- [x] Pin llama.cpp source revision.
- [x] Cross-compile for Android ARM64 with documented CPU features.
- [x] Expose a narrow, versioned C ABI for load, tokenize/chat-template application, generate/stream, cancel, unload, and metrics.
- [x] Ensure model inference cannot block the simulation thread.
- [x] Add memory-allocation and cancellation stress tests.

**Completion evidence — RMA-130**

- Historical ABI-1 implementation remains accepted at source SHA `11233d2967d9864f35f1684da13018110196f682`; dedicated run `31203427475`, job `92948655063`, and artifact `9003903370` passed.
- RMA-133 later extended the same pinned-runtime boundary to ABI 2 for explicit GBNF constrained generation. That extension is additive evidence and does not rewrite the accepted ABI-1 record in `docs/validation/RMA_130_LLAMA_CPP_ANDROID_RUNTIME_VALIDATION_2026-08-07.md`.

## RMA-131 — Define local model manifest

**Status:** Complete (2026-08-07)

- [x] Add fields for model ID, source, revision, license, file size, SHA-256, GGUF metadata, context limit, chat template, stop tokens, memory estimate, recommended threads, and device compatibility.
- [x] Mark experimental models clearly.
- [x] Keep model IDs/configuration out of hard-coded UI logic.

**Completion evidence**

- Schema version 1 and the immutable managed mirror define exact model/provenance/license identity, one verified GGUF artifact, normalized GGUF/tokenizer metadata, context/chat/stop metadata, context-and-batch-qualified peak RAM, recommended threads, and explicit Android/runtime compatibility.
- Experimental state is mandatory and internally consistent. At RMA-131 acceptance, the only committed example was a synthetic experimental fixture on the reserved `.invalid` domain; RMA-131 itself selected, downloaded, recommended, and bundled no real model. RMA-133 has since added a separately benchmark-selected real manifest without rewriting the historical RMA-131 acceptance record.
- `LocalModelManifestCatalog` uses unique data-driven IDs and ordinal exact lookup only. Missing IDs return no model or throw; no default, fuzzy, prefix, provider, or cloud fallback exists, and permanent tests reject candidate IDs hard-coded into settings/UI logic.
- Artifact paths are bounded package-relative lowercase `.gguf` paths. Absolute paths, traversal, drive prefixes, backslashes, invalid sizes/hashes, incompatible runtime/device metadata, and understated RAM fail visibly rather than receiving defaults.
- Dedicated run `31208746428`, job `92966163017`, passed the warnings-as-errors managed build, managed contract suite, JSON mutation suite, zero-network fixture validation, schema parsing, exact-SHA evidence generation, and artifact upload on accepted implementation SHA `94145dda69f6ee3f886a78be9728ea6ddc355bb8`.
- Artifact `9005805295` has digest `sha256:aefdd64431c6d3b9c730b5b08fa902cb2a5edeb7b67b9bfeb0a25caae726100f`. Hosted CI run `31208746388` passed static, managed, native/sanitizer, Android, and pinned Reachy-model jobs on the same SHA.
- Detailed design and accepted evidence are in `docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md` and `docs/validation/RMA_131_LOCAL_MODEL_MANIFEST_VALIDATION_2026-08-07.md`.

## RMA-132 — Implement safe model download/import

**Status:** Complete (2026-08-07)

- [x] Check free storage before download/import.
- [x] Download to a temporary path.
- [x] Support safe resume or clean restart.
- [x] Verify SHA-256 before atomic installation.
- [x] Recover from app termination and partial files.
- [x] Allow deletion and orphan cleanup.
- [x] Do not load arbitrary paths without validation.

**Completion evidence**

- `LocalModelPackageManager` owns a marker-bound managed store and stages all acquisition under it. Imports accept a caller-opened `Stream`, downloads use one explicit HTTPS source, and only revalidated manifest-derived installed files can produce a `LocalModelApprovedArtifact`.
- Free-space preflight requires the remaining artifact bytes plus a configured reserve. Download partials are resumable only when their sidecar still binds the exact source URI fingerprint, manifest size, and SHA-256; range mismatches fail visibly and unsupported resume performs a clean restart.
- Exact byte count, no extra byte, and SHA-256 verification are required before same-store atomic publication. Corrupt installed files never receive an approved path; verified replacements quarantine them before publication and final files are rehashed.
- Recovery removes abandoned non-resumable imports and malformed download state while retaining only valid manifest-bound resumable downloads. Manifest-derived deletion and marker-confined orphan cleanup remove loose/unknown staging, quarantine, and installed orphan entries without following reparse-point directories.
- The Ralph loop fixed strict async-stream/static analyzer findings and test-harness analyzer findings without suppression, corrected an over-broad static path assertion, and hardened a real staging-orphan cleanup edge before acceptance. No fallback or integrity relaxation was introduced.
- Dedicated run `31212296409`, job `92977704407`, passed the warnings-as-errors core build, all 15 managed package behaviors, static contracts, Python compilation, exact-SHA evidence generation, and artifact upload on accepted implementation SHA `d50e44d83b14e1e1420dc347164671db6593d73c`.
- Artifact `9007154955` has digest `sha256:3babe8eea5088de9e6b4f45da8115f562f03b051c233cb31ecedd3310f36f7c3`. Hosted CI run `31212296177` passed static, managed, native/sanitizer, Android, and pinned Reachy-model jobs on the same SHA.
- At RMA-132 acceptance, no real model had been downloaded, selected, recommended, or bundled; RMA-133 still owned benchmark-backed model selection and RMA-134 owned inference. RMA-133 has since selected Qwen3-0.6B through independent physical-device evidence; RMA-132 package integrity semantics remain unchanged. Detailed design and historical evidence are in `docs/architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md` and `docs/validation/RMA_132_LOCAL_MODEL_PACKAGE_VALIDATION_2026-08-07.md`.

## RMA-133 — Benchmark and select initial local model

**Status:** Complete (2026-08-08)

- [x] Evaluate Qwen3-0.6B-class and alternatives under the selected license constraints; after documented sub-1B rejection evidence, permit an up-to-2B-class candidate without weakening quality or safety gates.
- [x] Measure load time, peak memory, prompt processing, token rate, thermal behavior, and response quality.
- [x] Test high-level behavior JSON reliability.
- [x] Select a default/recommended model through documented evidence.
- [x] Do not describe a candidate as final before benchmarking.

**Completion evidence — RMA-133**

- V6 permanent physical run `31257650251`, job `93103766921`, on LG-H872 selected `qwen3-0.6b-q4-k-m` under unchanged 12/12, schema 1.0, semantic >=85, decode >=1 token/s, RSS <=1.5 GB, battery <45 C, and rise <=10 C gates.
- Selected metrics: semantic 85.4167, schema 1.0, decode 2.3465 token/s, peak RSS 740,380,672 bytes, battery peak 37.1 C, rise 5.9 C.
- The malformed-grammar control failed closed with status 16 and zero text events; no repair or unconstrained fallback exists.
- Real manifest: `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`, requiring `reachy_llama` ABI 2 and exact artifact SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`.
- Full selection evidence: `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md`.
- Controlled reproducibility run `31270194090` / physical job `93134741783` on source SHA `efe2a31a3b4df17281096a81f8d7509e2cc8de3b` reproduced Qwen3 at 12/12, schema 1.0, semantic 85.4167, 2.3677 token/s, 740,376,576-byte peak RSS, 37.1 C peak battery temperature, and +5.9 C rise after a 31.2-31.3 C stable cool-start window. Artifact `9025603640` has digest `sha256:a53d54ec69d5d851241bef5d6d57073e965d2463cf167f11841d96137c32ab42`.
- The warm closure rerun remains explicit failure evidence: its unchanged Qwen3 behavior result fell to 0.5718 token/s from a 34.9 C start and reached 43.0 C. RMA-135 owns production thermal/resource governance; RMA-133 does not lower its throughput gate or hide that result.
- Full reproducibility evidence: `docs/validation/RMA_133_V6_REPRODUCIBILITY_VALIDATION_2026-08-08.md`.
- Final closure source/docs/manifest SHA `1141b3e72f4e621164eecceb241c5f2013b706f4` passed permanent CI run `31271532993` across native/sanitizer, managed, Android, static/actionlint/Ruff/ShellCheck, and pinned Reachy-model jobs.
- RMA-133 physical-trigger hardening SHA `4565a2fb5508cc59dda4501c8daaa77e82c5e9a3` passed permanent CI run `31271760818` and hosted RMA-133 run `31271760823`; its scope gate correctly skipped a redundant physical benchmark because no frozen physical-benchmark input changed.
- RMA-133 is closed with Qwen3-0.6B Q4_K_M selected. The warm throttled run remains explicit evidence for RMA-135; no RMA-133 threshold, model, scorer, or fallback policy was weakened to obtain closure.

## RMA-134 — Implement local LLM provider

**Status:** Complete (checkbox audit 2026-08-17; underlying work predates this date)

- [x] Stream tokens/events on a worker thread.
- [x] Support cancellation and conversation reset.
- [x] Enforce context and output limits.
- [x] Validate generated behavior intent.
- [x] Fall back only to an explicit local “unavailable” state, not a hidden cloud request.

**Completion evidence**

- `ReachyLocalLlmProvider.GenerateAsync` dispatches generation via `Task.Run`
  (`Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalLlmProvider.Generation.cs:78-86`)
  and streams fragments through `ILocalLlmStreamSink.OnEventAsync`
  (`ReachyLocalLlmProvider.Streaming.cs:91-151`); proven non-blocking by
  `managed/ReachyMini.LocalLlm.Tests/Program.GenerationSuccessTests.cs`.
- Cancellation/reset rotates a conversation epoch and cancels the active
  generation (`ReachyLocalLlmProvider.Generation.cs:90-102`,
  `ReachyLocalLlmProvider.Streaming.cs:223-253`); tested by
  `managed/ReachyMini.LocalLlm.Tests/Program.ContextAndConcurrencyTests.cs`.
- Context/output limits enforced against `profile.ContextTokens`
  (`ReachyLocalLlmProvider.Generation.cs:239-248`) and a per-fragment UTF-8
  byte budget (`ReachyLocalLlmProvider.Streaming.cs:120-125`); tested by
  `Program.ContextAndConcurrencyTests.cs` and `Program.IntentAndOutputLimitTests.cs`.
- Grammar-constrained generation plus a post-hoc parse gate
  (`ReachyLocalLlmProvider.Generation.cs:254-262`,
  `ReachyLocalLlmProvider.Streaming.cs:277-307`) rejects malformed intents
  rather than repairing them; tested by
  `Program.IntentAndOutputLimitTests.cs:TestInvalidIntentIsNotRepairedAsync`.
- `GenerateAsync` returns an explicit `LocalLlmGenerationStatus.Unavailable`
  when not ready and never invokes another provider; no cloud reference
  exists anywhere under `Assets/.../LocalModels/*.cs`.
- All wired into permanent CI gate
  `.github/workflows/rma134-local-llm-provider.yml`.

## RMA-135 — Implement resource and thermal governor

**Status:** Governance mechanism complete (checkbox audit 2026-08-17); physical
acceptance criteria remain genuinely open per the thermal finding below, not stale.

- [x] Read Android thermal/memory signals where available.
- [x] Define device profiles.
- [x] Reduce LLM threads/context/batch or suspend inference before compromising physics.
- [x] Report throttling to diagnostics and UI.
- [x] Test OOM and cancellation cleanup.

**Completion evidence**

- `ReachyAndroidLocalLlmResourceSignalSource.Capture` reads
  `PowerManager.getCurrentThermalStatus` and `ActivityManager.MemoryInfo`
  (`Assets/ReachyMini/Runtime/Application/ReachyAndroidLocalLlmResourceSignalSource.cs:42-150`,
  `#if UNITY_ANDROID && !UNITY_EDITOR`, verified by a source-content contract
  test since it cannot run in managed CI:
  `scripts/tests/test_rma135_resource_governor.py`).
- `LocalLlmDeviceProfile.Select` defines Conservative/Balanced/Performance
  tiers by RAM/core count
  (`Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmResourceGovernor.cs:143-205`),
  exercised by `managed/ReachyMini.ResourceGovernor.Tests/Program.cs` and
  `managed/ReachyMini.ResourceGovernor.Integration.Tests/Program.cs`.
- `LocalLlmResourceGovernor.Evaluate`/`BuildEffectiveProfile` shrinks
  context/batch/threads under pressure and forces `Suspended`
  unconditionally on a physics deadline miss
  (`LocalLlmResourceGovernor.cs:254-397`, fed by
  `Assets/ReachyMini/Runtime/Core/Application/ReachyLocalLlmPhysicsBudgetTracker.cs:28-58`).
- `LocalLlmGovernorDiagnosticsSnapshot.Create` and
  `ReachyProviderGovernorMainScreenProjection.Create` map governor decisions
  to diagnostics/HUD state
  (`Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmGovernorDiagnostics.cs:69-121`,
  `Assets/ReachyMini/Runtime/Core/Application/ReachyProviderGovernorDiagnostics.cs:51-115`).
- OOM handled at both the provider (fault/cleanup/reload path,
  `ReachyLocalLlmProvider.Streaming.cs:309-333`) and governor (latch requiring
  3 consecutive nominal observations to clear, `LocalLlmResourceGovernor.cs:234,341-346`)
  layers; tested by `managed/ReachyMini.LocalLlm.Tests/Program.OutOfMemoryTests.cs`
  and `managed/ReachyMini.ResourceGovernor.Integration.Tests/Program.cs`.
- All wired into permanent CI gate
  `.github/workflows/rma135-resource-thermal-governor.yml`.

**Acceptance criteria — local LLM portion**

- [ ] The selected local model loads and produces a validated intent offline on a representative phone.
- [ ] Physics timing remains within the defined budget during generation.
- [x] Model failure is recoverable without restarting the app -- `LocalLlmProvider.ReloadAsync`
      performs in-process unload/reload recovery without an app restart
      (`ReachyLocalLlmProvider.Core.cs:131-277`), proven by
      `Program.OutOfMemoryTests.cs:TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync`
      (fault -> explicit reload -> successful second generation) and
      `Program.ReloadAndDisposeTests.cs`.

**Open finding (2026-08-17)** — physical acceptance on the SM-A546E (mid class) is
currently blocked by a genuine device thermal characteristic, not a governor bug: the
combined MuJoCo physics + local-LLM workload alone drives the SoC into light thermal
throttling within roughly 15 seconds, even from a measured-cool start, so post-recovery
generation retries keep hitting real, sustained deadline misses. See
`docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md` for the full evidence.
The governor-cadence bug that was masking this (hysteresis recovery starved of samples
between retries) was found and fixed the same day; the retry mechanism now converges
reliably and the remaining failure is thermal, not code.

---

# Phase 15 — OpenAI and OpenAI-compatible cloud providers

## RMA-140 — Implement secure provider configuration

- [ ] Define provider profiles containing base URL, endpoint style, model IDs, headers, timeout, streaming, TLS mode, and secret reference.
- [ ] Store secrets using Android Keystore-backed storage.
- [ ] Redact secrets from logs and exports.
- [ ] Reject cleartext HTTP by default.
- [ ] Add an explicit local-development override with a persistent warning.

## RMA-141 — Implement shared HTTP/streaming transport

- [ ] Add cancellation, connection/read timeout, bounded response size, retry classification, and backoff.
- [ ] Do not retry non-idempotent requests blindly.
- [ ] Parse streaming responses incrementally.
- [ ] Categorize authentication, permission, quota/rate limit, timeout, TLS, malformed response, and server errors.
- [ ] Include request IDs in diagnostics where provided.

## RMA-142 — Implement OpenAI LLM adapter

- [ ] Implement Responses API requests with configurable model ID.
- [ ] Support text and transformed-image input where enabled.
- [ ] Support structured high-level behavior output.
- [ ] Keep model IDs configurable.
- [ ] Add mock-server contract tests.

## RMA-143 — Implement OpenAI-compatible text adapters

- [ ] Implement Responses-style compatibility adapter.
- [ ] Implement Chat Completions-style adapter.
- [ ] Allow custom headers and base URL.
- [ ] Expose capability mismatches clearly.
- [ ] Do not assume all “OpenAI-compatible” servers support images, tools, JSON schema, or streaming.

## RMA-144 — Implement OpenAI ASR and compatible ASR

- [ ] Implement `/v1/audio/transcriptions` multipart requests.
- [ ] Buffer only the intended utterance.
- [ ] Apply format, size, duration, cancellation, and timeout limits.
- [ ] Delete temporary audio promptly.
- [ ] Implement configurable compatible endpoint/model.

## RMA-145 — Implement OpenAI TTS and compatible TTS

- [ ] Implement `/v1/audio/speech` with configurable model, voice, format, and supported instructions.
- [ ] Stream or buffer audio without blocking the Unity thread.
- [ ] Validate content type and size.
- [ ] Implement cancellation and cleanup.
- [ ] Implement configurable compatible endpoint/model.

## RMA-146 — Add explicit no-fallback policy engine

- [ ] Represent authorized fallback policies as named user settings.
- [ ] Default all cross-provider fallback to disabled.
- [ ] Require confirmation before a privacy boundary changes.
- [ ] Record provider switch reason in diagnostics.

Example policy model:

```csharp
public sealed record FallbackPolicy(
    bool AllowLocalQualityReduction,
    bool AllowSameProviderRetry,
    bool AllowNetworkProviderSwitch,
    IReadOnlySet<string> AuthorizedTargetProviderIds);
```

**Acceptance criteria — provider gate**

- [ ] ASR, TTS, LLM, and VLM providers can be selected independently.
- [ ] BYOK secrets are absent from logs and exported settings.
- [ ] Mock failures never activate an unauthorized provider.

---

# Phase 16 — Conversation orchestrator and deterministic robot behavior

## RMA-150 — Implement conversation state machine

- [ ] Implement Idle, Listening, Transcribing, Thinking, PreparingSpeech, Speaking, Interrupted, Unavailable, and Error states.
- [ ] Define every transition and cancellation path.
- [ ] Reject stale asynchronous completions using turn/session IDs.
- [ ] Prevent simultaneous conflicting ASR/TTS sessions.
- [ ] Add interruption/barge-in policy.

## RMA-151 — Define structured behavior intent schema

- [ ] Add a versioned JSON schema.
- [ ] Include optional speech, gaze target, expression, gesture, urgency, and timing constraints.
- [ ] Reject unknown unsafe actions.
- [ ] Bound string lengths, numeric ranges, and collection sizes.
- [ ] Add repair/retry policy that does not fabricate successful execution.

Example:

```json
{
  "schema_version": 1,
  "speech": "That looks interesting.",
  "gaze_target": {
    "kind": "tracked_entity",
    "entity_id": "entity-12"
  },
  "expression": "curious",
  "gesture": "small_head_tilt",
  "urgency": "normal"
}
```

## RMA-152 — Implement deterministic behavior planner

- [ ] Resolve gaze target against current world-model snapshot.
- [ ] Reject expired or low-confidence targets.
- [ ] Convert expressions and gestures into parameterized trajectories.
- [ ] Enforce workspace, joint, velocity, acceleration, load, collision, and image-coverage constraints.
- [ ] Coordinate body yaw, Stewart mechanism, and antennas.
- [ ] Route all motion through normal controller/servo/MuJoCo paths.
- [ ] Support cancellation and safe rest.

## RMA-153 — Implement baseline behavior library

- [ ] neutral idle micro-motion;
- [ ] listening posture;
- [ ] speaking motion;
- [ ] acknowledgment/nod;
- [ ] curiosity/head tilt;
- [ ] surprise/recoil;
- [ ] gaze acquisition and visual centering;
- [ ] gaze loss/search within valid coverage;
- [ ] unavailable/error expression;
- [ ] sleep/rest and wake.

- [ ] Give every behavior deterministic parameters, limits, and tests.
- [ ] Ensure “expressive” motion cannot bypass mechanical constraints.

## RMA-154 — Implement visual-servo gaze loop

- [ ] Use transformed-image tracking coordinates.
- [ ] Command a bounded head/body adjustment.
- [ ] Wait for actual MuJoCo motion to change the next transformed frame.
- [ ] Re-evaluate tracking error.
- [ ] Stop on tolerance, target loss, invalid coverage, timeout, load limit, or cancellation.
- [ ] Do not use requested head target as proof that gaze moved.

**Acceptance criteria — behavior gate**

- [ ] A face at an image edge causes actual simulated motion and recenters through feedback.
- [ ] Invalid LLM output cannot command raw joints or torque.
- [ ] Replaying the same intent and observation stream produces repeatable trajectories.

---

# Phase 17 — Persistence, privacy, and secure data handling

## RMA-160 — Implement versioned settings storage

**Status:** Complete (2026-08-13)

- [x] Store non-secret provider settings, selected voices/languages, camera calibration, fidelity mode, model manifests, and device profile.
- [x] Add migrations with tests.
- [x] Detect corruption and offer explicit recovery/export instead of silent reset.
- [x] Keep secret values separate.

## RMA-161 — Implement credential lifecycle

- [ ] Create/update/read/delete credentials through Android Keystore-backed storage.
- [ ] Test device lock changes and key invalidation paths.
- [ ] Remove credentials when provider is deleted or app data is cleared.
- [ ] Ensure screenshots/logs never display full secrets.

## RMA-162 — Implement private-media retention policy

- [ ] Default to no retention of raw camera frames, microphone audio, or cloud request media.
- [ ] Keep temporary files in protected app storage and delete promptly.
- [ ] Make conversation-history persistence opt-in and bounded.
- [ ] Add explicit recording/export UI before any future media retention feature.

## RMA-163 — Harden imported files and URLs

**Status:** Complete (2026-08-14)

- [x] Bound model/calibration/manifest file sizes.
- [x] Validate schema and numeric ranges.
- [x] Prevent path traversal and arbitrary file overwrite.
- [x] Validate custom endpoint URL scheme/host.
- [x] Reject untrusted cleartext endpoints by default.

**Acceptance criteria**

- [x] Security tests confirm secrets and private media are absent from diagnostics bundles.
- [x] Corrupt settings and files fail visibly without undefined state.

**Completion evidence**

- Imported JSON and ownership-marker reads use strict UTF-8 with per-document byte ceilings,
  and serialized persistence output is bounded before replacement.
- Calibration/profile collection sizes, calibration text, image dimensions, and crop
  arithmetic are bounded before use.
- Existing local-model path containment remains fail-closed while model/VLM download
  sources and redirects use a centralized public-HTTPS host policy.
- Provider cleartext mode remains limited to explicit trusted local-development hosts;
  credentials/private media remain denied from diagnostic bundles by default.
- Local validation is recorded in
  `docs/validation/RMA_163_IMPORTED_CONTENT_SECURITY_LOCAL_VALIDATION_2026-08-14.md`.

---

# Phase 18 — Diagnostics, observability, and no-silent-failure enforcement

## RMA-170 — Implement structured logging

**Status:** Complete (2026-08-14)

- [x] Add component, severity, event ID, monotonic timestamp, session/turn ID, and error category.
- [x] Redact secrets and private content by default.
- [x] Rate-limit repeated errors without suppressing first occurrence or final counts.
- [x] Do not log raw audio/image payloads.

**Completion evidence**

- The core diagnostics boundary provides stable event descriptors, typed severity/error
  categories, monotonic timing, bounded session/turn correlation, deterministic JSON,
  typed data classification, and centralized redaction before sinks.
- Repeated-event bursts emit the first occurrence immediately and a final count summary;
  provider/status/exception/operation/code discriminators prevent unrelated failures from
  being collapsed into one burst.
- Secret/private/raw-media fields and credential-bearing headers are default-redacted, and
  the diagnostic-bundle admission manifest denies private/raw content by default.
- Application, camera, renderer, and authoritative-runtime ordinary error paths use the
  structured Unity sink without logging raw exception messages; acceptance-marker logs
  remain separate.
- Local static validation is recorded in
  `docs/validation/RMA_170_STRUCTURED_LOGGING_LOCAL_VALIDATION_2026-08-14.md`.

## RMA-171 — Build diagnostics screen

**Status:** Complete (2026-08-14)

- [x] Show physics frequency, step time, missed deadlines, lag, constraint health, and faults.
- [x] Show render FPS, memory, thermal state, and device profile.
- [x] Show camera FPS, reprojection time, valid coverage, and active camera.
- [x] Show active ASR/TTS/LLM/VLM providers and local/network status.
- [x] Show model, calibration, native ABI, MuJoCo, Reachy asset, and app versions.
- [x] Provide a clear degraded/unavailable reason.

**Completion evidence**

- The main-screen diagnostics panel consumes a typed six-section snapshot rather than one
  opaque string, and each metric is explicitly Available, Degraded, or Unavailable.
- Authoritative simulation timing, Unity rendering/memory, RMA-135 thermal/device signals,
  camera acquisition/discovery, durable provider selections, and pinned version identities
  populate the screen without taking ownership of those subsystems.
- Camera reprojection timing and valid-coverage rows remain visible but explicitly
  unavailable until the production application publishes those telemetry sources; no fake
  zero/healthy values are substituted.
- The diagnostics panel is scrollable and preserves the existing toggle workflow. Legacy
  string binding is retained only through an adapter that marks typed sections unavailable.
- Local static validation is recorded in
  `docs/validation/RMA_171_DIAGNOSTICS_SCREEN_LOCAL_VALIDATION_2026-08-14.md`.

## RMA-172 — Implement diagnostic bundle export

**Status:** Complete (2026-08-14)

- [x] Export version/configuration, redacted logs, performance summaries, and health state.
- [x] Exclude credentials, raw media, transcripts, and conversation text by default.
- [x] Include explicit user selection for any optional sensitive content.
- [x] Add a manifest describing redactions.

**Completion evidence**

- `ReachyDiagnosticRecordBuffer` retains a bounded recent window of RMA-170 records and
  reports overwritten-record counts; `ReachyRuntimeDiagnostics` writes through a composite
  Unity + retained-record sink.
- `ReachyDiagnosticBundleExporter` produces an atomic, non-overwriting ZIP containing
  `manifest.json`, `version-configuration.json`, `performance-health.json`, and
  `logs.jsonl`. Every structured log field is re-redacted during export.
- Production user selection is explicitly `RedactedOnly`. Non-redacted selections for
  private text, raw media, or credentials fail closed; no transcript, conversation, raw
  media, credential, or raw settings source is wired into the exporter.
- The manifest records redaction/exclusion policy, user selection, retained/dropped log
  counts, entry byte counts, SHA-256 digests, and the `redacted_text` classification.
- The diagnostics panel exposes `EXPORT REDACTED BUNDLE`; the application writes only to
  its controlled `Application.persistentDataPath/diagnostics` directory and reports
  failures through structured diagnostics without raw exception messages.
- Design and local evidence are recorded in
  `docs/RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md` and
  `docs/validation/RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_LOCAL_VALIDATION_2026-08-14.md`.

## RMA-173 — Add silent-failure regression tests

Create tests proving that:

- [ ] on-device ASR failure does not activate cloud ASR;
- [ ] offline TTS failure does not activate a network voice;
- [ ] local LLM failure does not activate OpenAI;
- [ ] invalid camera pixels do not reuse stale content;
- [ ] solver failure does not switch to Unity animation;
- [ ] calibration mismatch does not load placeholders as calibrated;
- [ ] dropped command/state buffers produce visible errors;
- [ ] malformed provider response does not produce a fake successful intent.

---

# Phase 19 — Performance, thermal, and Android lifecycle hardening

## RMA-180 — Build performance harness

**Status:** Complete (2026-08-15)

- [x] Measure native physics step timing separately from Unity rendering.
- [x] Measure camera acquisition, warp, tracking, local LLM, audio, and network workloads.
- [x] Record median, p95, p99, and maximum timing.
- [x] Record memory, battery discharge, and thermal state over long runs.
- [x] Test at 30 and 60 FPS where supported.

**Completion evidence**

- `ReachyPerformanceTelemetry` provides one active bounded session with real production hooks for native physics, Unity frame cadence, camera acquisition/warp, lightweight tracking, local LLM generation, audio, and shared network transport.
- Each workload reports count, median, p95, p99, and exact maximum. A 4,096-entry deterministic reservoir keeps long runs bounded and explicitly marks percentile summaries approximate after compaction.
- `ReachyPerformanceRuntimeProbe` records Unity allocated memory, Android available memory, battery level/discharge, and thermal state every 10 seconds into a bounded 2,048-entry resource ring.
- `ReachyRma180PerformanceAcceptance` and `scripts/run_rma180_performance_acceptance_android.sh` exercise explicit 30 FPS and 60 FPS profiles with a default five-minute duration per profile and support up to one hour per profile.
- Managed and static regression contracts cover percentile math, long-run bounds, resource summaries, 30/60 profile enforcement, production hook coverage, and fail-closed input handling. Design and local evidence are recorded in `docs/RMA_180_PERFORMANCE_HARNESS_SPEC_2026-08-15.md` and `docs/validation/RMA_180_PERFORMANCE_HARNESS_LOCAL_VALIDATION_2026-08-15.md`.

## RMA-181 — Implement priority-based degradation policy

**Status:** Complete (2026-08-15)

Order of preservation:

1. simulation correctness;
2. audio interaction;
3. camera and lightweight tracking;
4. UI responsiveness;
5. LLM/VLM throughput;
6. visual quality/previews.

- [x] Reduce render FPS/effects before physics.
- [x] Reduce camera analysis resolution/rate before physics.
- [x] Cancel/suspend VLM first.
- [x] Reduce LLM resource use next.
- [x] Never silently enlarge physics timestep or skip arbitrary steps while reporting calibrated dynamics.

**Completion evidence**

- `ReachyPriorityDegradationPolicy` defines the ordered `Nominal`, `RenderReduced`, `CameraReduced`, `VlmSuspended`, `LlmReduced`, and `Critical` ladder, with immediate escalation and three-observation recovery hysteresis.
- Unity presentation degradation lowers target render FPS and disables optional shadows, anti-aliasing, and soft particles before any physics behavior can change. Lightweight tracking then reduces its bounded staging dimension and analysis cadence; throttled frames return an explicit unavailable result and never reuse stale tracking content.
- `ReachyVlmScheduler` cancels active leases and rejects new requests with `ResourceSuspended` before `LocalLlmResourceGovernor` is forced to `Minimal` or `Suspended`. The existing LLM resource governor may always impose a stronger restriction.
- Every RMA-181 decision retains the exact configured physics timestep, forbids arbitrary physics-step skipping, and preserves audio interaction. No RMA-181 code changes the simulation worker or native step API.
- The runtime bridge consumes the existing RMA-135 memory, thermal, and physics-budget sources and applies one decision to explicitly composed targets; render p95 may be supplied from the RMA-180 timing path.
- Managed contract sources cover ordering, recovery, physics/audio invariants, VLM cancellation/admission, LLM policy floors, and camera/tracking resolution/rate reduction. Static contracts and design details are in `scripts/tests/test_rma181_priority_degradation.py`, `docs/RMA_181_PRIORITY_DEGRADATION_POLICY_SPEC_2026-08-15.md`, and `docs/validation/RMA_181_PRIORITY_DEGRADATION_LOCAL_VALIDATION_2026-08-15.md`.

## RMA-182 — Harden pause/resume and interruption handling

**Status:** Complete (2026-08-15)

- [x] Pause simulation deterministically.
- [x] Stop/release camera and speech resources as required by lifecycle.
- [x] Cancel or suspend network and inference jobs safely.
- [x] Resume without simulation catch-up.
- [x] Restore UI/conversation to a defined state.
- [x] Test repeated background/foreground cycles.

**Completion evidence**

- `ReachyApplicationInterruptionCoordinator` provides the single application-service pause/resume state machine, pausing dependents before dependencies and resuming dependencies before dependents with idempotent repeated callbacks and fail-closed transition faults.
- `ReachySimulationWorker` retains its existing deterministic pause boundary and resets both the fixed-step accumulator and monotonic clock baseline on pause/resume, so elapsed background wall-clock time is never replayed as simulation catch-up.
- CameraX acquisition now exposes explicit lifecycle pause/resume operations driven by `ReachyApplicationHostBehaviour`; resume revalidates camera permission before restoring the desired stream.
- Speech focus, shared HTTP transport, local LLM generation, and VLM scheduling now expose lifecycle interruption hooks. Active work is cancelled, new work is rejected while backgrounded, and resume creates fresh work generations rather than restarting cancelled operations.
- Conversation and main-screen state use lifecycle-owned interruption states. Active turns are cancelled; resume returns only lifecycle-owned state to Idle while preserving pre-existing Error/Unavailable conditions.
- Managed contracts exercise deterministic ordering, cancellation, error preservation, and five repeated background/foreground cycles. Static design coverage and local validation are recorded in `docs/RMA_182_LIFECYCLE_HARDENING_SPEC_2026-08-15.md`, `docs/validation/RMA_182_LIFECYCLE_HARDENING_LOCAL_VALIDATION_2026-08-15.md`, and `scripts/tests/test_rma182_lifecycle_hardening.py`.

## RMA-183 — Handle memory and storage pressure

**Status:** Complete (2026-08-17)

- [x] Respond to low-memory callbacks.
- [x] Release caches and optional models without corrupting active state.
- [x] Handle low storage during model download and diagnostics export.
- [x] Provide cleanup UI.

**Completion evidence**

- `ReachyApplicationHostBehaviour` subscribes `OnLowMemory` to Unity's `Application.lowMemory` for the life of the host, releasing the camera texture bridge, sweeping every `ReachyMemoryPressureRegistry` participant, and calling `Resources.UnloadUnusedAssets()`, with the outcome recorded via `ReachyDiagnosticEventIds.ApplicationLowMemoryHandled`.
- The local LLM provider registers itself as an `IReachyMemoryPressureParticipant`: idle models are unloaded and their handle cleared, while a model that is `Loading` or `Generating` reports `RetainedActiveState` and is left untouched, so a memory sweep never corrupts an in-flight interaction.
- Model package downloads recheck free storage every `StorageRecheckIntervalBytes` (4 MiB) written rather than only at the start, surface a `StoragePressureIOException` on exhaustion, and leave the manifest-bound partial file resumable instead of deleting it.
- Diagnostic bundle export preflights free space against `ReachyDiagnosticBundleExporter.MaximumBundleBytes` plus a 16 MiB safety reserve before writing, throwing `ReachyDiagnosticBundleInsufficientStorageException`; `ReachyDiagnosticBundleExportCoordinator` catches it and surfaces an actionable "Use Recoverable Storage Cleanup and retry" message instead of a partial/corrupt bundle.
- The Settings screen exposes a "Clean Up Recoverable Storage" action; `ReachyStorageCleanupCoordinator` removes only its own owned diagnostic artifacts and the shared cache (`Caching.ClearCache()`), refusing reparse points, and explicitly preserves installed models, settings, credentials, and user state.

## RMA-184 — Representative-device matrix

- [ ] Define at least low, mid, and high performance Android test classes.
- [ ] Record SoC, Android version, RAM, graphics API, camera capability, and speech-service availability.
- [ ] Establish supported/unsupported criteria.
- [ ] Publish measured default profiles.

**Acceptance criteria**

- [ ] Long-running tests do not accumulate unbounded memory or state lag.
- [ ] Thermal degradation follows the documented priority order.
- [ ] Supported devices meet the defined simulation and interaction targets.

---

# Phase 20 — End-to-end validation and release gate

## RMA-195 — Wire the application composition to real subsystems

**Open finding (2026-08-17)** — the real, shipping composition
(`ReachyMainScreenBootstrap` -> `ReachySettingsApplicationCompositionProvider`,
confirmed by tracing the Bootstrap scene's runtime install path, not guessed)
still permanently stubs every service on the path from user input to robot
behavior, even though each underlying subsystem is real and independently
tested (RMA-090/091 camera acquisition, RMA-121/134 speech and local LLM, the
deterministic behavior planner). Discovered while investigating why the
main-screen microphone button reported unavailable -- fc715e6/022535f fixed
the microphone leg specifically; the same gap exists for provider selection,
perception, behavior, and camera frame acquisition.

**Scoped (2026-08-17)** — four parallel investigations of the real code
(not guesses) sharpened this into a dependency graph and phased plan. Baseline
behavior has zero dependency on the other three and can land first; camera
acquisition and provider selection are independent of each other; perception
needs camera acquisition; the full closed loop (gaze-tracking behavior,
provider-driven intents) needs perception and provider both Ready.

- [x] **Phase A -- baseline behavior (no perception/provider needed).**
      Complete (2026-08-21). `ReachyBaselineBehaviorApplicationService`
      (`Assets/ReachyMini/Runtime/Application/ReachyBaselineBehaviorApplicationService.cs`)
      replaces the permanently-Unavailable stub in
      `ReachySettingsApplicationCompositionProvider`'s "behavior"
      registration with a real continuous
      planner -> executor -> target-sink loop against
      `ReachyProductionAuthoritativeRuntime` alone (`worldSnapshot` is
      hardcoded `null` and `workspaceClear` hardcoded `true` -- deliberately
      zero perception/provider dependency, per this item's scope; Phase C
      replaces the hardcoded `workspaceClear` once perception lands). It
      re-plans indefinitely rather than RMA-154's fixed single run, aborts
      in-flight motion via `ReachyBehaviorAuthoritativeSafety`, integrates
      app-pause/resume through `IReachyApplicationInterruptionParticipant`,
      and gives `IReachyBehaviorService` real members (`Snapshot`,
      `SnapshotChanged`, `TryTriggerGesture`) in place of the previous
      zero-member marker (`ReachyApplicationContracts.cs`).
      **Completion evidence:** commits `cd1d84b` (initial service),
      `1119227` (fixed a `ServiceId` mismatch between the "behavior"
      registration and the service's own identity, caught by CI --
      `ReachyApplicationComposition.ValidateServiceContract` rejects any
      factory whose result doesn't match its registration), `71e12d6`
      (fixed a genuine Pause-vs-loop-thread snapshot-publish race caught by
      CI, via a lock-guarded generation counter), `374f98b` and `63a4202`
      (the continuous loop and the RMA-195/RMA-154 physical acceptance
      harnesses share one production `ReachyProductionAuthoritativeRuntime`
      instance with no arbitration -- both harnesses now pause behavior via
      `IReachyApplicationInterruptionParticipant` for their pose-driving
      window; `63a4202` additionally fixed a lookup-ordering bug in the
      first attempt, since a Unity Start()-order race could look up the
      host before its own Start() had run). Self-hosted run `32528147482`
      passed Unity edit-mode tests (148/148, including six new
      `ReachyBaselineBehaviorApplicationServiceTests`) and -- with the
      physical device pinned -- RMA-090/091/092/111/154/022 acceptance and
      authoritative-rendering acceptance, all on exact commit `63a4202`.
- [x] **Phase A -- camera frame acquisition.** Complete (2026-08-17).
      `ReachyMainScreen.RequestCameraPreview()`
      (`Assets/ReachyMini/Runtime/Application/ReachyMainScreen.CameraPreview.cs`)
      now locates the already-auto-instantiated
      `ReachyAndroidCameraAcquisition`/`ReachyAndroidCameraTextureBridge` pair
      lazily via `FindAnyObjectByType` and calls `Toggle(facing)` once camera
      permission is confirmed Granted; the settings panel renders
      `CameraPreviewTexture` via `GUI.DrawTexture` while active. This
      deliberately diverges from this item's original "thread the
      acquisition/bridge instances into
      `ReachySettingsApplicationCompositionProvider`" framing: lazy discovery
      from the main-screen code was chosen instead, to avoid restructuring the
      two independent `AfterSceneLoad` bootstraps under time pressure. Both
      bootstraps still exist unchanged; only the main-screen action's target
      changed. Unavailability (no permission, pipeline not installed) reports
      through the settings store per the settings-panel-action convention,
      matching calibration/reprojection/local-model controls, not the
      HUD-level microphone/camera-selector convention.
      `ReachyProductionApplicationCompositionProvider` remains confirmed dead
      code, not removed in this pass.
      **Completion evidence:** commits `fa3319c` (initial wiring),
      `5104cee` (fixed wrong state-store target caught by CI, plus an
      unrelated pre-existing `ReachyAuthoritativeInvariantTests` bug also
      caught by the same CI run). Self-hosted run `32075804253` passed Unity
      edit-mode tests, the ARM64 API-26 IL2CPP build, and -- with the
      physical SM-A546E device pinned -- RMA-090/091/092 camera acceptance,
      RMA-111/154 tracking/visual-servo acceptance, RMA-022 lifecycle
      acceptance, and authoritative-rendering acceptance, all on exact commit
      `5104cee`. Caveat: RMA-090/091/092's acceptance harnesses exercise
      `ReachyAndroidCameraAcquisition`/`ReachyAndroidCameraTextureBridge`
      directly via a launch-intent extra, not `RequestCameraPreview()`
      itself -- the new settings-panel button is covered by Unity Editor
      unit tests (`ReachyMainScreenTests`, `ReachySettingsScreenTests`) but
      not yet by an on-device acceptance harness tapping the actual button.
- [x] **Phase B -- provider selection, local LLM subset.** Complete
      (2026-08-21), scoped narrowly: wires the one provider path that is
      actually fully built end to end today, `(Llm, OnDevice)`.
      ASR/TTS/VLM and any AndroidService/Cloud execution stay exactly as
      before ("stored but not integrated") -- out of scope, unchanged.
      Also deliberately excludes the model install/import UI
      (`ReachyMainScreen.RequestLocalModelInstall/Import/Select/Delete`
      remain stubs, a separate and materially larger piece); on a fresh
      device this honestly reports "no compatible local model is
      installed" via the real `LocalModelPackageManager`-managed store,
      not a shortcut.
      `ReachyLocalLlmProviderApplicationService`
      (`Assets/ReachyMini/Runtime/Application/ReachyLocalLlmProviderApplicationService.cs`)
      resolves the four design questions this item originally raised: (1)
      `OnInitialize()` never triggers a model load at all -- avoids the
      sync/async hazard entirely rather than racing it, since there is no
      "Loading" `ReachyServiceState` to hold at and a ~700MB load isn't
      worth risking the same tolerated race
      `ReachyAndroidSpeechCapabilityApplicationService` already has
      elsewhere in this codebase; loading is fully lazy, triggered by the
      first `GenerateAsync` call. (2) Loading is lazy-only (no eager
      preload), naturally sidestepping the physics-worker memory
      coexistence question -- nothing loads until a real generation
      request needs it. (3) `LocalLlmBehaviorContract.CreateSelectedManifest()`
      is the settings-selection -> manifest mapping (only one manifest is
      production-approved today, so this is a lookup, not a catalog;
      extracted from two byte-identical copies previously hand-duplicated
      in the RMA-134/135 acceptance harnesses). (4) `IReachyProviderService`
      gained real members: `ProviderSnapshot`/`ProviderSnapshotChanged`
      (named to avoid colliding with `IReachyBehaviorService`'s
      same-shaped `Snapshot`/`SnapshotChanged` on the generic 8-interface
      test doubles this codebase uses), plus two optional capability
      interfaces implementers can be `as`-cast to:
      `ILocalLlmProviderCapability` (the real `GenerateAsync` entry
      point) and the pre-existing `IReachyProviderGovernorDiagnosticsSource`.
      **Completion evidence:** commits `fdc63e5` (shared manifest-factory
      refactor), `0fa4850` (contract extension, verified locally via
      `dotnet run` on both managed test suites), `ef67161` (the service
      itself + wiring + tests). Self-hosted run `32532159228` (commit
      `ef67161`) passed Unity edit-mode tests 155/155 (7 new
      `ReachyLocalLlmProviderApplicationServiceTests`) and every
      physical-device acceptance stage (RMA-090/091/092/111/154/022,
      authoritative rendering), all with the phone pinned; the separate
      `RMA-135 resource and thermal governor` gate (`32532159377`) and
      the general `CI` workflow (`32532159258`) both passed too,
      confirming the shared manifest-factory refactor didn't regress
      RMA-134/135. Caveat: the real load/admission/generate path only
      runs when a compatible model is actually installed via
      `LocalModelPackageManager` -- which nothing in the live app can do
      yet (see the excluded install/import UI above) -- so it is proven
      by the RMA-134/135 physical acceptance harnesses (which push the
      artifact directly) and by 7 new EditMode tests covering every
      off-Android/no-model-installed fail-closed path, not yet by an
      end-to-end on-device run through the live composition.
- [x] **Phase C -- perception, tracking-only.** Complete (2026-08-22). The
      original framing above understated the real scope: the tracker only
      accepts fully Level-1-reprojected "Reachy-eye" frames, which meant
      camera calibration and rotation-source wiring had to land too --
      both were, like the tracker bridge itself, completely unwired in
      production before this pass.
      `ReachyAndroidPerceptionDriver`
      (`Assets/ReachyMini/Runtime/Application/ReachyAndroidPerceptionDriver.cs`)
      is the real per-frame pipeline: camera texture bridge ->
      `ReachyCameraRelativeRotationCalculator.Calculate()` (against a
      captured `ReachySimAuthoritativeStateFrame`, mirroring
      `ReachyRma154VisualServoAcceptance.Feedback.cs`'s own
      `FindCameraPose` pattern -- the closest existing real, non-fixture
      consumer of this exact chain) -> `ReachyCameraHomographyWarpPipeline.Execute()`
      -> `ReachyOnDeviceLightweightTracker` via `VisionProviderExecutor.TrackAsync`
      -> `BoundedWorldModel.ApplyTracking()`. It is a MonoBehaviour driven
      from `Update()`, not a background loop like phase A's behavior
      service, because the GPU homography warp and texture readback are
      Unity-main-thread-only. `ReachyAndroidPerceptionApplicationService`
      is the `IReachyPerceptionService` facade the composition graph
      resolves, gated behind `Application.platform == Android` before
      creating the driver (mirroring phase B's local-LLM service).
      `IReachyPerceptionService` gained real members
      (`PerceptionSnapshot`/`PerceptionSnapshotChanged`, carrying a
      `WorldModelSnapshot?` verbatim -- the exact type the planner already
      accepts, so no translation layer needed), and
      `ReachyBaselineBehaviorApplicationService` now consumes the real
      snapshot instead of phase A's hardcoded `null`.
      **Deliberately excludes:** `workspaceClear` stays hardcoded `true`
      -- it gates whether the planner allows motion at all, and nothing
      in `WorldModelSnapshot`/`WorldEntitySnapshot` defines what "an
      obstacle is present" means; inventing that mapping would be
      guessing at safety-relevant behavior with no specification to
      verify it against. Also excludes any UI for triggering camera
      calibration capture -- RMA-100's calibration workflow exists but is
      opt-in and not itself composition-wired, so this pipeline correctly
      reports `NoCalibration` and does nothing on an uncalibrated device.
      **Completion evidence:** commit `04eac13` (the service/driver/contract
      changes) plus `c3d8d35` (fixed a missing `using System;` compile
      error in the new test file, caught by CI). Self-hosted run
      `32544283870` (commit `c3d8d35`) passed Unity edit-mode tests and
      every physical-device acceptance stage (RMA-090/091/092/111/154/022,
      authoritative rendering), all with the phone pinned; the general
      `CI` workflow (`32544283882`) passed too. Caveat: no physical
      acceptance harness yet exercises the *live* pipeline end to end --
      RMA-111's harness still only covers the ML Kit backend against a
      decoded fixture, per this phase's own scoping investigation: the
      new EditMode tests (`ReachyAndroidPerceptionApplicationServiceTests.cs`)
      only cover the off-Android fail-closed path, and the live
      calibration -> rotation -> homography -> tracker -> world-model
      chain running on real camera frames is unverified by any automated
      harness. A follow-up physical acceptance harness (mirroring
      RMA-111's launch-intent-extra/result-file convention) is the
      concrete next step to close that gap.
- [ ] **Phase D -- provider selection, cloud LLM/VLM; VLM-based perception;
      full closed loop.** No cloud LLM provider class exists at all today
      (unlike ASR/TTS/VLM cloud, which do) -- this is new code, not wiring,
      following the OpenAI-compatible ASR/TTS providers as the pattern. VLM
      scene description (`ReachyVlmScheduler`,
      `ReachyOpenAiVisionLanguageProviders`) and provider-driven behavior
      intents (`ReachyBehaviorIntentContracts`) both gate on this landing
      first. First cloud-provider enablement must route through
      `ReachyProviderFallbackPolicyEngine`'s privacy-boundary confirmation,
      matching the existing local/cloud disclosure contract.
- [ ] `ReachyProductionApplicationCompositionProvider` is confirmed dead code
      (never referenced by `ReachyMainScreenBootstrap`, which always
      constructs `ReachySettingsApplicationCompositionProvider`) -- remove it,
      or document why it is intentionally kept.

Blocks RMA-190's "On-device ASR -> local LLM -> behavior -> offline TTS" and
"Front camera -> head rotation -> transformed Reachy-eye frame" scenarios, and
RMA-194's "Selected benchmark-backed local LLM works without blocking physics"
and "Behavior planner validates all AI output" release criteria: none of these
can be exercised end-to-end in the live app until this composition wiring
exists, regardless of how well each underlying subsystem tests in isolation.

## RMA-190 — Build automated end-to-end scenarios

**Blocked by RMA-195** — every scenario below needs the composition layer
actually wired to provider selection, perception, and behavior, not just the
underlying subsystems tested in isolation.


- [ ] Offline launch with no network.
- [ ] MuJoCo full model load and neutral reset.
- [ ] Deterministic gesture replay.
- [ ] Front camera -> head rotation -> transformed Reachy-eye frame.
- [ ] On-device ASR -> local LLM -> behavior -> offline TTS.
- [ ] Rear-camera visual question with optional VLM.
- [ ] Independent cloud provider combinations.
- [ ] Permission denial/revocation.
- [ ] Network loss, rate limit, malformed response, and cancellation.
- [ ] Model corruption/OOM.
- [ ] Solver fault and controlled reset.

## RMA-191 — Complete privacy and security review

- [ ] Verify no embedded keys.
- [ ] Verify Keystore usage.
- [ ] Verify network security configuration.
- [ ] Verify provider disclosure before cloud-bound audio/image/text.
- [ ] Verify logs and diagnostics redaction.
- [ ] Verify temporary media cleanup.
- [ ] Review native buffer and lifetime safety.

## RMA-192 — Complete license and attribution review

- [ ] Reconcile packaged dependencies/assets against inventory.
- [ ] Verify notices in repository and app.
- [ ] Verify modified Reachy-derived assets have required attribution/share-alike treatment.
- [ ] Verify no endorsement claim.
- [ ] Verify selected local model redistribution/download presentation.

## RMA-193 — Complete documentation

- [ ] Build instructions.
- [ ] Android device requirements.
- [ ] Model installation/import.
- [ ] Provider configuration and BYOK warning.
- [ ] Calibration workflow.
- [ ] Fidelity-level explanation.
- [ ] Camera Level 1 approximation and missing-pixel behavior.
- [ ] Troubleshooting and diagnostic bundle instructions.
- [ ] Privacy and data-flow explanation.

## RMA-194 — Release acceptance checklist

- [ ] App launches and runs offline on supported hardware.
- [ ] MuJoCo is authoritative and stable.
- [ ] Full closed-loop Reachy model is rendered from native state.
- [ ] Level 1 camera reprojection passes reference tests.
- [ ] Android on-device ASR and offline TTS work where installed.
- [ ] Selected benchmark-backed local LLM works without blocking physics.
- [ ] OpenAI and compatible providers are independently configurable.
- [ ] Behavior planner validates all AI output.
- [ ] No silent provider, privacy, calibration, or kinematic fallbacks remain.
- [ ] Diagnostics identify active fidelity, providers, and failures.
- [ ] Representative-device performance report exists.
- [ ] Licenses and attribution are complete.

**Acceptance criteria — initial release gate**

- [ ] All incomplete items are either resolved or moved to an explicitly named later milestone with rationale.
- [ ] No deferred Level 2, Level 3, AR, movable observer camera, or VLA code is half-enabled in production.
- [ ] The release is labeled accurately as geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin according to completed evidence.

---

# Future milestones — not part of this TODO

These items are intentionally deferred and must receive separate specifications/TODOs before implementation:

- Depth-assisted Level 2 reprojection using ARCore depth or a depth model.
- Persistent Level 3 scene reconstruction and virtual viewpoint rendering.
- AR placement of Reachy in a real room.
- User-controlled movable observer camera.
- Optional continuous/local VLM research.
- Multi-microphone or external audio-array support.
- Experimental Reachy-specific VLA/learned social-motion policy.
- iOS port.

---

# Completion record

When the TODO is complete, add:

- final release commit/tag;
- tested device matrix;
- MuJoCo and Reachy source revisions;
- selected local model and hash;
- fidelity/calibration level;
- known limitations;
- links to validation and performance reports;
- confirmation that every file referenced by the final handoff exists at the stated repository path.
