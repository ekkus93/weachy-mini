# Implementation status

**Updated:** 2026-07-29  
**Branch:** `master`  
**Current implementation series:** Phase 4 simulation ABI and authoritative-thread foundation, with Phase 3 physical-device evidence in progress

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

The two-machine Unity/Android reproducibility acceptance criterion remains open until successful run evidence exists.

### RMA-003 — Baseline quality gates

First-party C, Java, managed, Python, shell, Android, and GitHub Actions workflow warning/lint policies are configured. Native normal and ASan/UBSan test builds pass locally with warnings-as-errors. Static repository, documentation, inventory, Python, Java, shell-syntax, and deterministic-import checks pass in the available environment.

The normal GitHub Actions workflow runs `actionlint`, Ruff, Ruff format, ShellCheck, native strict/sanitizer builds, managed analyzer tests, Android Lint, Java `-Werror`, and Android tests. Newly added workflow/toolchain changes are not marked passed until their current Actions results are reviewed.

### RMA-010/RMA-011 — Third-party inventory and Reachy provenance

Reachy Mini source is pinned at commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`. The deterministic importer rejects dirty/mismatched sources, copies all MJCF-referenced meshes and the upstream license, and emits attribution plus per-file SHA-256 provenance. Fixture tests cover repeated deterministic output and visible failures.

Actual upstream asset import/release packaging and the in-app licenses screen remain open.

### RMA-020/RMA-021 preparation — MuJoCo and constrained probe

MuJoCo 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. The Android ARM64 build script preserves upstream source and isolates third-party warnings. A closed-loop equality-constraint fixture, malformed fixture, first-party probe, 900,000-step contract test, Android runner, timing report, and device-evidence path are implemented.

The native feasibility artifacts target API 26 and `arm64-v8a`. The connected LG G6 reports serial `LGH87250967ab9`, Android 8.0/API 26, and `arm64-v8a`. A separate x86_64 emulator may remain connected because the device workflow selects physical hardware explicitly.

A successful real MuJoCo load and 900,000-step report have not yet been produced, so the Phase 3 gate remains open.

### RMA-030/RMA-031 — Native ABI and managed interop

The repository contains a versioned explicit-width C ABI, structured recoverability, stale-handle and bounded-buffer checks, native contract tests, exact managed layouts, deterministic handle ownership, typed managed errors, and native lifecycle stress coverage. The production backend continues to fail visibly until the real MuJoCo backend is linked; the contract backend is test-only.

### RMA-032 — Authoritative simulation worker

A managed-owned dedicated simulation thread, bounded command queue, step-boundary command application, immutable snapshot publication, monotonic fixed-step timing, visible queue overflow, pause/resume/reset/shutdown handshakes, and lifecycle tests are implemented. The implementation still requires successful current CI and Unity compilation evidence before the task can be marked complete.

## Locally verified evidence

- toolchain manifest validation before the Unity 6.5 repin;
- repository scaffold validation;
- local Markdown link validation;
- third-party inventory validation;
- Python byte-code compilation;
- importer and probe-fixture Python tests;
- shell syntax validation;
- Java `-Xlint:all -Werror` compilation for the bridge scaffold;
- first-party native strict warnings-as-errors build and tests;
- first-party native ASan/UBSan build and tests;
- 900,000-step probe contract test and malformed-model structured failure using the first-party API mock;
- successful GitHub job assignment to the `kawa` self-hosted runner;
- ADB authorization and ARM64/API-26 identification of the LG G6.

## Open hard gates

- current hosted CI success after the Unity 6.5/API-26 repin;
- Unity Android Build Support availability for 6000.5.2f1 on `kawa`;
- successful Unity edit-mode and play-mode tests on `kawa`;
- successful ARM64/API-26 feasibility APK build and installation on the LG G6;
- successful hosted MuJoCo Android ARM64 cross-build artifact;
- physical-phone MuJoCo library load and 900,000-step timing report;
- development APK and release AAB builds;
- two-machine reproducible build evidence;
- pause/resume and shutdown lifecycle validation on Android;
- in-app licenses and attribution screen.

No open gate may be converted into a completed checkbox by substituting a mock, cosmetic Unity animation, hidden fallback, suppressed warning, or fabricated measurement.
