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

- [ ] Add versioned JSON/CBOR/Parquet-compatible schemas for command, joint, current/load, voltage, IMU, external pose, force, and temperature samples.
- [ ] Include monotonic timestamps and clock/source metadata.
- [ ] Include robot identity, firmware, register configuration, ambient conditions, and dataset hash.
- [ ] Validate untrusted imports with size and range limits.

## RMA-071 — Build calibration capture tooling

- [ ] Create tooling to capture physical Reachy telemetry.
- [ ] Support external camera/pose data import.
- [ ] Support force/torque test data import.
- [ ] Synchronize sources or estimate clock offset explicitly.
- [ ] Never treat unsynchronized data as synchronized without uncertainty metadata.

## RMA-072 — Implement experiment runner

- [ ] Implement scripted unloaded sweeps.
- [ ] Implement gravity-loaded static-pose tests.
- [ ] Implement step and frequency-response tests.
- [ ] Implement backlash direction-reversal tests.
- [ ] Implement torque-disabled/free-decay tests.
- [ ] Implement multi-actuator and warm/cold tests.
- [ ] Add safety notes for physical test execution.

## RMA-073 — Implement parameter fitting and held-out validation

- [ ] Separate training/fitting datasets from held-out validation datasets.
- [ ] Fit friction, backlash, latency, controller, voltage, compliance, and thermal parameters where data supports them.
- [ ] Report confidence or sensitivity.
- [ ] Generate a signed/hashed calibration profile manifest.
- [ ] Reject profiles incompatible with model or simulator versions.

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

- [ ] Define app-level services and dependency construction.
- [ ] Separate simulation, camera, audio, provider, perception, behavior, persistence, and UI interfaces.
- [ ] Ensure services are explicitly initialized and disposed.
- [ ] Add a top-level health/status model.

## RMA-081 — Build the main screen

- [ ] Display Reachy using the fixed front/three-quarter camera.
- [ ] Show concise state: idle, listening, transcribing, thinking, speaking, interrupted, unavailable, error.
- [ ] Show active camera and local/cloud provider indicators.
- [ ] Add microphone, camera selector, settings, and diagnostics controls.
- [ ] Do not add orbit/pan/free-camera gestures.

## RMA-082 — Build settings screens

- [ ] Providers: independent ASR/TTS/LLM/VLM selection.
- [ ] Camera: front/rear, preview, calibration, reprojection diagnostics.
- [ ] Speech: language, voice, offline/network status.
- [ ] Local model: install/import/select/delete and resource settings.
- [ ] Simulation: fidelity mode, calibration profile, reset, diagnostic controls.
- [ ] Privacy: cloud-bound data indicators, history/retention options.
- [ ] Licenses and attribution.

**Acceptance criteria**

- [ ] Every provider or capability unavailable state is visible and actionable.
- [ ] Settings do not imply offline operation when a network-backed Android service is selected.

---

# Phase 10 — Android CameraX bridge

## RMA-090 — Implement camera permissions and capability discovery

- [ ] Request camera permission only when needed.
- [ ] Enumerate front/rear camera availability and characteristics.
- [ ] Report supported analysis resolutions and orientations.
- [ ] Record camera intrinsics when available; define calibration fallback when unavailable.
- [ ] Handle permission denial, permanent denial, revocation, and camera-in-use errors.

## RMA-091 — Implement CameraX frame acquisition

- [ ] Bind preview and `ImageAnalysis` lifecycle-aware use cases.
- [ ] Use a bounded backpressure strategy that discards stale analysis frames.
- [ ] Carry timestamp, sensor orientation, lens facing, crop, pixel format, and intrinsics with each frame.
- [ ] Close every `ImageProxy` exactly once.
- [ ] Avoid copying to CPU formats unless a consumer requires it.
- [ ] Support explicit front/rear switching with orderly teardown.

**Tests**

- [ ] rapid start/stop;
- [ ] repeated front/rear switch;
- [ ] pause/resume;
- [ ] permission revoke;
- [ ] analyzer overrun;
- [ ] device rotation;
- [ ] camera unavailable.

## RMA-092 — Create GPU texture bridge

- [ ] Convert CameraX frames to a Unity-consumable GPU texture with minimal copies.
- [ ] Correct YUV conversion, color range, rotation, and front-camera mirroring.
- [ ] Maintain timestamp correspondence.
- [ ] Add a CPU reference conversion for tests only.

**Acceptance criteria**

- [ ] Preview and analysis show correct orientation and color on representative devices.
- [ ] No stale or closed camera buffer is sampled.

---

# Phase 11 — Level 1 rotation-only Reachy-eye reprojection

## RMA-100 — Define coordinate systems and calibration

- [ ] Document Android sensor, camera image, Unity, MuJoCo, and virtual Reachy camera axes.
- [ ] Define neutral relationship between phone camera forward direction and Reachy neutral camera direction.
- [ ] Define front-camera mirror handling separately from physical camera orientation.
- [ ] Define phone and virtual Reachy intrinsic matrices.
- [ ] Add camera calibration persistence and versioning.

## RMA-101 — Compute relative rotation from MuJoCo state

- [ ] Extract the actual head-camera rotation from the authoritative MuJoCo body/site transform.
- [ ] Do not substitute requested target orientation for actual simulated orientation.
- [ ] Remove the translational component for Level 1 only.
- [ ] Combine device/camera orientation and simulated head rotation consistently.
- [ ] Unit-test signs for yaw, pitch, and roll.

## RMA-102 — Implement GPU homography warp

- [ ] Compute:

```text
H = K_reachy * R_reachy_phone * inverse(K_phone)
```

- [ ] Pass the inverse mapping required by the shader to avoid holes from forward splatting.
- [ ] Sample only valid source coordinates.
- [ ] Emit transformed color and a validity mask.
- [ ] Support output resolution independent from source resolution.
- [ ] Avoid CPU readback for local trackers that can consume GPU input.

Shader pseudocode:

```text
for each output pixel p_out:
    ray_reachy = inverse(K_reachy) * homogeneous(p_out)
    ray_phone  = inverse(R_reachy_phone) * ray_reachy
    p_source   = K_phone * normalize_project(ray_phone)
    if p_source inside source image and ray_phone.z > 0:
        color = sample(source, p_source)
        valid = 1
    else:
        color = configured_invalid_visual
        valid = 0
```

## RMA-103 — Implement valid-coverage policy

- [ ] Calculate valid coverage percentage.
- [ ] Ensure invalid pixels are never filled from prior frames.
- [ ] Propagate validity metadata to tracking, VLM, world model, behavior, and diagnostics.
- [ ] Define thresholds for normal, degraded, and unusable coverage.
- [ ] Ensure the behavior planner can stop vision-driven turning before coverage becomes unusable.

## RMA-104 — Build reprojection test suite

- [ ] Identity transform golden image.
- [ ] Known yaw/pitch/roll synthetic grid images.
- [ ] Camera-intrinsic scaling tests.
- [ ] Front-camera mirroring tests.
- [ ] Portrait/landscape tests.
- [ ] GPU output versus double-precision CPU reference.
- [ ] Invalid-mask boundary and stale-pixel tests.
- [ ] Actual-versus-target head orientation test.

**Acceptance criteria — camera gate**

- [ ] Actual MuJoCo head rotation changes the transformed image correctly.
- [ ] X/Y/Z translation is intentionally ignored and labeled `rotation_only`.
- [ ] Invalid coverage is explicit and testable.
- [ ] CV/VLM receive the transformed frame, not the raw phone frame, unless a debug tool explicitly requests raw input.

---

# Phase 12 — Lightweight perception, world model, and VLM

## RMA-110 — Define vision provider contracts

- [ ] Separate frame source, lightweight tracker, and semantic VLM interfaces.
- [ ] Include cancellation, timeouts, capability metadata, and provider identity.
- [ ] Include validity mask/coverage with requests.

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

## RMA-111 — Implement on-device lightweight tracking

- [ ] Select and document ML Kit, MediaPipe, LiteRT, or another mobile-compatible approach.
- [ ] Implement face/person tracking first.
- [ ] Add basic object or motion tracking only if performance supports it.
- [ ] Convert detections to the transformed Reachy-eye coordinate system.
- [ ] Do not report detections centered in invalid pixels.
- [ ] Add stable local IDs and expiry.

## RMA-112 — Implement bounded world model

- [ ] Store entity ID, class/description, position, estimated direction, confidence, first/last seen, provider, description age, and coverage context.
- [ ] Expire stale entities deterministically.
- [ ] Deduplicate VLM descriptions for a tracked entity.
- [ ] Limit memory and history growth.
- [ ] Expose immutable snapshots to the conversation/behavior layers.

## RMA-113 — Implement VLM scheduling policy

- [ ] Trigger only for user visual questions, explicit planner requests, significant scene changes, new entities, manual requests, or a configured slow interval.
- [ ] Add per-provider rate and concurrency limits.
- [ ] Never run VLM continuously at camera frame rate by default.
- [ ] Cancel obsolete requests when the scene or question changes.
- [ ] Surface cost/network disclosure for cloud requests.

## RMA-114 — Implement local VLM extension point

- [ ] Define a local VLM adapter interface and model manifest fields.
- [ ] Do not require a local VLM for the first release.
- [ ] Add a stub/unavailable implementation that reports capability honestly.
- [ ] Benchmark candidate sub-1B-class VLMs only after core physics and LLM performance is stable.

## RMA-115 — Implement OpenAI and compatible VLM adapters

- [ ] Reuse the selected Responses- or Chat-style provider transport.
- [ ] Encode only transformed valid image content.
- [ ] Define image resizing and quality policy.
- [ ] Include prompt context stating coverage limitations where relevant.
- [ ] Validate structured results and preserve provider error detail without secrets.

**Acceptance criteria**

- [ ] Basic face tracking works without a VLM.
- [ ] VLM requests are selective and cancellable.
- [ ] Stale entities are not presented to the LLM as currently visible.

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

- [ ] Pin llama.cpp source revision.
- [ ] Cross-compile for Android ARM64 with documented CPU features.
- [ ] Expose a narrow, versioned C ABI for load, tokenize/chat-template application, generate/stream, cancel, unload, and metrics.
- [ ] Ensure model inference cannot block the simulation thread.
- [ ] Add memory-allocation and cancellation stress tests.

## RMA-131 — Define local model manifest

- [ ] Add fields for model ID, source, revision, license, file size, SHA-256, GGUF metadata, context limit, chat template, stop tokens, memory estimate, recommended threads, and device compatibility.
- [ ] Mark experimental models clearly.
- [ ] Keep model IDs/configuration out of hard-coded UI logic.

## RMA-132 — Implement safe model download/import

- [ ] Check free storage before download/import.
- [ ] Download to a temporary path.
- [ ] Support safe resume or clean restart.
- [ ] Verify SHA-256 before atomic installation.
- [ ] Recover from app termination and partial files.
- [ ] Allow deletion and orphan cleanup.
- [ ] Do not load arbitrary paths without validation.

## RMA-133 — Benchmark and select initial sub-1B model

- [ ] Evaluate Qwen3-0.6B-class and at least one alternative under the selected license constraints.
- [ ] Measure load time, peak memory, prompt processing, token rate, thermal behavior, and response quality.
- [ ] Test high-level behavior JSON reliability.
- [ ] Select a default/recommended model through documented evidence.
- [ ] Do not describe a candidate as final before benchmarking.

## RMA-134 — Implement local LLM provider

- [ ] Stream tokens/events on a worker thread.
- [ ] Support cancellation and conversation reset.
- [ ] Enforce context and output limits.
- [ ] Validate generated behavior intent.
- [ ] Fall back only to an explicit local “unavailable” state, not a hidden cloud request.

## RMA-135 — Implement resource and thermal governor

- [ ] Read Android thermal/memory signals where available.
- [ ] Define device profiles.
- [ ] Reduce LLM threads/context/batch or suspend inference before compromising physics.
- [ ] Report throttling to diagnostics and UI.
- [ ] Test OOM and cancellation cleanup.

**Acceptance criteria — local LLM portion**

- [ ] The selected local model loads and produces a validated intent offline on a representative phone.
- [ ] Physics timing remains within the defined budget during generation.
- [ ] Model failure is recoverable without restarting the app.

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

- [ ] Store non-secret provider settings, selected voices/languages, camera calibration, fidelity mode, model manifests, and device profile.
- [ ] Add migrations with tests.
- [ ] Detect corruption and offer explicit recovery/export instead of silent reset.
- [ ] Keep secret values separate.

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

- [ ] Bound model/calibration/manifest file sizes.
- [ ] Validate schema and numeric ranges.
- [ ] Prevent path traversal and arbitrary file overwrite.
- [ ] Validate custom endpoint URL scheme/host.
- [ ] Reject untrusted cleartext endpoints by default.

**Acceptance criteria**

- [ ] Security tests confirm secrets and private media are absent from diagnostics bundles.
- [ ] Corrupt settings and files fail visibly without undefined state.

---

# Phase 18 — Diagnostics, observability, and no-silent-failure enforcement

## RMA-170 — Implement structured logging

- [ ] Add component, severity, event ID, monotonic timestamp, session/turn ID, and error category.
- [ ] Redact secrets and private content by default.
- [ ] Rate-limit repeated errors without suppressing first occurrence or final counts.
- [ ] Do not log raw audio/image payloads.

## RMA-171 — Build diagnostics screen

- [ ] Show physics frequency, step time, missed deadlines, lag, constraint health, and faults.
- [ ] Show render FPS, memory, thermal state, and device profile.
- [ ] Show camera FPS, reprojection time, valid coverage, and active camera.
- [ ] Show active ASR/TTS/LLM/VLM providers and local/network status.
- [ ] Show model, calibration, native ABI, MuJoCo, Reachy asset, and app versions.
- [ ] Provide a clear degraded/unavailable reason.

## RMA-172 — Implement diagnostic bundle export

- [ ] Export version/configuration, redacted logs, performance summaries, and health state.
- [ ] Exclude credentials, raw media, transcripts, and conversation text by default.
- [ ] Include explicit user selection for any optional sensitive content.
- [ ] Add a manifest describing redactions.

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

- [ ] Measure native physics step timing separately from Unity rendering.
- [ ] Measure camera acquisition, warp, tracking, local LLM, audio, and network workloads.
- [ ] Record median, p95, p99, and maximum timing.
- [ ] Record memory, battery discharge, and thermal state over long runs.
- [ ] Test at 30 and 60 FPS where supported.

## RMA-181 — Implement priority-based degradation policy

Order of preservation:

1. simulation correctness;
2. audio interaction;
3. camera and lightweight tracking;
4. UI responsiveness;
5. LLM/VLM throughput;
6. visual quality/previews.

- [ ] Reduce render FPS/effects before physics.
- [ ] Reduce camera analysis resolution/rate before physics.
- [ ] Cancel/suspend VLM first.
- [ ] Reduce LLM resource use next.
- [ ] Never silently enlarge physics timestep or skip arbitrary steps while reporting calibrated dynamics.

## RMA-182 — Harden pause/resume and interruption handling

- [ ] Pause simulation deterministically.
- [ ] Stop/release camera and speech resources as required by lifecycle.
- [ ] Cancel or suspend network and inference jobs safely.
- [ ] Resume without simulation catch-up.
- [ ] Restore UI/conversation to a defined state.
- [ ] Test repeated background/foreground cycles.

## RMA-183 — Handle memory and storage pressure

- [ ] Respond to low-memory callbacks.
- [ ] Release caches and optional models without corrupting active state.
- [ ] Handle low storage during model download and diagnostics export.
- [ ] Provide cleanup UI.

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

## RMA-190 — Build automated end-to-end scenarios

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
- [ ] Local sub-1B-class LLM works without blocking physics.
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
