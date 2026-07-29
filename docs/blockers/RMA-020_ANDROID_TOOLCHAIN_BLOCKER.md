# RMA-020 Android toolchain execution blocker

**Recorded:** 2026-07-29  
**Affected tasks:** RMA-020, RMA-021, RMA-022  
**Status:** Hosted CI path implemented; successful hosted and physical-device evidence still required

## Required pinned inputs

The feasibility build is pinned to:

- Android NDK r28c, package revision `28.2.13676358`;
- MuJoCo 3.9.0 commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`;
- Android ABI `arm64-v8a`;
- Android platform API 31;
- CMake 3.31.6.

## Original execution-environment limitation

The initial implementation environment had sufficient disk space but could not resolve the Android download host and did not contain an Android SDK, NDK, `adb`, Unity editor, or physical Android phone.

That limitation is not evidence that MuJoCo cannot be built for Android. It only prevented the experiment from running in that environment.

## GitHub Actions path now implemented

`.github/workflows/android-feasibility.yml` now moves the toolchain-dependent portion to GitHub Actions.

The hosted Ubuntu job:

1. installs the exact SDK, NDK, and CMake packages through `sdkmanager`;
2. checks out the exact pinned MuJoCo commit;
3. validates that the checkout is clean and matches the repository lock file;
4. cross-compiles `libmujoco.so` and the first-party probe for ARM64;
5. verifies AArch64 ELF metadata, dynamic dependencies, exported symbols, and build provenance;
6. uploads the complete staged probe as an immutable workflow artifact for that commit.

The same workflow has a manual `device-probe` job. That job runs only when explicitly requested and only on a trusted self-hosted Linux runner labeled `weachy-mini-android-device`. It downloads the exact hosted artifact and runs it on exactly one authorized ARM64 Android phone.

The physical-device job is deliberately unavailable to pull-request events so untrusted pull-request code cannot execute on the trusted USB-connected machine.

## Work completed

- Exact MuJoCo source revision and license are pinned.
- The Android ARM64 CMake build harness validates source and toolchain revisions.
- Third-party MuJoCo source remains unmodified and does not inherit first-party warning flags.
- A first-party closed-loop MJCF fixture and malformed fixture are committed.
- A first-party probe checks fixed timestep, non-finite state, constraint residual, simulation-time advancement, warning count, and median/p95/maximum step duration.
- A 900,000-step contract test passes with strict warnings-as-errors and ASan/UBSan against a first-party MuJoCo API mock.
- The hosted cross-build workflow and artifact checks are committed.
- The self-hosted `adb` job and device-report artifact path are committed.
- A manual Unity validation workflow is committed for tests, development APK, and release AAB after Unity secrets are configured.

The mock validates our wrapper contract and failure handling only. It is not a substitute for running the real MuJoCo solver.

## Remaining setup

Follow `docs/ci/GITHUB_ACTIONS_SETUP.md` to:

1. review the hosted ARM64 build result;
2. register a trusted self-hosted runner with the `weachy-mini-android-device` label;
3. connect and authorize exactly one ARM64 Android phone;
4. manually run **Android MuJoCo Feasibility** with `run_device_probe` enabled;
5. configure Unity CI license secrets;
6. manually run **Unity Validation**.

RMA-020 through RMA-022 must remain open until the hosted build, physical-device load, 900,000-step timing, malformed-model result, Unity IL2CPP build, pause/resume behavior, failure visibility, and deterministic shutdown evidence are all recorded.
