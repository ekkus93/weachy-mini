# Implementation status

**Updated:** 2026-07-30  
**Branch:** `master`  
**Current implementation series:** RMA-041 machine-readable mechanical-parameter
fidelity, joint-limit provenance, source uncertainty binding, and fail-closed
calibration labeling after RMA-040 official-model import closure

## Repository rules in force

- Work directly on `master`; do not create branches or pull requests unless the
  user explicitly changes that instruction.
- Every first-party warning or error is a defect to fix at its source.
- Do not suppress warnings, hide failures, select mocks in production, or add a
  silent kinematic/cosmetic fallback.
- Preserve pinned third-party source and provenance.

## Completed foundations

### RMA-001 through RMA-003 — repository, toolchain, and quality gates

The repository pins Unity, Android SDK/AGP/Gradle/JDK/NDK/CMake, MuJoCo, and the
Reachy source model. Hosted validation covers actionlint, Ruff, ShellCheck,
repository policy, first-party native warnings and sanitizers, managed analyzers
and lifecycle tests, Android Lint/tests, and official-model validation.

The trusted `kawa` runner builds Unity with the pinned editor, stages the exact
ARM64 production runtime, builds the API-26 IL2CPP feasibility APK, and runs
installed-device acceptance on the LG G6. A second independently provisioned
Unity/Android machine remains required for the two-machine reproducibility gate.

### RMA-010/RMA-011 — provenance

The Reachy Mini source is pinned at
`a739a6e461eb6d722901f1cfc225265ffc85c28d`. The importer verifies hashes,
rejects dirty or mismatched inputs, preserves notices, imports all referenced
assets, and generates deterministic model/render maps. RMA-012 in-app licenses
and the complete release-notice reconciliation remain open.

### RMA-020 through RMA-022 — Android native feasibility and lifecycle

MuJoCo 3.9.0 is pinned at
`237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. The project builds API-26 ARM64
`libmujoco.so`, production `libreachy_sim.so`, model compilers, and probes without
modifying upstream MuJoCo source.

The LG G6 constrained-mechanism gate completed 900,000 steps / 30 simulated
minutes with finite state, bounded equality residuals, zero MuJoCo warnings, and
a structured malformed-model failure.

RMA-022 is physically accepted. The installed IL2CPP application resolves the
native ABI/version, visibly reports a controlled malformed-MJB failure, creates,
steps, closes, and rejects reuse of a valid native handle, survives two real
HOME/resume cycles without suspended-wall-time catch-up, destroys the production
runtime, disables the renderer, and shuts down the process deterministically.

### RMA-030 — native ABI

The public C ABI remains version 2. It uses explicit-width/versioned structures,
generation-bearing opaque handles, typed status/recoverability, exact command
records, state/wrench/snapshot operations, and caller-owned error data.

RMA-030 native handle concurrency hardening is implemented:

- every handle-scoped public operation owns a nonblocking exclusive lease;
- same-handle contention returns retryable `HANDLE_BUSY` before backend access;
- invalid and stale handles retain their specific statuses;
- unrelated handles remain independent;
- destroy cannot race a live operation;
- contended calls do not replace retained diagnostics;
- state/snapshot size outputs change only on success or `BUFFER_TOO_SMALL`;
- null/nonzero, missing-size, undersized, and invalid output combinations fail
  without wrapper-side partial mutation;
- optional creation diagnostics require initialized ABI and structure size.

The native suite retains the original contract coverage and adds deterministic
blocked-operation tests plus eight-thread / 16,000-attempt contention tests with
exact success/busy accounting and sequence verification. Hosted warnings,
ASan, and UBSan gates pass the implementation.

### RMA-031 — managed P/Invoke boundary

RMA-031 is complete. `NativeReachySim` is the single P/Invoke declaration surface
in `ReachyMini.Runtime`; it mirrors the versioned C ABI with sequential layouts
and no managed callback entry point. `ReachySimSafeHandle` owns the opaque native
token deterministically, while `ReachySimSession` serializes operations and maps
native status, recoverability, and diagnostic text into typed managed results.

A fail-closed managed contract now verifies the 64-bit process, all fixed ABI and
authoritative-state structure sizes, and critical field offsets. Android IL2CPP
runs this contract before the first scene, loads `libreachy_sim`, and rejects a
native ABI mismatch before simulation startup. Hosted managed tests inject an
incompatible ABI and require a typed fatal `AbiMismatch` containing both version
numbers. The native-backed managed suite retains 1,000 create/step/dispose cycles.

The detailed contracts are in [Simulation ABI](SIMULATION_ABI.md),
[Native handle concurrency](architecture/NATIVE_HANDLE_CONCURRENCY.md), and
[Managed simulation interop](architecture/MANAGED_INTEROP.md).

### RMA-032 — authoritative simulation worker

RMA-032 is complete. A managed-owned dedicated thread schedules the 500 Hz native
simulation with a monotonic fixed-step accumulator. Commands are admitted through
a bounded preallocated queue and applied only at native step boundaries. Immutable
state and timing snapshots are published through a versioned triple buffer, so
30 Hz, 60 Hz, slow, and stalled readers cannot mutate or schedule physics work.

Timing diagnostics include total steps, last and maximum native-step duration,
deadline misses, accumulated lag, command overflow/discard counts, exact health
flags, and MuJoCo-warning episodes. Individual over-budget native steps and bounded
catch-up backlog are both visible. Sleeping health is preserved without being
counted as a solver warning.

Native command, step, reset, and state-copy errors become retained typed faults. A
stale-command acceptance test proves that queued commands remain unapplied while
paused, apply at the next step boundary, and fault visibly when native sequencing
rejects them. Pause, resume, reset, shutdown, request timeout, and deterministic
handle closure are explicit handshakes. Resume resets the accumulator and excludes
suspended wall time rather than executing a catch-up burst.

The design and acceptance contract are documented in
[Authoritative simulation worker](architecture/AUTHORITATIVE_SIMULATION_WORKER.md).

### RMA-033 — snapshots and deterministic reset

RMA-033 is complete. The native snapshot envelope is independently versioned
and binds persisted state to the exact model, configuration, calibration
profile, authoritative sequence/time, command sequencing, health state, pending
wrench, and full MuJoCo integration state. Managed snapshots own immutable byte
copies and expose validated metadata without leaking backend-private payloads.

Restore is fail-closed and transactional. Version, model, calibration,
timestep, format, payload, sequence, and numeric-state mismatches return
`SNAPSHOT_INCOMPATIBLE`; any candidate state rejected after loading is rolled
back to the byte-identical live state. Same-runtime command and wrench replay
requires byte-identical final state and recaptured snapshot output.

The authoritative worker now serializes capture and restore as paused control
requests. Incompatible restore remains a nonfatal paused rejection with no
publication or queue mutation. Successful restore discards queued future
commands visibly, resets scheduler lag, publishes the restored immutable state,
and remains paused until explicit resume.

Stable named reset IDs are `SleepRest` and `NeutralAwake`. Neutral reset is
supported. Sleep/rest resolves only a named model keyframe; because the pinned
official Reachy model provides none, production returns typed `UNSUPPORTED`
instead of inventing a pose.

The detailed contract is in
[Simulation snapshots and deterministic reset](architecture/SIMULATION_SNAPSHOTS.md).

### RMA-040 — official model import and integrity

RMA-040 is complete. The clean pinned Pollen Robotics checkout at
`a739a6e461eb6d722901f1cfc225265ffc85c28d` is the immutable solver source. The
importer copies the MJCF, every referenced visual/collision mesh, and license
without content modification and emits deterministic per-file provenance.

The topology gate now locks all 17 named bodies plus the complete ordered 18-body
hierarchy, all joints and types, actuator mappings, sites, cameras, and equality
pairs. This moves body reparenting or order drift into the import gate before
native state indices or Unity transform identities can change. `MODEL_MAP.json`
is the machine-readable model contract; the anonymous camera-frame body remains
pinned at index 15 and receives only the presentation identity `__body_15`.

The source MuJoCo cameras remain model metadata but are excluded from the Unity
prefab. The versioned visual conversion is presentation-only and does not modify
the solver MJCF, collisions, ranges, inertias, actuators, or equality constraints.
MuJoCo 3.9.0 compiles the model with 19 bodies including world, 16 joints,
9 actuators, 5 equality constraints, 13 sites, `nq=37`, and `nv=30`.

The detailed import contract is in
[Official Reachy Mini model import](architecture/OFFICIAL_REACHY_MODEL_IMPORT.md).

### RMA-041 — mechanical parameter audit

RMA-041 is complete. The version-2 machine-readable audit binds the fidelity
profile to the exact pinned Reachy source commit and MJCF SHA-256, classifies every
parameter group, joint range, actuator/default class, and equality-solver setting,
and explicitly records which manufacturer, measured, fitted, and calibrated
evidence is absent.

Active `chosen_actuator` dynamics inherit the upstream `perfect_actuator` defaults
and remain a calibration-blocking placeholder. Antenna hinges remain placeholders
because the source encodes no hard-stop ranges; passive ball-joint and explicit
hinge limits remain upstream approximations rather than physical measurements.
The validator rejects any calibrated label while placeholders remain.

Joint-limit provenance is structured and per-joint. Upstream uncertainty comments
are bound to exact actuator classes or collision-mesh scope, including a source
location check that rejects identical text moved outside the applicable default
block. A display-ready diagnostics projection is checked against authoritative
fidelity/source fields so future UI cannot silently overstate model fidelity.

The detailed contract is in [Model parameter audit](model-parameter-audit.md), with
validation evidence in
[the RMA-041 validation record](validation/RMA_041_MODEL_PARAMETER_AUDIT_VALIDATION_2026-07-30.md).

### RMA-042 — desktop/Android reference-state comparison

RMA-042 is complete. A versioned scenario pins the exact Reachy model, MuJoCo
3.9.0 runtime, 500 Hz timestep, compiled dimensions, actuator/body order, command
phases, checkpoints, and numeric policies. Desktop Python MuJoCo generation and
the native Android ARM64 runner execute the same generated scenario, while a
compact SHA-256 lock requires byte-identical desktop fixture regeneration.

The comparator requires exact platform, scenario, model, runtime, count, step,
and body identities. It validates every qpos and qvel value, all named body poses,
normalized wxyz quaternions with q/-q equivalence, scenario-clock timing, zero
warnings, and the maximum absolute residual across every equality-constraint row.
Matching-but-malformed traces, non-finite values, wrong clocks, non-unit
quaternions, over-bound loop closures, and non-hexadecimal fixture hashes fail
visibly.

Physical LG-H872 API-26 evidence agrees with the desktop trace by orders of
magnitude inside all locked tolerances. The detailed contract is in
[Desktop/Android reference-state comparison](reference-state-comparison.md), with
validation evidence in
[the RMA-042 validation record](validation/RMA_042_REFERENCE_STATE_VALIDATION_2026-07-30.md).

### RMA-050 through RMA-052 — authoritative Unity rendering

The deterministic generated presentation contains all 18 non-world MuJoCo
bodies. The unnamed upstream body has the canonical identity `__body_15`; all
runtime identities are nonempty and unique.

The production state-format-v1 envelope publishes model identity, sequence,
simulation time, continuity, qpos, qvel, actuator observations, canonical body
poses, calibration identity, warnings, constraint counts, and residuals. The
managed parser validates all offsets, counts, identities, ordering, finiteness,
and quaternions, then publishes immutable pose pairs.

Unity interpolates by simulation timestamps, snaps across discontinuities, and
never feeds presentation transforms back into MuJoCo. Rigidbody, articulation,
Animator, Timeline, and other competing writers are rejected or detected.
Physical acceptance verifies body yaw, head, both antennas, all six Stewart
links, reset continuity, renderer health, and absence of a hidden kinematic
fallback.

The physical acceptance scripts now share one deterministic device contract:
wake, unlock, collapse overlays, acknowledge immersive confirmation, keep awake,
launch the exact Unity activity, verify focused-window ownership, capture
structured evidence, and restore device power policy.

## Current validation evidence

- Focused RMA-041 audit run `30587841758`: Ruff, 11 positive/failure-path tests,
  static evidence validation, and deterministic patch artifact publication passed
  before the exact validated bytes were applied to `master`.
- Hosted RMA-041 source gate in run `30588235631`: the official-model job passed
  the exact pinned Reachy source/topology/parameter audit, Unity visual conversion,
  MuJoCo compile/step, and desktop reference generation with the permanent v2
  contract. Temporary patch scaffolding observed only by that run's static job was
  subsequently removed at `a44d1f88`.
- Physical RMA-042 Android MuJoCo run `30583271127`: regenerated and locked the
  pinned desktop trace, cross-built the AArch64 runtime/reference runner, and
  passed the LG-H872 API-26 state/transform/equality comparison on `d229235d`.
- Hosted RMA-042 quality run `30583907077`: Ruff/actionlint/ShellCheck/static,
  native warnings/sanitizers, managed, Android, official-model, and desktop trace
  generation gates passed on `da6fb1fd`.
- Hosted RMA-040 run `30567896524`: full pinned-source/topology import,
  Unity visual conversion, MuJoCo compile/step and reference trace, static policy,
  native warnings/sanitizers, managed tests, and Android tests passed on
  `d096796c422e9d7e0353a1dca89295e490665b84`.
- Self-hosted RMA-040 run `30567896601`: production ARM64 staging, Unity tests,
  ARM64/API-26 IL2CPP build/verification, installed lifecycle acceptance,
  authoritative rendering, evidence uploads, and APK upload passed on the exact
  same commit.
- Hosted RMA-033 run `30561792617`: native warnings/sanitizers, managed
  warnings-as-errors and worker-owned snapshot acceptance, static checks,
  Android tests, and official-model validation passed on `1606bb55`.
- Self-hosted RMA-033 run `30561792261`: production staging, Unity tests,
  ARM64 API-26 IL2CPP build/verification, installed lifecycle acceptance,
  authoritative rendering, evidence uploads, and APK upload passed on the exact
  same commit after one isolated device scheduling timeout was cleared by an
  unchanged exact-job rerun.
- Hosted RMA-032 code-quality run `30539234220`: native warnings/sanitizers,
  managed warnings-as-errors and native-backed worker acceptance, static checks,
  Android tests, and official-model validation passed on `ac961fce`.
- Hosted RMA-031 code-quality run `30536538862`: native warnings/sanitizers,
  managed warnings-as-errors and lifecycle tests, static checks, Android tests,
  and official-model validation passed with the managed ABI/layout contract.
- Self-hosted Unity/Android exact-head run `30534082314`: production staging,
  Unity tests, APK build/verification, RMA-022 lifecycle, authoritative rendering,
  evidence uploads, and APK upload passed on `c109b13b` before the additive
  RMA-031 startup preflight.
- Production-identical Android MuJoCo run `30533169884`: ARM64 cross-build,
  architecture/provenance verification, and physical LG G6 probes passed on
  `22fdd1f4a47b14136ea2c85c918da1941684fc34`.

## Open hard gates

- RMA-060 long-duration official-model baseline dynamics and monitoring;
- RMA-012 offline licenses, attribution, and unofficial-project notice;
- API-31 development APK and release AAB validation;
- two-machine reproducible Unity/Android build evidence;
- later servo fidelity, calibration, application shell, camera, perception,
  speech, provider, behavior, privacy, diagnostics, performance, and release
  phases.

No open gate may be converted into a completed claim through a mock, hidden
fallback, suppressed warning, or fabricated measurement.
