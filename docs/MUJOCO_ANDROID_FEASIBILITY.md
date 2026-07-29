# MuJoCo Android ARM64 feasibility build

MuJoCo 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. Upstream does not advertise an Android build target, so this is an explicit feasibility experiment rather than a supported packaging recipe.

## Source checkout

```bash
git clone https://github.com/google-deepmind/mujoco.git /path/to/mujoco
git -C /path/to/mujoco checkout --detach 237c17e48539b6c90bf90d3161547cbdcbfaa1e0
git -C /path/to/mujoco status --short
```

The checkout must be clean and at the exact commit. The build script does not patch or edit third-party source.

## Build

Install Android NDK `28.2.13676358`, CMake `3.31.6`, and Ninja, then run:

```bash
ANDROID_NDK_HOME=/path/to/android-ndk-r28c \
MUJOCO_SOURCE_DIR=/path/to/mujoco \
./scripts/build_mujoco_android.sh
```

The script configures an Android `arm64-v8a` Release build for API 31, uses the static Android C++ runtime, disables upstream examples, simulator UI, studio, tests, Python utility tests, OpenUSD, and Filament, then builds only the `mujoco` target.

The script deliberately does **not** add first-party warnings-as-errors flags to MuJoCo. Third-party source remains unmodified and isolated from the project's lint policy.

It then builds the first-party probe runner with strict warnings-as-errors against the staged MuJoCo library.

## Staged output

Successful output is staged under `Assets/Plugins/Android/libs/arm64-v8a/` and includes:

- `libmujoco.so`;
- `reachy_mujoco_probe_runner`;
- `closed_loop_probe.xml`;
- `malformed_probe.xml`;
- `libmujoco.dynamic.txt` from NDK `llvm-readelf`;
- `libmujoco.exports.txt` from NDK `llvm-nm`;
- `BUILD_INFO.txt` with source and toolchain provenance.

The build fails if it detects desktop GL, GLX, X11, or GLFW dynamic dependencies.

## Probe behavior

The valid fixture contains an equality `connect` constraint and a 0.002-second timestep. The runner defaults to 900,000 steps, corresponding to 30 simulated minutes. It reports:

- completed steps and simulated time;
- maximum equality-constraint residual;
- median, p95, and maximum step duration;
- accumulated MuJoCo warning count;
- structured status and error text.

During every step, it rejects non-finite position, velocity, acceleration, activation, control, or constraint arrays; excessive constraint residual; and simulation time that does not advance.

The malformed fixture must return `model_load_failed` as JSON rather than crash.

## Physical-phone run

Connect exactly one authorized phone, then run:

```bash
./scripts/run_mujoco_probe_android.sh
```

The runner pushes the staged files to `/data/local/tmp`, verifies malformed-model handling first, executes the long probe, and records JSON timing plus phone manufacturer, model, device, Android release, SDK, and ABI under `diagnostics-output/mujoco-probe/`.

## Contract-mock limitation

Desktop CI builds the first-party probe against a first-party API mock. That test exercises argument validation, structured load failure, finite-state checks, timing aggregation, cleanup, and the full 900,000-step control path. It does **not** validate MuJoCo dynamics or Android performance.

## Current status

The build/probe paths are implemented and first-party normal and sanitizer tests pass. This environment could not download the pinned NDK because external DNS resolution failed and has no physical phone. The gate remains incomplete; see `docs/blockers/RMA-020_ANDROID_TOOLCHAIN_BLOCKER.md`.
