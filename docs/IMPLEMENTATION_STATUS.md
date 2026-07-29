# Implementation status

**Updated:** 2026-07-29  
**Branch:** `master`  
**Current implementation series:** Phase 5 model integrity; RMA-042 desktop/Android reference-state comparison is next

## Repository rules in force

- Work directly on `master`; do not create branches or pull requests unless the user explicitly changes that instruction.
- Every lint warning, analyzer warning, compiler warning, or error in first-party code is a bug and must be fixed at its source.
- Do not hide first-party warnings through pragmas, blanket suppressions, lint baselines, fake generated-code labels, or reduced warning levels.
- Do not modify third-party source to satisfy first-party lint policy. Keep third-party builds isolated and preserve upstream source.

## Implemented

### RMA-001 — Repository structure

The initial Unity, Android bridge, native wrapper, managed-test, model-manifest, calibration-schema, documentation, script, and third-party inventory layout exists. README, ignore rules, text/LFS policy, asset policy, and a minimal explicit bootstrap scene are present.

### RMA-002 — Toolchain and build entry points

The repository pins Unity 6000.5.2f1 to match the installed editor on the `kawa` self-hosted runner. Android API/AGP/Gradle/JDK/NDK/CMake versions, IL2CPP, and ARM64 are pinned. Toolchain validation verifies that `ProjectVersion.txt` and `toolchain.lock.json` agree and fails visibly when the Unity Android Build Support module or pinned SDK packages are absent.

Unity 6000.5 no longer provides an Android x86_64 player target. The build entry points are:

- ARM64/API-26 physical-device feasibility APK;
- ARM64/API-31 development APK;
- ARM64/API-31 release AAB.

The API-26 feasibility target exists only to exercise the LG G6 test phone. It does not silently lower the normal application or release API floor.

GitHub Actions contains:

- hosted Android SDK installation, Android Lint, Java warnings-as-errors, and tests;
- hosted Unity test/APK/AAB validation through a manual GameCI workflow after Unity secrets are configured;
- hosted Android ARM64 MuJoCo cross-compilation;
- a trusted self-hosted `kawa` runner for local Unity validation and physical-phone jobs;
- physical-phone selection that ignores emulator targets and requires the sole connected ARM64 hardware device;
- stable issue-based status endpoints for hosted quality, local Unity, and Android MuJoCo feasibility workflows.

The two-machine Unity/Android reproducibility acceptance criterion remains open until successful run evidence exists on a second independently provisioned machine.

### RMA-003 — Baseline quality gates

First-party C, Java, managed, Python, shell, Android, and GitHub Actions workflow warning/lint policies are configured. The normal GitHub Actions workflow runs actionlint, Ruff lint/format, ShellCheck, native warnings-as-errors and sanitizer builds, managed analyzer/native-lifecycle tests, Android Lint, Java `-Werror`, Android tests, static repository validation, pinned Reachy topology validation, parameter-audit validation, and a desktop MuJoCo model-load test.

Hosted run `30496186915` passed all static, native, managed, Android, and official-model jobs for commit `29c175cd5c136f1e15fa5759daddcc3d57f0e99c`.

### RMA-010/RMA-011 — Third-party inventory and Reachy provenance

Reachy Mini source is pinned at commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`. The deterministic importer rejects dirty or mismatched sources, copies all MJCF-referenced meshes and the upstream license, and emits attribution plus per-file SHA-256 provenance. Fixture tests cover repeated deterministic output, duplicate names, topology drift, and preservation of previous known-good output after a failed import.

Release-package inventory reconciliation and the in-app licenses screen remain open.

### RMA-020 — MuJoCo Android ARM64 build

MuJoCo 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. The Android ARM64 build preserves upstream source, isolates third-party warnings, records compiler/provenance information, and produces an API-26 `arm64-v8a` shared library and probe executable. A first-party API-26 compatibility shim supplies checked `aligned_alloc` behavior through `posix_memalign` without modifying MuJoCo source.

Android feasibility run `30494266978` built and verified the AArch64 artifact, uploaded it, loaded `libmujoco.so` on the LG G6, and completed the physical probe job successfully.

### RMA-021 — Minimal constrained-mechanism physical test

The constrained fixture contains an equality loop closure and runs at a 0.002-second timestep. The probe measures equality rows separately from contact rows, rejects NaN/Inf and timing or residual failures, and returns structured malformed-model errors.

On LG G6 `LGH87250967ab9` running Android 8.0/API 26, run `30494266978` completed:

- `900000` steps;
- `1799.999999971` simulated seconds;
- maximum equality residual `3.67318514288284e-06`;
- median step time `17.708000087` microseconds;
- p95 step time `17.760998162` microseconds;
- maximum step time `336.302997312` microseconds;
- zero MuJoCo warnings;
- structured malformed-model failure without a crash.

The measured result leaves substantial execution headroom relative to a 2,000-microsecond 500 Hz step budget on this fixture. It does not by itself establish full-model 500 Hz headroom under all application workloads.

### RMA-030/RMA-031 — Native ABI and managed interop

The repository contains a versioned explicit-width C ABI, structured recoverability, stale-handle and bounded-buffer checks, native contract tests, exact managed layouts, deterministic handle ownership, typed managed errors, and native lifecycle stress coverage. The public C ABI is version 2 after the snapshot-header expansion. The production backend still fails visibly until the real MuJoCo backend is connected to the application ABI; the contract backend is test-only.

Final caller-output-pointer validation and per-handle operation serialization or a typed `HANDLE_BUSY` result remain open safety-hardening items.

### RMA-032 — Authoritative simulation worker

A managed-owned dedicated simulation thread, bounded command queue, step-boundary command application, immutable triple-buffered state publication, monotonic fixed-step timing, visible queue overflow, pause/resume/reset/shutdown handshakes, retained faults, and lifecycle tests are implemented.

Hosted native/managed gates and the `kawa` Unity tests passed the implementation. Unity frame cadence remains separated from the authoritative worker, rendering stalls do not own mutable physics state, queue overflow is counted rather than overwritten silently, and resume does not execute a wall-time catch-up burst.

### RMA-033 — Snapshots and deterministic reset

Named sleep/rest and neutral-awake reset identifiers, snapshot format version 1, model identity, calibration-profile identity, immutable managed snapshot ownership, compatibility rejection, and deterministic replay tests are implemented. The native ABI is version 2; snapshot serialization is independently versioned.

The contract and failure behavior are documented in [Simulation snapshots and deterministic reset](architecture/SIMULATION_SNAPSHOTS.md). The deterministic test backend has zero replay tolerance: recaptured state and snapshot bytes must match exactly after restore and identical replay. Production MuJoCo snapshot payloads and floating-point tolerances remain part of the real backend and RMA-042 integration work and must not be inferred from the test backend.

### RMA-040 — Official Reachy Mini model integrity

The pinned official MJCF and all referenced mesh assets are imported with provenance. `MODEL_MAP.json` records the complete body, joint, actuator, equality, site, and camera topology. CI rejects missing names, duplicate names, changed topology, source-hash drift, or a dirty/mismatched source checkout.

Desktop MuJoCo 3.9.0 compiles and steps the model. The physical Android probe on the LG G6 also loaded the complete model and completed 100 steps with:

- 19 bodies including world;
- 16 joints;
- 9 actuators;
- 5 equality constraints;
- 13 sites;
- 2 cameras;
- `nq=37`, `nv=30`;
- maximum equality residual `3.028836354224129e-07`;
- median step time `335.91149986` microseconds;
- p95 step time `366.151155322` microseconds;
- maximum step time `551.249999262` microseconds;
- zero MuJoCo warnings.

The model source, compiled dimensions, coordinate units, quaternion order, and initial `xl_330` reference pose are pinned in `models/reachy-mini/model-baseline.json`.

### RMA-041 — Mechanical parameter audit

The human-readable audit is [Reachy Mini model parameter audit](model-parameter-audit.md). The authoritative machine-readable audit is `models/reachy-mini/model-parameter-audit.json`.

The audit classifies geometry and inertial data as CAD-derived, explicit upstream joint ranges and equality settings as upstream approximations, and the active `chosen_actuator` dynamics plus missing antenna hard-stop ranges as placeholders. It preserves upstream uncertainty comments, records every active joint and actuator, and explicitly records the absence of manufacturer, measured, fitted, or calibrated evidence.

CI rejects source/hash drift, changed ranges or inherited actuator constants, missing uncertainty comments, unknown classifications, and any calibrated label while placeholders remain. Hosted run `30496186915` passed static policy tests and exact validation against the pinned upstream MJCF.

### Local Unity/Android validation

Self-hosted run `30494266944` on `kawa` passed Unity EditMode/PlayMode tests, the ARM64/API-26 IL2CPP APK build, APK verification, and artifact upload for commit `66072eedaf66f2812cc8e8e69ee59db2629e935d`. Push validation intentionally does not install or launch the Unity APK on the connected phone.

## Verified evidence

- toolchain manifest and cross-file Unity-version validation;
- repository scaffold, Markdown links, inventory, deterministic importer, and topology-failure tests;
- actionlint, Ruff lint/format, ShellCheck, and static repository checks;
- Java `-Xlint:all -Werror`, Android Lint, Gradle tests, and pinned Android SDK provisioning;
- first-party native warnings-as-errors and ASan/UBSan builds/tests;
- managed analyzers, 1,000-cycle native handle lifecycle coverage, and deterministic snapshot replay;
- ARM64/API-26 MuJoCo build, AArch64/provenance verification, artifact upload, and physical library execution;
- 900,000-step constrained-model physical report and structured malformed-model failure;
- official Reachy model desktop and Android load/step evidence with matching compiled counts;
- source-linked machine-readable mechanical-parameter fidelity audit;
- Unity EditMode and PlayMode tests on Unity 6000.5.2f1;
- ARM64/API-26 Unity APK build, verification, and artifact upload on `kawa`;
- ADB authorization and ARM64/API-26 identification of the LG G6.

## Open hard gates

- RMA-022 physical installation/launch of the Unity IL2CPP APK with real native-wrapper initialization, failure visibility, pause/resume, and shutdown validation;
- connection of the production `reachy_sim` ABI backend to real MuJoCo model/state/command/snapshot operations;
- caller-output-pointer validation and per-handle concurrency safety in the native ABI;
- development APK and release AAB builds;
- two-machine reproducible build evidence;
- desktop/Android reference-state comparison for reset and representative command traces (RMA-042);
- in-app licenses, attribution, and unofficial-project notice (RMA-012);
- all later rendering, dynamics, calibration, camera, perception, speech, LLM/provider, behavior, privacy, diagnostics, performance, and release phases.

No open gate may be converted into a completed checkbox by substituting a mock, cosmetic Unity animation, hidden fallback, suppressed warning, or fabricated measurement.
