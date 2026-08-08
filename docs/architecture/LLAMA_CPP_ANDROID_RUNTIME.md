# RMA-130 / RMA-133 llama.cpp Android native runtime

**RMA:** 130 runtime foundation; RMA-133 ABI-2 constrained-generation extension  
**Scope:** native local inference runtime only; model installation/selection/provider/resource policy remain separate responsibilities

The accepted RMA-130 ABI-1 architecture is preserved verbatim in
`docs/architecture/LLAMA_CPP_ANDROID_RUNTIME_ABI1.md`. This document describes the current
runtime after the deliberate RMA-133 ABI-2 extension.

## Source and license boundary

The runtime still pins llama.cpp release `b10313` at commit
`dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`. The source checkout is not vendored. Every
validated build fetches that exact revision and passes it through
`scripts/verify_source_checkout.py`; a mismatched or dirty checkout fails the build.

The upstream license is MIT. The source lock records exact repository/revision/license identity,
and hosted/Android build paths verify the pinned checkout before compilation.

Product model identity and metadata belong to RMA-131, validated installation to RMA-132,
benchmark/selection to RMA-133, provider integration to RMA-134, and device resource/thermal
policy to RMA-135. The tiny GGUF used by hosted constrained-generation lifecycle tests is
explicitly test-only, revision/size/SHA-256 pinned in
`third_party/llama-cpp-test-model.lock.json`, downloaded only by the hosted test helper, and is
not a product model or manifest.

## Android build contract

The first-party product remains one shared library: `libreachy_llama.so`.

Pinned llama.cpp/ggml code is built as static position-independent code and linked into that
library. A linker version script exports only `reachy_llama_*`; upstream llama/ggml symbols
remain private. Android validation rejects prohibited dynamic `libllama`, `libggml`, or
`libc++_shared` dependencies and unexpected exports.

The compatibility build remains:

- Android ABI `arm64-v8a` only;
- NDK `28.2.13676358` and CMake `3.31.6` from the repository toolchain lock;
- `c++_static`;
- Android API 26 for the physical-feasibility packaging gate;
- CPU baseline `armv8-a`;
- `GGML_NATIVE=OFF`, `GGML_OPENMP=OFF`, and `GGML_LLAMAFILE=OFF`;
- dynamic ggml backend loading disabled; and
- RPC and OpenSSL disabled.

API 26 remains a build/link compatibility experiment rather than a promise of the final app
minimum SDK. The runtime does not silently raise the floor or introduce a compatibility shim.

## Deliberate ABI 2

`native/llama_runtime/include/reachy_llama.h` now declares
`REACHY_LLAMA_ABI_VERSION == 2`. ABI 2 retains the opaque model/generation handles and the
existing asynchronous API while adding explicit constrained generation. It does **not** silently
reinterpret ABI-1 constraint data as ABI 2. Historical ABI-1 source/evidence remains preserved in
`native/llama_runtime/src/reachy_llama_abi1_base.inc` and the ABI-1 architecture snapshot.

The current ABI includes:

- model load/unload and model metrics;
- tokenization with query-capacity semantics;
- chat-template application;
- unconstrained asynchronous generation start for later callers that explicitly choose it;
- `reachy_llama_generation_start_constrained` for explicit constrained generation;
- nonblocking stream polling;
- cancellation;
- generation metrics; and
- terminal generation release.

There is deliberately no synchronous `generate` or `wait` API.

## Constraint contract and ownership

ABI 2 adds `reachy_llama_constraint_type` and
`reachy_llama_generation_constraint`. RMA-133 V6 uses `REACHY_LLAMA_CONSTRAINT_GBNF` only.
The constraint struct contains `struct_size`, `abi_version`, type/reserved fields, an explicit
UTF-8 grammar pointer/byte length, and an explicit UTF-8 root pointer/byte length.

Validation is fail-closed before a worker is launched:

- struct size and ABI must match ABI 2;
- type must be the requested supported constraint type and reserved fields must be zero;
- grammar/root pointers and lengths must be present and bounded;
- grammar is bounded to 256 KiB and the root name to 128 bytes;
- embedded NULs and invalid UTF-8 are rejected; and
- root syntax is validated before ownership transfer.

The runtime deep-copies grammar and root bytes before returning success from constrained start.
The asynchronous worker therefore never retains caller-owned grammar pointers. Hosted loaded-model
tests start generation from mutable caller strings and overwrite those strings immediately; the
result must still follow the original copied grammar.

## Exact pinned llama.cpp grammar integration

The RMA-133 implementation was checked against the exact pinned llama.cpp commit above. It uses
these upstream sampler interfaces from that revision:

- `llama_sampler_chain_init`;
- `llama_sampler_chain_add`;
- `llama_sampler_init_grammar(vocab, grammar_str, grammar_root)`;
- `llama_sampler_sample`; and
- `llama_sampler_free` through sampler-chain ownership.

For a constrained job, the wrapper captures the loaded model vocabulary and inserts the grammar
sampler into the sampler chain **before** the historical greedy/min-p/temperature/distribution
samplers are added. Consequently the grammar constrains the first generated token as well as later
tokens. The pinned `llama_sampler_sample` path performs sampler acceptance/state advancement for
the selected token.

The accepted ABI-1 generation worker is preserved as historical source and reused through a
narrow internal shim. A thread-local active constraint exists only on the constrained worker. An
unconstrained start has no active constraint and retains the historical path.

The historical ABI-1 status switch predates ABI-2 status values. Rather than modifying its
preserved bytes, the current translation unit suppresses `-Wswitch` only around inclusion of that
historical source. First-party ABI-2 code remains under the normal warnings-as-errors policy and
maps the new statuses explicitly.

## Failure policy: never fall back

Constraint validation and grammar initialization have explicit statuses:

- `REACHY_LLAMA_STATUS_INVALID_CONSTRAINT` (`15`); and
- `REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED` (`16`).

If `llama_sampler_init_grammar` rejects the GBNF, the generation terminates with status 16.
The wrapper does not treat the failure as EOS, empty success, parser-repair input, or permission to
restart unconstrained. The terminal error text explicitly records that unconstrained generation
was not attempted.

RMA-133 also keeps the independent strict response validator after grammar-constrained generation.
A grammar is an output-generation guard, not a reason to accept malformed bytes after the fact.
There is no Markdown stripping, JSON repair, parser recovery, hidden retry, hidden model switch,
or provider fallback.

## Simulation-thread isolation and backpressure

Constrained generation preserves the existing asynchronous model:

- start validates/claims the model generation slot and launches a worker;
- context creation, prefill, sampling, token rendering, and decode occur on the worker;
- poll returns only already-buffered text, a terminal event, or `NONE`;
- cancel sets the atomic cancellation flag and wakes bounded queue backpressure; and
- release refuses a still-running job with `BUSY` rather than blocking invisibly.

Each generation retains the bounded FIFO stream queue. No generated fragments are silently
dropped, overwritten, coalesced, or drained. One loaded model still permits one active generation;
a concurrent start returns `BUSY` rather than entering a hidden request queue.

## Context, sampling, privacy, and provider boundary

Prompt plus requested output must fit the explicit context; the wrapper returns `CONTEXT_LIMIT`
rather than silently truncating. Temperature `<= 0` uses greedy sampling. Positive temperature
uses the historical minimal `min_p -> temperature -> distribution` sampling policy, with the
grammar sampler additionally active only for constrained jobs.

The runtime suppresses upstream global logging and returns bounded first-party errors. It does not
persist prompts, generated text, model paths, tokens, or logs. It contains no socket, HTTP,
download, cloud-provider, API-key, or provider-fallback path. Model acquisition is outside the
runtime.

## Validation

Permanent hosted validation for the current ABI includes:

1. exact llama.cpp source verification;
2. strict first-party native build and historical bounded-queue/lifecycle contracts;
3. ABI-2 constraint struct/type/bounds/UTF-8/NUL/root validation;
4. ASan/UBSan runs of first-party contracts;
5. a revision/size/SHA-256 pinned tiny GGUF used only for hosted lifecycle tests;
6. real loaded-model proof that caller grammar/root buffers are deep-copied;
7. exact constrained-byte tests that cannot begin with Markdown fences or `<think>`;
8. malformed-grammar proof of explicit status 16 with zero partial/unconstrained output;
9. constrained cancellation under queue backpressure followed by release, model reuse, and unload;
10. ARM64 Android cross-compilation against the exact pinned NDK/CMake/API floor;
11. ELF dependency/export inspection, including the ABI-2 constrained-start symbol; and
12. build/evidence hashing.

RMA-133's physical benchmark then adds a device-side malformed-grammar negative control and the
frozen V6 grammar/candidate evaluation. A selected product model is not created or promoted by
this runtime layer; selection remains conditional on the full RMA-133 physical gates.
