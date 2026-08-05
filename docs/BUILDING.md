# Building the scaffold

## Pinned toolchain

The authoritative machine-readable versions are in `toolchain.lock.json`. The current pins are Unity 6000.5.2f1, Android Gradle Plugin 9.3.1, Gradle 9.5.0, JDK 17, Android API 37, Build Tools 36.0.0, NDK 28.2.13676358, CMake 3.31.6, .NET SDK channel 8.0, Ruff 0.12.0, ShellCheck 0.10.0, and actionlint 1.7.12.

Run:

```bash
python3 scripts/verify_toolchain.py
```

This also checks that `cmake`, `dotnet`, `ruff`, `shellcheck`, `java`, `actionlint`, Unity, and the Android SDK components on your machine match the pinned versions above (plus a bootstrap `gradle` executable, but only until `android-plugin/gradlew` has been generated — see "Android bridge" below). Use `--manifest-only` to validate just the lock file without requiring any of those tools to be installed.

## Static repository checks

```bash
./scripts/ci.sh --static-only
```

This validates the repository layout, Markdown links, third-party inventory, JSON files, and Python syntax. CI additionally runs Ruff, ShellCheck, actionlint, managed tests, and the native compiler warnings-as-errors build.

Install the lint tools at the pinned versions if they're missing:

```bash
pip install --user "ruff==0.12.0"
# ShellCheck and actionlint: use your platform's package manager, or download a
# prebuilt binary from https://www.shellcheck.net/ and
# https://github.com/rhysd/actionlint/releases — pin to 0.10.0 and 1.7.12 respectively.
```

```bash
ruff check scripts
ruff format --check --diff scripts
shellcheck scripts/*.sh
actionlint
```

## Native desktop build

```bash
./scripts/build_native.sh
REACHY_ENABLE_SANITIZERS=ON ./scripts/build_native.sh
```

The sanitizer build is a desktop-only first-party test configuration. It does not modify or rebuild third-party source with project warning flags.

## Managed .NET tests

Install the pinned .NET SDK channel (`toolchain.lock.json` → `quality_tools.dotnet_channel`, currently 8.0) without root using the official install script:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

The projects under `managed/` (`ReachyMini.Core.Tests`, `ReachyMini.Application.Tests`, `ReachyMini.Camera.Tests`) are standalone console runners (`OutputType: Exe`), not xUnit/NUnit test projects. **`dotnet test` silently reports zero tests and exits 0 without running anything** — always use `dotnet run`, matching how `scripts/ci.sh` and CI invoke them:

```bash
dotnet restore managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj
dotnet run \
    --project managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj \
    --configuration Release \
    --no-restore
```

`./scripts/ci.sh` runs `ReachyMini.Core.Tests` this way as part of the full local CI. `ReachyMini.Application.Tests` and `ReachyMini.Camera.Tests` aren't part of `ci.sh`; they're exercised by their own ticket-scoped CI gate workflows (e.g. `.github/workflows/rma102-gpu-homography-warp.yml`), but can be run locally the same way.

By default `ReachyMini.Core.Tests` only runs its in-process unit tests. To also exercise the native-interop lifecycle tests (matching CI's dedicated `managed` job), point it at the shared library `./scripts/build_native.sh` already produces:

```bash
export REACHY_MANAGED_NATIVE_LIBRARY_DIR="$(pwd)/build/native/native/reachy_sim"
export LD_LIBRARY_PATH="${REACHY_MANAGED_NATIVE_LIBRARY_DIR}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
export REACHY_MANAGED_NATIVE_TESTS=1
dotnet run --project managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj --configuration Release --no-restore
```

## MuJoCo Android feasibility build

See `docs/MUJOCO_ANDROID_FEASIBILITY.md`. The build requires a clean checkout at the pinned MuJoCo commit and Android NDK 28.2.13676358. It never patches third-party source or applies first-party warning flags to that source.

```bash
ANDROID_NDK_HOME=/path/to/ndk \
MUJOCO_SOURCE_DIR=/path/to/mujoco \
./scripts/build_mujoco_android.sh
```

The native feasibility artifacts target `arm64-v8a` with API 26 so they can run on the LG G6 test phone. This does not lower the provisional API 31 minimum for normal development or release application builds.

## Android bridge

The bridge uses a pinned Gradle distribution. The wrapper properties and distribution checksum are committed, but the binary wrapper JAR must be generated before the first bridge build. This needs a `gradle` executable to bootstrap from:

```bash
cd android-plugin
gradle wrapper --gradle-version 9.5.0 --distribution-type bin
```

If no `gradle` executable is available (a stale distro package is not sufficient — it must be able to produce the pinned 9.5.0 wrapper), download the pinned distribution directly and verify it against the checksum already committed in `android-plugin/gradle/wrapper/gradle-wrapper.properties` (same value as `android.gradle_distribution_sha256` in `toolchain.lock.json`) before using it to bootstrap:

```bash
curl -sSL -o gradle-9.5.0-bin.zip https://services.gradle.org/distributions/gradle-9.5.0-bin.zip
sha256sum gradle-9.5.0-bin.zip   # must equal android.gradle_distribution_sha256
unzip gradle-9.5.0-bin.zip
cd android-plugin
../gradle-9.5.0/bin/gradle wrapper --gradle-version 9.5.0 --distribution-type bin
```

The generated `gradlew`, `gradlew.bat`, and `gradle/wrapper/gradle-wrapper.jar` are **not committed** (see `docs/ASSET_POLICY.md`) — only `gradle/wrapper/gradle-wrapper.properties` is checked in. Regenerate them locally whenever needed; do not add them to git. Then run `./gradlew lint test` from `android-plugin/` (needs JDK 17, per `toolchain.lock.json`).

## Unity Android builds

Set `UNITY_EDITOR` to the exact pinned Unity executable and ensure at least one scene is enabled in Build Settings.

```bash
export UNITY_EDITOR=/home/phil/Unity/Hub/Editor/6000.5.2f1/Editor/Unity

./scripts/build_unity_android.sh device-feasibility
./scripts/build_unity_android.sh development
./scripts/build_unity_android.sh release
```

The `device-feasibility` command produces an ARM64 APK with a test-only API 26 floor for the connected LG G6. The normal development APK and release AAB retain the provisional API 31 floor. All Android builds use IL2CPP and ARM64; no Android x86_64 player target exists in Unity 6000.5.

The scaffold intentionally fails if no scene is configured rather than silently generating or selecting one.

## Run the MuJoCo feasibility probe on a phone

After a successful Android MuJoCo/probe build, connect one authorized physical ARM64 Android phone and run:

```bash
REACHY_ANDROID_SERIAL=LGH87250967ab9 \
./scripts/run_mujoco_probe_android.sh
```

Other ADB targets, including emulators, may remain online when `REACHY_ANDROID_SERIAL` identifies the physical phone. The default run performs 900,000 steps at a model timestep of 0.002 seconds and writes machine-readable timing plus device identification under `diagnostics-output/mujoco-probe/`. A first-party contract mock is used only for desktop boundary tests and does not satisfy the real-solver acceptance gate.

## Current limitations

The repository has not yet passed the two-machine Unity build acceptance criterion, Android bridge wrapper generation, physical-phone Unity installation, or MuJoCo Android feasibility gate. Those items remain incomplete in the authoritative TODO.
