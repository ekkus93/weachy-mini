# Implementation status

**Updated:** 2026-07-28  
**Branch:** `master`  
**Scaffold commit:** `ef97e022b28df3843a4c7f0d5a359d5acd3b13a8`

## Completed in the current Ralph-loop pass

### RMA-001 — Repository structure

Implemented the initial Unity, Android bridge, native wrapper, managed-test, model-manifest, calibration-schema, documentation, script, and third-party inventory layout.

Added:

- root project README with supported platform, maturity, build entry points, and links to the specification and TODO;
- Unity/Android/native/model/calibration credential and generated-output exclusions;
- text normalization and approved Git LFS patterns;
- an explicit asset and large-file policy.

### RMA-002 — Toolchain pins and build entry points

Pinned the initial compatibility-spike toolchain in `toolchain.lock.json`:

- Unity `6000.3.18f1` / Unity 6.3 LTS;
- Android API 37 compile/target and provisional API 31 minimum;
- Android Gradle Plugin 9.3.1;
- Gradle 9.5.0 with distribution checksum;
- JDK 17;
- Android NDK 28.2.13676358;
- CMake 3.31.6;
- Android ARM64 and Unity IL2CPP release configuration.

Added manifest validation plus native and Unity Android build entry-point scripts. The minimum Android API remains provisional until representative-device testing.

### RMA-003 — Baseline quality gates

Implemented first-party warning and lint policy:

- C warnings-as-errors with strict GCC/Clang/MSVC settings;
- desktop AddressSanitizer and UndefinedBehaviorSanitizer option for first-party native code;
- Java `-Xlint:all -Werror` and Android Lint warnings-as-errors;
- .NET analyzers and warnings-as-errors for the managed test harness;
- Ruff and ShellCheck CI jobs;
- repository structure, documentation link, inventory, and toolchain validators;
- GitHub Actions jobs for static, native, managed, and Android configuration checks.

No blanket warning suppression, lint baseline, or third-party source modification was added.

### RMA-010 — Initial third-party inventory

Added human-readable notices and a machine-readable inventory. Planned dependencies and candidate models are marked as not imported or blocked pending selection. No third-party source or binary is currently vendored or packaged.

## Validation performed in the implementation environment

Passed:

- toolchain manifest validation;
- repository scaffold validation;
- local Markdown link validation;
- third-party inventory validation;
- Python byte-code compilation;
- shell syntax validation;
- Java compilation with all lint warnings treated as errors;
- native GCC warnings-as-errors configure/build/test;
- native AddressSanitizer and UndefinedBehaviorSanitizer configure/build/test.

## Gates not yet claimed complete

The following cannot be marked complete until their required environment or evidence exists:

- Unity editor import/compilation and Unity test execution;
- development APK and release AAB build;
- Android Gradle wrapper JAR generation and full Android Lint/test execution;
- two-machine reproducible Unity/Android build evidence;
- physical ARM64 Android phone validation;
- MuJoCo Android cross-compilation and constrained-mechanism feasibility testing;
- in-app licenses screen.

Failures in these areas must remain visible. They must not be replaced by cosmetic Unity behavior, hidden provider changes, warning suppressions, or fabricated acceptance evidence.
