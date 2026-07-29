# RMA-020 Android toolchain execution blocker

**Recorded:** 2026-07-28  
**Affected tasks:** RMA-020, RMA-021, RMA-022  
**Status:** Environment blocked; implementation remains incomplete

## Required external inputs

The feasibility build is pinned to:

- Android NDK r28c, package revision `28.2.13676358`;
- Linux package `android-ndk-r28c-linux.zip`;
- official package size `722261334` bytes;
- official SHA-1 `a7b54a5de87fecd125a17d54f73c446199e72a64`;
- MuJoCo 3.9.0 commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`.

## Evidence from this execution environment

- Available disk space was sufficient for the archive and extracted toolchain.
- The official NDK download URL was resolved from the Android NDK unsupported-downloads page.
- The managed download attempt failed.
- A direct `curl` attempt failed with `Could not resolve host: dl.google.com` after retries.
- No Android SDK, Android NDK, `adb`, Unity editor, or physical Android phone is available in this environment.

This is not evidence that MuJoCo cannot be built for Android. It means the required experiment could not be executed in this environment.

## Work completed despite the blocker

- Exact MuJoCo source revision and license are pinned.
- The Android ARM64 CMake build harness validates the source commit and NDK revision.
- Third-party MuJoCo source remains unmodified and does not inherit first-party warning flags.
- A first-party closed-loop MJCF fixture and malformed fixture are committed.
- A first-party probe checks fixed timestep, non-finite state, constraint residual, simulation-time advancement, warning count, and median/p95/maximum step duration.
- A 900,000-step contract test passes with strict warnings-as-errors and ASan/UBSan against a first-party MuJoCo API mock.
- An `adb` runner is prepared to verify malformed-model handling and collect phone/device timing evidence.

The mock validates our wrapper contract and failure handling only. It is not a substitute for running the real MuJoCo solver.

## Next required experiment

On a networked Linux developer machine:

```bash
sha1sum android-ndk-r28c-linux.zip
# Must equal a7b54a5de87fecd125a17d54f73c446199e72a64

ANDROID_NDK_HOME=/path/to/android-ndk-r28c \
MUJOCO_SOURCE_DIR=/path/to/mujoco-at-pinned-commit \
./scripts/build_mujoco_android.sh
```

Then connect exactly one authorized ARM64 Android phone and run:

```bash
./scripts/run_mujoco_probe_android.sh
```

RMA-020 through RMA-022 must remain open until the build, load, 900,000-step run, malformed-model result, pause/resume behavior, and device measurements are recorded.
