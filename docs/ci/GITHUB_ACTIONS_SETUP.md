# GitHub Actions CI setup

This project uses GitHub Actions for hosted static analysis, Android lint/tests, native tests, Android ARM64 MuJoCo cross-compilation, and optional Unity validation. A physical-phone probe uses a separately labeled self-hosted runner because a GitHub-hosted virtual machine cannot access a USB phone attached to a developer machine.

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

### `.github/workflows/android-feasibility.yml`

The hosted `build-arm64` job runs automatically when the MuJoCo build/probe inputs change and can also be started manually.

It:

1. reads Android and MuJoCo versions from the repository lock files;
2. installs the exact Android SDK, NDK, and CMake packages through `sdkmanager`;
3. checks out the exact pinned MuJoCo commit;
4. validates that the checkout is clean and matches the lock file;
5. cross-compiles MuJoCo and the first-party probe for `arm64-v8a`;
6. verifies the produced ELF architecture, dynamic dependencies, symbols, and provenance;
7. uploads the staged library, runner, fixtures, and reports as a GitHub Actions artifact.

The optional `device-probe` job downloads that exact artifact and runs the 900,000-step probe on one physical ARM64 phone.

### `.github/workflows/unity-validation.yml`

This is intentionally manual. It runs:

- Unity edit-mode and play-mode tests;
- the development APK build entry point;
- the release AAB build entry point.

It requires valid Unity activation secrets. A failed or absent Unity license must remain visible; do not bypass Unity validation with a cosmetic or non-Unity replacement.

## Configure the physical Android device runner

Use a trusted Ubuntu machine controlled by the repository owner. Do not expose this runner to arbitrary pull-request code.

1. Open the repository on GitHub.
2. Go to **Settings → Actions → Runners**.
3. Choose **New self-hosted runner** and follow GitHub's generated Linux installation commands.
4. During runner configuration, add the custom label:

   ```text
   weachy-mini-android-device
   ```

5. Install Android platform tools so `adb` is available to the runner account.
6. Configure USB permissions for the phone.
7. Connect exactly one ARM64 Android phone and approve its ADB authorization prompt.
8. Verify from the runner account:

   ```bash
   adb devices
   adb shell getprop ro.product.cpu.abi
   ```

   The second command must report `arm64-v8a`.

9. Keep the runner disabled or offline when it is not being used for trusted manual device validation.

The physical-device job is available only through `workflow_dispatch` with `run_device_probe` selected. It is not triggered by pull requests.

## Configure Unity CI secrets

Open **Settings → Secrets and variables → Actions** and add the Unity values required by the selected Unity license type:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`
- `UNITY_SERIAL` when required by the license type

Do not commit license data or credentials to the repository. Run **Unity Validation** manually after the secrets are configured.

## Running the workflows

### Hosted MuJoCo ARM64 build only

Open **Actions → Android MuJoCo Feasibility → Run workflow** and leave `run_device_probe` disabled.

### Hosted build followed by physical-phone probe

Open **Actions → Android MuJoCo Feasibility → Run workflow** and enable `run_device_probe`. The hosted build must complete first; the device job then waits for a matching online self-hosted runner.

### Unity validation

Open **Actions → Unity Validation → Run workflow** after adding the Unity secrets.

## Acceptance boundaries

A successful hosted ARM64 cross-build can satisfy the build-related portion of RMA-020, but it does not prove physical-device loading or runtime timing.

RMA-021 and RMA-022 remain incomplete until the real device and Unity jobs produce evidence for:

- ARM64 library loading on a physical phone;
- structured malformed-model failure;
- the complete 900,000-step run and timing report;
- application pause/resume behavior without catch-up simulation;
- controlled native initialization failure;
- deterministic destruction and shutdown;
- Unity IL2CPP Android build and native symbol resolution.
