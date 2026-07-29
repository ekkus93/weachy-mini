# Implementation status

**Updated:** 2026-07-29  
**Branch:** `master`  
**Current implementation series:** Phase 5 Reachy model import and integrity gate, with Phase 3 physical-device evidence still in progress

## Repository rules in force

- Work directly on `master`; do not create branches or pull requests unless the user explicitly changes that instruction.
- Every lint warning, analyzer warning, compiler warning, or error in first-party code is a bug and must be fixed at its source.
- Do not hide first-party warnings through pragmas, blanket suppressions, lint baselines, fake generated-code labels, or reduced warning levels.
- Do not modify third-party source to satisfy first-party lint policy. Keep third-party builds isolated and preserve upstream source.

## Implemented

### RMA-001 — Repository structure

The initial Unity, Android bridge, native wrapper, managed-test, model-manifest, calibration-schema, documentation, script, and third-party inventory layout exists. README, ignore rules, text/LFS policy, asset policy, and a minimal explicit bootstrap scene are present.

### RMA-002 — Toolchain and build entry points

The repository now pins Unity 6000.5.2f1 to match the installed editor on the `kawa` self-hosted runner. Android API/AGP/Gradle/JDK/NDK/CMake versions, IL2CPP, and ARM64 are pinned. Toolchain validation verifies that `ProjectVersion.txt` and `toolchain.lock.json` agree and fails visibly when the Unity Android Build Support module or pinned SDK packages are absent.

Unity 6000.5 no longer provides an Android x86_64 player target. The obsolete x86_64 emulator APK path has been removed. The build entry points are now:

- ARM64/API-26 physical-device feasibility APK;
- ARM64/API-31 development APK;
- ARM64/API-31 release AAB.

The API-26 feasibility target exists only to exercise the LG G6 test phone. It does not silently lower the normal application or release API floor.

GitHub Actions contains:

- hosted Android SDK installation, Android Lint, Java warnings-as-errors, and tests;
- hosted Unity test/APK/AAB validation through a manual GameCI workflow after Unity secrets are configured;
- hosted Android ARM64 MuJoCo cross-compilation;
- a trusted self-hosted `kawa` runner for local Unity validation and physical-phone jobs;
- a physical-phone selector that ignores emulator targets and chooses the sole connected ARM64 hardware device.

The two-machine Unity/Android reproducibility acceptance criterion remains open until successful run evidence exists on a second independently provisioned machine.

### RMA-003 — Baseline quality gates

First-party C, Java, managed, Python, shell, Android, and GitHub Actions workflow warning/lint policies are configured. The normal GitHub Actions workflow runs actionlint, Ruff lint/format, ShellCheck, native warnings-as-errors and sanitizer builds, managed analyzer/native-lifecycle tests, Android Lint, Java `-Werror`, Android tests, and static repository validation.

Hosted run `30486297200` passed all static, native, managed, and Android jobs for commit `28ec16bd5fb3ef7c68dd6f35192deb97c36d4e9b`. The issue-based status publishers expose queued, running, and completed workflow state without committing generated status files.

### RMA-010/RMA-011 — Third-party inventory and Reachy provenance

Reachy Mini source is pinned at commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`. The deterministic importer rejects dirty/mismatched sources, copies all MJCF-referenced meshes and the upstream license, and emits attribution plus per-file SHA-256 provenance. Fixture tests cover repeated deterministic output and visible failures.

Actual upstream asset import/release packaging and the in-app licenses screen remain open.

### RMA-020/RMA-021 preparation — MuJoCo and constrained probe

MuJoCo 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. The Android ARM64 build script preserves upstream source and isolates third-party warnings. A closed-loop equality-constraint fixture, malformed fixture, first-party probe, 900,000-step contract test, Android runner, timing report, and device-evidence path are implemented.

The native feasibility artifacts target API 26 and `arm64-v8a`. The connected LG G6 reports serial `LGH87250967ab9`, Android 8.0/API 26, and `arm64-v8a`. A separate x86_64 emulator may remain connected because the device workflow selects physical hardware explicitly.

A successful real MuJoCo load and 900,000-step report have not yet been produced, so the Phase 3 gate remains open.

### RMA-030/RMA-031 — Native ABI and managed interop

The repository contains a versioned explicit-width C ABI, structured recoverability, stale-handle and bounded-buffer checks, native contract tests, exact managed layouts, deterministic handle ownership, typed managed errors, and native lifecycle stress coverage. The public C ABI is version 2 after the snapshot-header expansion. The production backend continues to fail visibly until the real MuJoCo backend is linked; the contract backend is test-only.

### RMA-032 — Authoritative simulation worker

A managed-owned dedicated simulation thread, bounded command queue, step-boundary command application, immutable triple-buffered state publication, monotonic fixed-step timing, visible queue overflow, pause/resume/reset/shutdown handshakes, retained faults, and lifecycle tests are implemented.

Hosted native/managed gates and the `kawa` Unity tests passed the current implementation. Unity frame cadence remains separated from the authoritative worker, rendering stalls do not own mutable physics state, queue overflow is counted rather than overwritten silently, and resume does not execute a wall-time catch-up burst.

### RMA-033 — Snapshots and deterministic reset

Named sleep/rest and neutral-awake reset identifiers, snapshot format version 1, model identity, calibration-profile identity, immutable managed snapshot ownership, compatibility rejection, and deterministic replay tests are implemented. The native ABI is version 2; snapshot serialization is independently versioned.

The contract and failure behavior are documented in [Simulation snapshots and deterministic reset](architecture/SIMULATION_SNAPSHOTS.md). The deterministic test backend has zero replay tolerance: recaptured state and snapshot bytes must match exactly after restore and identical replay. Hosted native warnings-as-errors, ASan/UBSan, and managed lifecycle tests passed that coverage. Production MuJoCo snapshot payloads and floating-point tolerances remain part of the RMA-040/RMA-042 model integration work and must not be inferred from the test backend.

### Local Unity/Android validation

Self-hosted run `30486297221` on `kawa` passed Unity EditMode/PlayMode tests, the ARM64/API-26 IL2CPP APK build, APK verification, and artifact upload for commit `28ec16bd5fb3ef7c68dd6f35192deb97c36d4e9b`. Installation/launch was intentionally skipped because push validation does not mutate a connected phone.

## Verified evidence

- toolchain manifest and cross-file Unity-version validation;
- repository scaffold, Markdown links, inventory, deterministic importer, and probe-fixture tests;
- actionlint, Ruff lint/format, and ShellCheck;
- Java `-Xlint:all -Werror`, Android Lint, Gradle tests, and pinned Android SDK provisioning;
- first-party native warnings-as-errors and ASan/UBSan builds/tests;
- managed analyzers, 1,000-cycle native handle lifecycle coverage, and deterministic snapshot replay;
- 900,000-step constrained-probe contract test and malformed-model structured failure using the first-party test API mock;
- Unity EditMode and PlayMode tests on Unity 6000.5.2f1;
- ARM64/API-26 Unity APK build, verification, and artifact upload on `kawa`;
- ADB authorization and ARM64/API-26 identification of the LG G6.

## Open hard gates

- successful ARM64/API-26 feasibility APK installation and launch on the LG G6;
- successful hosted real-MuJoCo Android ARM64 cross-build artifact;
- physical-phone MuJoCo library load and 900,000-step timing report;
- development APK and release AAB builds;
- two-machine reproducible build evidence;
- pause/resume and shutdown lifecycle validation on Android hardware;
- imported official Reachy MJCF/mesh package and generated body/joint/actuator map;
- desktop/Android reference-state comparison for the full Reachy model;
- in-app licenses and attribution screen.

No open gate may be converted into a completed checkbox by substituting a mock, cosmetic Unity animation, hidden fallback, suppressed warning, or fabricated measurement.
