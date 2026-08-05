---
name: lint-n-test
description: Lint the codebase (ruff, shellcheck, actionlint) and run all test suites (Python, native ctest, managed dotnet, Android Gradle). Use only when the user explicitly invokes /lint-n-test.
model: haiku
---

# Lint and Test

Run the repository's lint tools and full test suites, in order, from the repo root. Report pass/fail for each step with its actual output — don't just say "it failed" or "it passed".

## Environment setup

`dotnet` and `actionlint` may not be on the default non-interactive shell PATH even if installed (this repo's toolchain lives at `~/.dotnet` and `~/go/bin`). Prefix every bash step below with:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/go/bin:$PATH"
```

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

```bash
for proj in ReachyMini.Core.Tests ReachyMini.Application.Tests ReachyMini.Camera.Tests; do
  dotnet restore "managed/$proj/$proj.csproj"
  dotnet run --project "managed/$proj/$proj.csproj" --configuration Release --no-restore
done
```

Success is silent (exit 0, no output) for `ReachyMini.Core.Tests` unless it prints a named "... passed" line like the other two — either way, check the exit code, don't assume output means failure.

By default `ReachyMini.Core.Tests` only runs its in-process unit tests. To also exercise the native-interop lifecycle tests (matching CI's dedicated `managed` job), point it at the shared library `build_native.sh` just produced:

```bash
export REACHY_MANAGED_NATIVE_LIBRARY_DIR="$(pwd)/build/native/native/reachy_sim"
export LD_LIBRARY_PATH="${REACHY_MANAGED_NATIVE_LIBRARY_DIR}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
export REACHY_MANAGED_NATIVE_TESTS=1
dotnet run --project managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj --configuration Release --no-restore
```

If the Android Gradle wrapper has been generated (`android-plugin/gradlew` exists), also run it (needs JDK 17, per `toolchain.lock.json`):

```bash
cd android-plugin
JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 ./gradlew lint test
```

Otherwise skip it and note that the wrapper hasn't been generated (`gradle wrapper --gradle-version 9.5.0 --distribution-type bin`), rather than trying to generate it yourself.

## Rules

- Do not use warning suppressions, lint baselines, or pragma-disables to force a pass — per `docs/WARNING_POLICY.md`, warnings are bugs and must be fixed, not silenced.
- Never modify third-party code to satisfy first-party lint/test checks.
- Stop and surface the failure as soon as one is found; don't silently continue past a broken step.

## Summary

End with a short table or list: each step, pass/fail, and one line of detail for any failure.
