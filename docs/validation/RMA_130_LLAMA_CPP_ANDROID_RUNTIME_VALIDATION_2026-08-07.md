# RMA-130 llama.cpp Android runtime validation

**Status:** Implementation accepted on `11233d2967d9864f35f1684da13018110196f682`; final evidence SHA validation pending  
**Date:** 2026-08-07

RMA-130 introduces the model-independent llama.cpp native runtime boundary required before local
model manifests, installation, benchmarking, and provider integration.

## Accepted implementation contract

The accepted implementation preserves all of the following:

1. llama.cpp is pinned to release `b10313`, commit
   `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`, and the checkout/license are verified rather than
   trusting a moving branch.
2. No llama.cpp source checkout or GGUF/model binary is committed to the repository.
3. Android builds produce one ARM64 `libreachy_llama.so` with statically linked upstream runtime
   and only `reachy_llama_*` public exports.
4. The initial CPU baseline is `armv8-a`, with native host tuning, OpenMP, llamafile, RPC, OpenSSL,
   and dynamic ggml backend loading disabled.
5. API 26 compatibility is tested by the actual NDK linker. No compatibility shim or automatic
   platform-floor increase is used.
6. ABI version 1 includes load, tokenize, chat-template application, asynchronous generation
   start/stream polling, cancel, unload, and metrics.
7. There is no synchronous generate/wait function that can execute inference on the simulation
   thread.
8. Stream backpressure is bounded and lossless for already-enqueued fragments; queue overflow is
   not handled by dropping or overwriting output.
9. Cancellation wakes queue backpressure and is wired into llama.cpp's CPU decode abort callback.
10. Active generation release and model unload return `BUSY` rather than blocking or implicitly
    cancelling work.
11. Missing/invalid models, context overflow, unsupported encoder models, decode failures, and
    allocation failures are structured and visible.
12. No network, download, API-key, cloud-provider, automatic retry, or provider/model fallback is
    present.
13. The standard Unity Android staging path packages `libreachy_llama.so` without packaging a
    model.

## Source and build identity

- llama.cpp release: `b10313`
- llama.cpp commit: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`
- upstream license: MIT
- pinned `LICENSE` Git blob: `e7dca554bcb802f98408383a864404e3aa4eacca`
- Android ABI: `arm64-v8a`
- Android native compatibility floor exercised: API 26
- Android NDK: `28.2.13676358`
- CMake: `3.31.6`
- C++ runtime: `c++_static`
- CPU baseline: `armv8-a`
- selected/bundled language model: none

The source checkout is fetched at the exact commit and verified by
`scripts/verify_source_checkout.py`. The Android build also verifies the exact upstream license
blob before compiling. RMA-130 does not trust a moving llama.cpp branch or substitute an upstream
prebuilt binary for the source build.

## Exact implementation-SHA evidence

The accepted implementation SHA is `11233d2967d9864f35f1684da13018110196f682`.

### Dedicated RMA-130 gate

Workflow run `31203427475`, job `92948655063`, completed successfully on the exact implementation
SHA. It passed:

- exact source pin and static contract verification;
- a strict first-party host build against the real pinned llama.cpp source;
- the always-active native contract executable in Release configuration;
- first-party ASan/UBSan allocation, ownership, bounded-queue, and cancellation stress;
- installation and verification of the pinned Android NDK/CMake;
- Android ARM64 cross-compilation against API 26;
- ELF dependency inspection rejecting dynamic llama/ggml/`libc++_shared` dependencies;
- ELF export inspection requiring the first-party versioned `reachy_llama_*` ABI and rejecting
  leaked upstream symbols;
- evidence-boundary checks and artifact upload.

Artifact `9003903370`, named
`rma130-llama-android-11233d2967d9864f35f1684da13018110196f682`, has digest
`sha256:77ac8e0ebd8422a987a8c005861aeff379aeda97385594d6afdb6fc7bc81ff7c`.
The evidence bundle contains `libreachy_llama.so`, the pinned upstream license, build identity,
SHA-256, dynamic-dependency inspection, exported-symbol inspection, and imported-symbol
inspection.

### Hosted repository CI

Hosted CI run `31203427454` completed successfully on the same implementation SHA. All normal
repository jobs passed:

- static policy, actionlint, Ruff lint/format, and ShellCheck;
- native warnings-as-errors and sanitizer tests;
- managed warnings-as-errors and native lifecycle tests;
- Android lint, Java warnings, compilation, and tests;
- pinned Reachy-model verification, conversion, MuJoCo compile/step, and reference generation.

No RMA-130 exemption, warning suppression, lint baseline, or relaxed repository gate was added.

### Self-hosted Unity/API-26 packaging evidence

Self-hosted run `31203426565`, job `92948641999`, independently passed the RMA-130-relevant
integration boundary on the same implementation SHA:

- generated Reachy Unity presentation preparation;
- production native-runtime staging, including an independent exact-source API-26 ARM64 build of
  `libreachy_llama.so` beside the existing MuJoCo/Reachy libraries;
- Unity tests after staging the new native runtime;
- full ARM64/API-26 device APK build;
- ARM64/API-26 APK verification;
- physical-device pinning;
- RMA-090 camera-discovery acceptance;
- RMA-091 camera-acquisition acceptance.

At the time this evidence record was prepared, that broader legacy run had continued into the
unrelated downstream physical camera-texture/perception/lifecycle regression sequence. RMA-130
does not reclassify any later unrelated physical result as LLM-runtime evidence. The required
native staging and APK packaging boundary had already passed before those downstream gates.

## Ralph-loop corrections

The first candidate run exposed two test/build-harness defects rather than a llama.cpp runtime
failure:

1. `assert(...)`-based native test expressions disappeared in Release because of `NDEBUG`, leaving
   variables unused under warnings-as-errors. The tests were changed to an always-active
   `Require`/`FailTest` contract mechanism.
2. Weachy's first-party `-Wshadow -Werror` policy was initially applied to declarations inside
   upstream ggml headers. The exact upstream CMake subtree is now imported as `SYSTEM`; Weachy's
   wrapper and tests remain under the full strict first-party warning policy.

A separate broad-CI pass also found only Ruff formatting drift in the new Python contract file;
that formatting was corrected without changing runtime behavior.

No analyzer/warning suppression was added to first-party code, and no API floor, CPU baseline,
streaming semantics, fallback policy, or cancellation behavior was weakened to obtain the green
implementation SHA.

## Simulation-thread and failure semantics

The native API makes inference isolation structural rather than advisory:

- `generation_start` validates/claims ownership and starts a worker;
- context creation, prompt prefill, sampling, token rendering, and decode run only on that worker;
- `generation_poll` is nonblocking and only returns already-buffered or terminal events;
- `generation_cancel` signals the worker and wakes bounded backpressure;
- `generation_release` refuses an active job with `BUSY` instead of waiting;
- model unload refuses active generation ownership with `BUSY`;
- one generation per loaded model is explicit and a second request returns `BUSY`, with no hidden
  queue or preemption.

The bounded stream queue never silently drops already-produced text. An undersized consumer buffer
returns `BUFFER_TOO_SMALL` without consuming the queued fragment. Cancellation wakes a producer
blocked on queue capacity while preserving previously queued fragments for visible draining before
the terminal cancellation event.

## Model-selection boundary

RMA-130 intentionally has no positive language-generation quality acceptance because no model is
selected here. A model fixture is not required to prove the runtime's ownership, threading,
cancellation, ABI, Android linkability, or packaging boundaries. RMA-131 through RMA-135 own the
model manifest, safe installation, benchmark-based selection, managed provider, and resource/
thermal policy.

RMA-130 must not be used as evidence that any particular GGUF model is suitable, performant, or
safe for the target device. It establishes the pinned native execution boundary only.
