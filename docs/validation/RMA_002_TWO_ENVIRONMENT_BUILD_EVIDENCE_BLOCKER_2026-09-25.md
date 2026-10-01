# RMA-002 two-environment build evidence blocker — 2026-09-25

## Scope

This note records the remaining RMA-002 acceptance gap on current `master`.
It does **not** close the RMA-002 checklist item.

The still-open acceptance criterion is:

> Two clean developer environments can build the same minimal Android application.

## Current evidence boundary

The repository already has strong single-environment and CI-oriented toolchain evidence:

- `toolchain.lock.json` pins Unity, Android Gradle Plugin, Gradle, JDK, NDK, CMake, compile/target SDK, IL2CPP, and ARM64 settings.
- `scripts/verify_toolchain.py --manifest-only` checks pinned manifest consistency in CI.
- `Assets/ReachyMini/Editor/AndroidBuild.cs` configures Unity Android builds deterministically from the pinned toolchain assumptions.
- Numerous hosted and self-hosted validation runs have built Android APKs/AAB-path artifacts on known runners.

Those facts are useful, but they are **not** the RMA-002 two-clean-environment acceptance evidence. A CI runner plus one developer/running host, or repeated runs on the same host, must not be reinterpreted as two independent clean developer environments.

## Required closure evidence

To close the RMA-002 acceptance criterion, a future validation must provide evidence equivalent to:

1. two independently provisioned clean environments;
2. exact environment identities, including OS, Unity editor version, JDK, Android SDK/NDK/CMake, Gradle/AGP, and repository SHA;
3. both environments running the same minimal Android build command or documented equivalent;
4. resulting build metadata or artifact hash comparison sufficient to prove the same minimal Android application was built;
5. failure logs if either environment initially fails, plus the exact source/tooling remediation if any source change is needed.

## Non-claims

This blocker note does not claim reproducibility, does not claim a second developer machine exists, and does not mark the TODO checkbox complete.

The lack of this evidence is not a source-code failure; it is an external qualification dependency requiring a second clean Unity/Android environment or explicit user-provided evidence from one.
