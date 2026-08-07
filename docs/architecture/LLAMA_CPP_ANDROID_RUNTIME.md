# RMA-130 llama.cpp Android native runtime

**RMA:** 130  
**Scope:** native runtime only; model manifest/download/selection and the managed LLM provider are later tasks

## Source and license boundary

RMA-130 pins llama.cpp release `b10313` at commit
`dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`. The source checkout is not vendored. Every
validated build fetches that exact revision and passes it through
`scripts/verify_source_checkout.py`; a mismatched or dirty checkout fails the build.

The upstream license is MIT. The lock records the exact upstream `LICENSE` Git blob
`e7dca554bcb802f98408383a864404e3aa4eacca`, and both hosted and Android build paths verify
that blob before compilation. The Android evidence bundle includes the pinned license text.

No GGUF or other language-model binary is introduced by RMA-130. Model identity and metadata
belong to RMA-131, validated installation to RMA-132, benchmark/selection to RMA-133, provider
integration to RMA-134, and device resource/thermal policy to RMA-135.

## Android build contract

The first-party product is one shared library: `libreachy_llama.so`.

The pinned llama.cpp/ggml runtime is built as static position-independent code and linked into
that first-party library. A linker version script exports only `reachy_llama_*`; upstream
llama/ggml symbols remain private implementation details. The Android gate rejects dynamic
`libllama`, `libggml`, or `libc++_shared` dependencies and rejects unexpected exported symbols.

The initial compatibility build uses:

- Android ABI `arm64-v8a` only;
- repository-pinned NDK `28.2.13676358` and CMake `3.31.6`;
- `c++_static`;
- Android API 26 for the existing physical-feasibility packaging gate;
- CPU baseline `armv8-a`;
- `GGML_NATIVE=OFF`;
- `GGML_OPENMP=OFF`;
- `GGML_LLAMAFILE=OFF`;
- dynamic ggml backend loading disabled;
- RPC and OpenSSL disabled.

API 26 is intentionally a build/link compatibility experiment, not a claim about the final app
minimum SDK. If pinned llama.cpp requires a newer Android native API, the link must fail visibly.
RMA-130 does not add a compatibility shim or silently raise the target floor.

The `armv8-a` CPU baseline is conservative by design. RMA-130 establishes correctness and broad
ARM64 loadability before RMA-133/RMA-135 benchmark device-specific dot-product, i8mm, SME, batch,
thread, and context choices.

## Versioned C ABI

`native/llama_runtime/include/reachy_llama.h` defines ABI version 1. Consumers see opaque 64-bit
model and generation handles plus fixed-layout configuration, event, error, and metrics structs.
The ABI includes:

- model load/unload and model metrics;
- tokenization with query-capacity semantics;
- model/default or caller-supplied chat-template application;
- asynchronous generation start;
- nonblocking stream polling;
- cancellation;
- generation metrics;
- terminal generation release.

There is deliberately no synchronous `generate` or `wait` function.

The model load path forces CPU execution. It does not download, discover, select, retry, or switch
to another model/provider. A missing/invalid local file returns a structured failure with a zero
handle.

## Simulation-thread isolation

`reachy_llama_generation_start` validates configuration, claims the model's single generation
slot, creates a worker, and returns. All llama context creation, prompt prefill, sampling, token
rendering, and decoding execute on that worker.

`reachy_llama_generation_poll` never waits for inference or for a new token. It returns either the
next already-buffered text fragment, a terminal event, or `NONE`.

`reachy_llama_generation_cancel` sets an atomic cancellation flag and wakes bounded stream
backpressure. The same flag is installed as llama.cpp's CPU `abort_callback`, allowing an active
`llama_decode` to terminate rather than waiting for an entire inference request.

`reachy_llama_generation_release` refuses a still-running job with `BUSY`. It never turns a caller
mistake into a hidden blocking join. Model unload likewise returns `BUSY` while a generation owns
the model.

These API constraints allow the later managed provider to keep Unity/MuJoCo simulation work off
the inference worker without relying on caller discipline around a synchronous native function.

## Streaming and backpressure

Each generation owns a bounded FIFO text queue. The default capacity is 64 fragments and the ABI
allows a bounded configured capacity up to 4096.

When the consumer is slower than generation, the inference worker waits for queue capacity. It
does not drop, overwrite, coalesce, or silently discard already-produced fragments. Polling with
an undersized output buffer returns `BUFFER_TOO_SMALL` without consuming the fragment.

Cancellation wakes a producer blocked on a full queue. Fragments already in the queue remain
available to the consumer; no silent drain occurs. After queued fragments have been consumed, the
terminal cancellation/error/completion event becomes visible.

RMA-130 permits one active generation per loaded model. A concurrent start returns `BUSY`; there
is no hidden request queue or preemption policy.

## Context and generation behavior

A generation owns a fresh llama context and sampler. The runtime rejects encoder models because
this ABI implements decoder-only text generation. Prompt plus requested output must fit the
explicit configured context; the wrapper returns `CONTEXT_LIMIT` rather than truncating the
prompt or output budget silently.

Prefill is submitted in bounded batches. Temperature `<= 0` uses greedy sampling. Positive
temperature uses a minimal `min_p -> temperature -> distribution` chain. Sampling policy remains
configuration rather than model-selection policy.

Metrics expose model tensor bytes/parameter count and generation prompt/generated token counts,
queue depth, state, timestamps, context, batch, and thread settings. The wrapper intentionally
uses its own monotonic timings rather than llama.cpp's example/tool performance helpers.

## Error and privacy boundary

The wrapper suppresses upstream global logging and returns bounded first-party error categories.
It does not persist prompts, generated text, model paths, tokens, or logs. Error strings do not
echo the caller's model path or prompt.

RMA-130 contains no socket, HTTP, download, cloud-provider, API-key, or provider-fallback path.
The native runtime can only operate on a local model path supplied by its caller. RMA-132 must
validate allowable installed paths before RMA-134 exposes model loading to application behavior.

## Validation

The permanent RMA-130 workflow performs:

1. exact source and license-blob verification;
2. strict first-party host build and native contracts;
3. ASan/UBSan first-party stress contracts;
4. repeated invalid model-load allocation/lifecycle checks without a model fixture;
5. bounded FIFO allocation/order stress;
6. blocked-producer cancellation stress proving cancellation wakes backpressure without draining
   retained fragments;
7. ARM64 Android cross-compilation with the pinned NDK/CMake and API-26 link floor;
8. ELF dependency and export inspection;
9. evidence generation including runtime SHA-256 and build configuration.

The normal self-hosted Unity Android staging path also builds the same pinned runtime and stages
`libreachy_llama.so` beside MuJoCo/Reachy native libraries. That proves the plug-in is packaged by
the existing ARM64 application path without requiring RMA-130 to select a language model.
