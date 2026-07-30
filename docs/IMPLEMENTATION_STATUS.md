# Implementation status

**Updated:** 2026-07-30  
**Branch:** `master`  
**Current implementation series:** Production MuJoCo state publication, Unity
pose binding, physical lifecycle/rendering acceptance, and RMA-030 native handle
concurrency hardening

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

### RMA-030/RMA-031 — native ABI and managed interop

The public C ABI remains version 2. It uses explicit-width/versioned structures,
generation-bearing opaque handles, typed status/recoverability, exact command
records, state/wrench/snapshot operations, and caller-owned error data. The
managed layer uses exact layouts and deterministic `SafeHandle` ownership.

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

The detailed contracts are in [Simulation ABI](SIMULATION_ABI.md) and
[Native handle concurrency](architecture/NATIVE_HANDLE_CONCURRENCY.md).

### RMA-032/RMA-033 — authoritative worker and snapshots

A managed-owned dedicated fixed-step worker owns mutable simulation work,
applies commands at step boundaries, publishes immutable state, retains faults,
and provides explicit pause/resume/reset/shutdown handshakes. Queue overflow is
visible, rendering stalls do not own physics state, and resume does not execute a
wall-time catch-up burst.

Production snapshots are versioned, model/configuration/calibration bound,
transactionally restored, and require byte-identical recapture/replay. Neutral
reset is supported. Sleep/rest returns `UNSUPPORTED` because the pinned upstream
model has no named sleep/rest keyframe; no pose is fabricated.

### RMA-040 through RMA-042 — official model integrity

The complete official model is imported and compiled with 19 bodies including
world, 16 joints, 9 actuators, 5 equality constraints, 13 sites, `nq=37`, and
`nv=30`. Desktop/Android reference traces compare qpos, qvel, all named body
transforms, equality residuals, warnings, dimensions, hashes, and MuJoCo version
within locked tolerances.

The mechanical audit explicitly classifies the generic actuator dynamics and
missing antenna hard-stop evidence as uncalibrated placeholders. No calibrated
claim is made.

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

- Hosted RMA-030 exact-head quality run `30534082373`: all jobs passed on
  `c109b13b7909efee017d32352f4ba2a973cf1447`.
- Self-hosted Unity/Android exact-head run `30534082314`: production staging,
  Unity tests, APK build/verification, RMA-022 lifecycle, authoritative rendering,
  evidence uploads, and APK upload passed on `c109b13b`.
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
