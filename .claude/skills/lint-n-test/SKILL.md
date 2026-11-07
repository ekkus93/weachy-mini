---
name: lint-n-test
description: Lint the codebase (ruff, shellcheck, actionlint) and run all test suites (Python, native ctest, managed dotnet, Android Gradle). Use only when the user explicitly invokes /lint-n-test.
model: haiku
---

# Lint and Test

Run the repository's lint tools and full test suites, in order, from the repo root. Report pass/fail for each step with its actual output — don't just say "it failed" or "it passed".

## Environment setup

The toolchain may not be on the default non-interactive shell PATH (`dotnet` in `~/.dotnet`, Go tools in `~/go/bin`, uv/pip tools such as ruff, cmake, shellcheck, actionlint and gradle in `~/.local/bin`). Prefix every bash step below with:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/go/bin:$HOME/.local/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export JAVA_HOME="$(echo "$HOME"/.jdks/jdk-17*)"   # JDK 17 per toolchain.lock.json; Android Studio's bundled JBR is 25, don't use it
export ANDROID_HOME="$HOME/Android/Sdk" ANDROID_SDK_ROOT="$HOME/Android/Sdk"
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358"
```

Don't use `$TMPDIR` for logs (it is unset here); write logs under the session scratchpad directory.

## Lint

```bash
ruff check scripts
ruff format --check --diff scripts
shellcheck scripts/*.sh
command -v actionlint >/dev/null && actionlint || echo "actionlint not installed, skipping"
```

## Test

```bash
# Python tooling tests
python3 -m unittest discover -s scripts/tests -v

# Native C/C++ build + ctest
./scripts/build_native.sh
```

Managed .NET tests are **not** xUnit/NUnit projects — they're standalone console runners (`OutputType: Exe`). `dotnet test` silently reports zero tests and exits 0 without actually running anything; always use `dotnet run`, matching how CI invokes them (see `scripts/ci.sh` and `.github/workflows/ci.yml`):

Run **every** `managed/*Tests` project (there are ~19; don't hardcode a list, since projects get added). Set the native-interop variables first so `ReachyMini.Core.Tests` also exercises the native lifecycle tests against the library `build_native.sh` just produced (matching CI's dedicated `managed` job); the other projects ignore them. Stop at the first non-zero exit:

```bash
export REACHY_MANAGED_NATIVE_LIBRARY_DIR="$(pwd)/build/native/native/reachy_sim"
export LD_LIBRARY_PATH="${REACHY_MANAGED_NATIVE_LIBRARY_DIR}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
export REACHY_MANAGED_NATIVE_TESTS=1
for d in managed/*Tests; do
  proj=$(basename "$d")
  dotnet restore "$d/$proj.csproj" && dotnet run --project "$d/$proj.csproj" --configuration Release --no-restore
  rc=$?; echo "$proj rc=$rc"; [ "$rc" -ne 0 ] && break
done
```

Success is often silent (exit 0, little or no output) — check the exit code, don't assume output means failure.

If the Android Gradle wrapper has been generated (`android-plugin/gradlew` exists), also run it with the JDK 17 / `ANDROID_HOME` exported above (no `local.properties`; it must never be committed):

```bash
cd android-plugin
./gradlew --no-daemon lint test
```

Note that `android-plugin` currently has no unit-test sources (`testDebugUnitTest NO-SOURCE`), so a pass means lint and compilation only; say so in the summary.

If `gradlew` is missing, skip it and note that the wrapper hasn't been generated (`gradle wrapper --gradle-version 9.5.0 --distribution-type bin`), rather than generating it yourself. After a manual generation, `git checkout -- android-plugin/gradle/wrapper/gradle-wrapper.properties` drops the generator's extra `retries` lines; `gradlew`, `gradlew.bat` and the jar stay uncommitted.

## Rules

- Do not use warning suppressions, lint baselines, or pragma-disables to force a pass — per `docs/WARNING_POLICY.md`, warnings are bugs and must be fixed, not silenced.
- Never modify third-party code to satisfy first-party lint/test checks.
- Stop and surface the failure as soon as one is found; don't silently continue past a broken step.

## Summary

End with a short table or list: each step, pass/fail, and one line of detail for any failure.
