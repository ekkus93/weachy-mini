# RMA-130 llama.cpp Android runtime validation

**Status:** Candidate implementation; exact-SHA evidence pending  
**Date:** 2026-08-07

RMA-130 introduces the model-independent llama.cpp native runtime boundary required before local
model manifests, installation, benchmarking, and provider integration.

## Candidate contract

The candidate is acceptable only if all of the following remain true:

1. llama.cpp is pinned to release `b10313`, commit
   `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`, and the checkout/license are verified rather than
   trusting a moving branch.
2. No llama.cpp source checkout or GGUF/model binary is committed to the repository.
3. Android builds produce one ARM64 `libreachy_llama.so` with statically linked upstream runtime
   and only `reachy_llama_*` public exports.
4. The initial CPU baseline is `armv8-a`, with native host tuning, OpenMP, llamafile, RPC, OpenSSL,
   and dynamic ggml backend loading disabled.
5. API 26 compatibility is tested by the actual NDK linker. Failure must be visible; no shim or
   automatic platform-floor increase is permitted.
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

## Required automated evidence

Before the RMA-130 TODO is checked, the exact implementation SHA must pass:

- `.github/workflows/rma130-llama-android-runtime.yml`, including strict host compilation,
  model-free allocation/cancellation stress, sanitizer stress, exact source/license verification,
  Android ARM64 cross-build, and ELF export/dependency inspection;
- normal hosted repository CI, including actionlint, ShellCheck, Ruff/static policy, native,
  managed, Android, and pinned Reachy-model jobs;
- the self-hosted Unity/Android regression far enough to prove that the pinned RMA-130 runtime is
  built and included in the ARM64 application package. Any unrelated pre-existing physical gate
  failure must be recorded accurately rather than reclassified as RMA-130 success.

No warning suppression, sanitizer suppression, dropped stream event, hidden retry, fallback model,
network path, unpinned source, prebuilt opaque replacement, or relaxed export/dependency check may
be introduced merely to make these gates pass.

## Model-selection boundary

RMA-130 intentionally has no positive language-generation quality acceptance because no model is
selected here. A model fixture is not required to prove the runtime's ownership, threading,
cancellation, ABI, Android linkability, or packaging boundaries. RMA-131 through RMA-135 own the
model manifest, safe installation, benchmark-based selection, managed provider, and resource/
thermal policy.
