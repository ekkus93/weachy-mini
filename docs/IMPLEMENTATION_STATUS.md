# Implementation status

**Updated:** 2026-07-29  
**Branch:** `master`  
**Current implementation series:** Phase 1 foundation, source provenance, and MuJoCo feasibility preparation

## Repository rules in force

- Work directly on `master`; do not create branches or pull requests unless the user explicitly changes that instruction.
- Every lint warning, analyzer warning, compiler warning, or error in first-party code is a bug and must be fixed at its source.
- Do not hide first-party warnings through pragmas, blanket suppressions, lint baselines, fake generated-code labels, or reduced warning levels.
- Do not modify third-party source to satisfy first-party lint policy. Keep third-party builds isolated and preserve upstream source.

## Implemented

### RMA-001 — Repository structure

The initial Unity, Android bridge, native wrapper, managed-test, model-manifest, calibration-schema, documentation, script, and third-party inventory layout exists. README, ignore rules, text/LFS policy, and asset policy are present.

### RMA-002 — Toolchain and build entry points

The repository pins Unity 6000.3.18f1, Android API/AGP/Gradle/JDK/NDK/CMake versions, IL2CPP ARM64 configuration, and development/release build entry points. Toolchain validation fails visibly when required installations differ or are absent.

GitHub Actions now contains:

- hosted Android SDK installation, Android Lint, Java warnings-as-errors, and tests;
- hosted Unity test/APK/AAB validation through a manual GameCI workflow after Unity secrets are configured;
- hosted Android ARM64 MuJoCo cross-compilation;
- an opt-in self-hosted physical-phone job that consumes the exact hosted-build artifact.

The two-machine Unity/Android reproducibility acceptance criterion remains open until successful run evidence exists.

### RMA-003 — Baseline quality gates

First-party C, Java, managed, Python, shell, Android, and GitHub Actions workflow warning/lint policies are configured. Native normal and ASan/UBSan test builds pass locally with warnings-as-errors. Static repository, documentation, inventory, Python, Java, shell-syntax, and deterministic-import checks pass in the available environment.

The normal GitHub Actions workflow now runs `actionlint`, Ruff, Ruff format, ShellCheck, native strict/sanitizer builds, managed analyzer tests, Android Lint, Java `-Werror`, and Android tests. Connected-tool status reporting does not expose push-triggered check runs, so newly added hosted jobs are not claimed as passed until their Actions results are reviewed.

### RMA-010/RMA-011 — Third-party inventory and Reachy provenance

Reachy Mini source is pinned at commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`. The deterministic importer rejects dirty/mismatched sources, copies all MJCF-referenced meshes and the upstream license, and emits attribution plus per-file SHA-256 provenance. Fixture tests cover repeated deterministic output and visible failures.

Actual upstream asset import/release packaging and the in-app licenses screen remain open.

### RMA-020/RMA-021 preparation — MuJoCo and constrained probe

MuJoCo 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. The Android ARM64 build script preserves upstream source and isolates third-party warnings. A closed-loop equality-constraint fixture, malformed fixture, first-party probe, 900,000-step contract test, Android runner, timing report, and device-evidence path are implemented.

The hosted `Android MuJoCo Feasibility` workflow now installs the exact NDK/CMake packages, checks out the exact MuJoCo commit, cross-compiles the ARM64 library and probe, verifies the ELF architecture/dependencies/provenance, and uploads the result. A manual device option routes the artifact to a trusted runner labeled `weachy-mini-android-device`.

Physical-phone execution remains incomplete until that self-hosted runner is registered and a successful report is produced. See `docs/ci/GITHUB_ACTIONS_SETUP.md` and `docs/blockers/RMA-020_ANDROID_TOOLCHAIN_BLOCKER.md`.

## Locally verified evidence

- toolchain manifest validation;
- repository scaffold validation;
- local Markdown link validation;
- third-party inventory validation;
- Python byte-code compilation;
- importer and probe-fixture Python tests;
- shell syntax validation;
- Java `-Xlint:all -Werror` compilation for the bridge scaffold;
- first-party native strict warnings-as-errors build and tests;
- first-party native ASan/UBSan build and tests;
- 900,000-step probe contract test and malformed-model structured failure using the first-party API mock.

## Open hard gates

- successful hosted Android Lint/test result;
- successful hosted MuJoCo Android ARM64 cross-build artifact;
- Unity license-secret configuration and successful Unity tests;
- development APK and release AAB builds;
- two-machine reproducible build evidence;
- self-hosted physical-phone library load and 900,000-step timing;
- pause/resume and shutdown lifecycle validation;
- in-app licenses and attribution screen.

No open gate may be converted into a completed checkbox by substituting a mock, cosmetic Unity animation, hidden fallback, suppressed warning, or fabricated measurement.
