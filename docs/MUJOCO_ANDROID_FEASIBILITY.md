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

The script configures an Android `arm64-v8a` Release build for API 31, disables upstream examples, simulator UI, studio, tests, Python utility tests, OpenUSD, and Filament, then builds only the `mujoco` target.

The script deliberately does **not** add first-party warnings-as-errors flags to MuJoCo. Third-party source remains unmodified and isolated from the project's lint policy.

## Output checks

Successful output is staged under `Assets/Plugins/Android/libs/arm64-v8a/` and includes:

- `libmujoco.so`;
- `libmujoco.dynamic.txt` from NDK `llvm-readelf`;
- `libmujoco.exports.txt` from NDK `llvm-nm`;
- `BUILD_INFO.txt` with source and toolchain provenance.

The build fails if it detects desktop GL, GLX, X11, or GLFW dynamic dependencies. Loading and stepping on a physical phone remain separate acceptance gates.

## Current status

The script and validation paths are implemented, but this environment does not contain the pinned Android NDK or a physical Android device. RMA-020 remains incomplete until the cross-build succeeds and RMA-021/RMA-022 measurements are recorded.
