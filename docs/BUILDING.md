# Building the scaffold

## Pinned toolchain

The authoritative machine-readable versions are in `toolchain.lock.json`. The initial pins are Unity 6000.3.18f1, Android Gradle Plugin 9.3.1, Gradle 9.5.0, JDK 17, Android API 37, Build Tools 36.0.0, NDK 28.2.13676358, and CMake 3.31.6.

Run:

```bash
python3 scripts/verify_toolchain.py
```

Use `--manifest-only` to validate the lock file without requiring the SDKs to be installed.

## Static repository checks

```bash
./scripts/ci.sh --static-only
```

This validates the repository layout, Markdown links, third-party inventory, JSON files, Python syntax, and deterministic import tests. CI additionally runs Ruff, ShellCheck, managed tests, and the native compiler warnings-as-errors build.

## Native desktop build

```bash
./scripts/build_native.sh
REACHY_ENABLE_SANITIZERS=ON ./scripts/build_native.sh
```

The sanitizer build is a desktop-only first-party test configuration. It does not modify or rebuild third-party source with project warning flags.

## MuJoCo Android feasibility build

See `docs/MUJOCO_ANDROID_FEASIBILITY.md`. The build requires a clean checkout at the pinned MuJoCo commit and Android NDK 28.2.13676358. It never patches third-party source or applies first-party warning flags to that source.

```bash
ANDROID_NDK_HOME=/path/to/ndk \
MUJOCO_SOURCE_DIR=/path/to/mujoco \
./scripts/build_mujoco_android.sh
```

## Android bridge

The bridge uses a pinned Gradle distribution. The wrapper properties and distribution checksum are committed, but the binary wrapper JAR must be generated and verified before the first bridge build:

```bash
cd android-plugin
gradle wrapper --gradle-version 9.5.0 --distribution-type bin
```

After generation, verify the wrapper JAR against Gradle's published checksum before committing it. Then run `./gradlew lint test` from `android-plugin/`.

## Unity Android builds

Set `UNITY_EDITOR` to the exact pinned Unity executable and ensure at least one scene is enabled in Build Settings.

```bash
./scripts/build_unity_android.sh development
./scripts/build_unity_android.sh release
```

The development command produces an APK. The release command produces an AAB. Release uses IL2CPP and ARM64. The current scaffold intentionally fails if no scene is configured rather than silently generating or selecting one.

## Current limitations

The repository has not yet passed the two-machine Unity build acceptance criterion, Android bridge wrapper generation, physical-phone build, or MuJoCo Android feasibility gate. Those items remain incomplete in the authoritative TODO.
