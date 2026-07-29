# GitHub Actions CI setup

This project uses GitHub Actions for hosted static analysis, Android lint/tests, native tests, Android ARM64 MuJoCo cross-compilation, and Unity validation. Physical-phone jobs use the separately labeled self-hosted `kawa` runner because a GitHub-hosted virtual machine cannot access a USB phone attached to the developer machine.

## Workflows

### `.github/workflows/ci.yml`

Runs automatically for pushes to `master` and pull requests.

It performs:

- `actionlint` validation of first-party GitHub Actions workflows;
- Ruff lint and formatting checks for first-party Python;
- ShellCheck for first-party shell scripts;
- repository, documentation, toolchain-manifest, and inventory checks;
- strict native compilation and tests with warnings as errors;
- ASan/UBSan native tests;
- managed analyzer/build tests with warnings as errors;
- Android SDK installation;
- Android Lint with warnings as errors;
- Java compilation with `-Xlint:all -Werror`;
- Android unit tests.

Third-party source is not rewritten or subjected to the repository's first-party warning policy.

### `.github/workflows/local-unity-android-validation.yml`

Runs on trusted pushes to `master` and may also be started manually. It targets the `kawa` runner through these labels:

```text
self-hosted
linux
x64
weachy-mini-android-device
```

Automatic push runs perform Unity edit-mode and play-mode tests and build the ARM64/API-26 physical-device feasibility APK. A manual input can additionally install and launch that APK on the sole connected physical ARM64 phone. Emulator targets are ignored by the physical-device selector.

The workflow is pinned to Unity 6000.5.2f1. It requires the matching Unity Android Build Support module; missing modules or mismatched editor versions fail visibly.

### `.github/workflows/android-feasibility.yml`

The hosted `build-arm64` job runs automatically when the MuJoCo build/probe inputs change and can also be started manually.

It:

1. reads Android and MuJoCo versions from the repository lock files;
2. installs the exact Android SDK, NDK, and CMake packages through `sdkmanager`;
3. checks out the exact pinned MuJoCo commit;
4. validates that the checkout is clean and matches the lock file;
5. cross-compiles MuJoCo and the first-party probe for `arm64-v8a` at the separately pinned native-feasibility API floor;
6. verifies the produced ELF architecture, API floor, dynamic dependencies, symbols, and provenance;
7. uploads the staged library, runner, fixtures, and reports as a GitHub Actions artifact.

The optional `device-probe` job downloads that exact artifact and runs the 900,000-step probe on one physical ARM64 phone. The current native-feasibility floor is API 26, independently of the full Unity application's provisional API 31 minimum. An x86_64 Android emulator may remain online because the workflow excludes `emulator-*` serials.

### `.github/workflows/unity-validation.yml`

This is intentionally manual and hosted. It runs Unity tests plus the API-26 feasibility APK, normal development APK, and release AAB entry points through GameCI after Unity license secrets are configured.

A failed or absent Unity license must remain visible; do not bypass Unity validation with a cosmetic or non-Unity replacement.

## Configure the self-hosted runner

Use the trusted Ubuntu machine controlled by the repository owner. Do not expose this runner to arbitrary pull-request code.

1. Open the repository on GitHub.
2. Go to **Settings → Actions → Runners**.
3. Choose **New self-hosted runner** and follow GitHub's generated Linux installation commands.
4. During runner configuration, add the custom label:

   ```text
   weachy-mini-android-device
   ```

5. Install Android platform tools so `adb` is available to the runner account.
6. Install Unity 6000.5.2f1 and its Android Build Support module.
7. Configure USB permissions for the LG G6.
8. Connect the phone and approve its ADB authorization prompt.
9. Verify from the runner account:

   ```bash
   adb devices -l
   adb -s LGH87250967ab9 shell getprop ro.product.cpu.abi
   adb -s LGH87250967ab9 shell getprop ro.build.version.sdk
   ```

   The expected values are `arm64-v8a` and `26`.

10. Start the runner with the Android SDK available:

   ```bash
   cd /home/phil/actions-runner-weachy-mini
   export ANDROID_SDK_ROOT=/home/phil/Android/Sdk
   export ANDROID_HOME=/home/phil/Android/Sdk
   export PATH="$ANDROID_SDK_ROOT/platform-tools:$PATH"
   ./run.sh
   ```

The runner may remain online while trusted Ralph-loop commits are being validated. Take it offline before accepting or running untrusted code.

## Configure Unity CI secrets

Open **Settings → Secrets and variables → Actions** and add the Unity values required by the selected Unity license type:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`
- `UNITY_SERIAL` when required by the license type

Do not commit license data or credentials to the repository. Run **Unity Validation** manually after the secrets are configured.

## Running the workflows

### Automatic local Unity validation

A trusted push to `master` queues **Local Unity Android Validation** on `kawa`. Older queued runs are cancelled when a newer push arrives.

### Manual Unity validation and phone installation

Open **Actions → Local Unity Android Validation → Run workflow**. Enable `install_physical_device` to install and launch the ARM64/API-26 feasibility APK on the LG G6.

### Hosted MuJoCo ARM64 build only

Open **Actions → Android MuJoCo Feasibility → Run workflow** and leave `run_device_probe` disabled.

### Hosted build followed by physical-phone probe

Open **Actions → Android MuJoCo Feasibility → Run workflow** and enable `run_device_probe`. The hosted build must complete first; the device job then waits for the online `kawa` runner and selects the physical ARM64 phone by serial even when an emulator is also online.

## Acceptance boundaries

A successful hosted ARM64 cross-build can satisfy the build-related portion of RMA-020, but it does not prove physical-device loading or runtime timing.

RMA-021 and RMA-022 remain incomplete until real-device and Unity jobs produce evidence for:

- ARM64 library loading on the LG G6 or another documented physical phone;
- structured malformed-model failure;
- the complete 900,000-step run and timing report;
- Unity IL2CPP ARM64 APK installation and native symbol resolution;
- application pause/resume behavior without catch-up simulation;
- controlled native initialization failure;
- deterministic destruction and shutdown.
