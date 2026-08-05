# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Weachy Mini is a free, noncommercial Android app that runs a virtual Reachy Mini robot in Unity (MuJoCo is the authoritative dynamics engine). It is at the foundation/scaffold stage — not yet a working robot simulator. Polyglot repo: C/C++ (native sim), C#/Unity, Java/Kotlin (Android plugin), Python (tooling/CI scripts), Bash.

Authoritative docs: `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md` (spec), `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` (ordered TODO), `docs/BUILDING.md`, `docs/ci/GITHUB_ACTIONS_SETUP.md`, `docs/ASSET_POLICY.md`, `docs/WARNING_POLICY.md`.

## Build / test / lint

- `./scripts/ci.sh --static-only` — fast sanity check, no compilers/SDKs needed (toolchain manifest, scaffold, doc links, inventory, ruff, `python3 -m unittest discover -s scripts/tests`).
- `./scripts/ci.sh` (or `./scripts/ci.sh all`) — full local CI: static checks + native build/ctest + `dotnet` managed tests. **Use this as the default check before considering work done** — the local environment has the required toolchains.
- `./scripts/build_native.sh` — native desktop build + ctest. `REACHY_ENABLE_SANITIZERS=ON ./scripts/build_native.sh` for an ASan/UBSan build.
- `python3 scripts/verify_toolchain.py [--manifest-only]` — checks installed toolchain versions against `toolchain.lock.json`.
- `./scripts/build_unity_android.sh {development|release|device-feasibility}` — requires `UNITY_EDITOR` set to the pinned Unity executable (`toolchain.lock.json` → `unity.editor_version`, currently `6000.5.2f1`).
- `ANDROID_NDK_HOME=... MUJOCO_SOURCE_DIR=... ./scripts/build_mujoco_android.sh` — MuJoCo Android cross-compile.
- `REACHY_ANDROID_SERIAL=<serial> ./scripts/run_mujoco_probe_android.sh` — perf probe on the physical test phone.
- Android plugin: the Gradle wrapper JAR is **not committed**. First run `cd android-plugin && gradle wrapper --gradle-version 9.5.0 --distribution-type bin`, then `./gradlew lint test`.
- Lint tools: `ruff check scripts` / `ruff format --check --diff scripts` (Python), `shellcheck scripts/*.sh`, `actionlint` (`.github/workflows/*.yml`).

## Code style

- Ruff (`pyproject.toml`): line-length **100** (not 88), double quotes, LF endings, target py311, rule set `B,C4,E,F,I,RUF,SIM,UP`.
- C#/.NET (`.editorconfig`): block-scoped namespaces required (`csharp_style_namespace_declarations = block_scoped:error`, not file-scoped); `var` is discouraged everywhere (`csharp_style_var_*` all `false:warning`); no `this.` qualification; unused usings are an error (`IDE0005 = error`).
- No `.clang-format` exists — native C/C++ style is enforced only via strict compiler warnings (`native/cmake/CompilerWarnings.cmake`), not formatting.
- **Warnings-as-errors is a hard project-wide policy** (`docs/WARNING_POLICY.md`): no warning-disable pragmas, suppressions, or lint baselines to make checks pass. Never modify third-party code to satisfy first-party lint rules, and never let first-party warning flags leak into third-party builds.

## Git / commit conventions

- History lands directly on `master` — no feature branches or PRs in this repo. Commit messages are prefixed with the ticket number: `RMA-<n>: <imperative summary>` (e.g. `RMA-102: implement GPU homography warp`). Each ticket typically has a matching permanent CI gate workflow at `.github/workflows/rma<n>-<slug>.yml`.

## Gotchas

- Never commit: API keys/tokens, local SDK paths, `local.properties`, model binaries (GGUF/safetensors/ONNX/TFLite), raw camera frames/mic recordings/transcripts/diagnostics, raw calibration captures, or IDE/build caches (Unity `Library`, Gradle, CMake) — see `docs/ASSET_POLICY.md`.
- MuJoCo and Reachy-Mini sources are pinned via `third_party/*-source.lock.json`, not vendored — a missing/mismatched pin must fail visibly, never fall back silently.
- Git LFS is configured for `*.gguf, *.safetensors, *.onnx, *.tflite, *.parquet` but only for approved fixtures recorded in `third_party/inventory.json`.
- Android target is ARM64/IL2CPP only (no x86_64 in Unity 6000.5); min SDK 31 is explicitly provisional pending a device-compatibility spike. A separate API 26 floor applies only to the physical LG G6 test phone (`REACHY_ANDROID_SERIAL`), not the real app minimum.
- CI's physical-device jobs run on a self-hosted runner (label `weachy-mini-android-device`, nicknamed "kawa") since GitHub-hosted runners can't reach a USB phone.
