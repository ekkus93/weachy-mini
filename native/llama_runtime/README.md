# RMA-130 llama.cpp Android runtime

This directory contains only Weachy Mini's first-party wrapper, tests, and build definitions.
The llama.cpp source is not vendored. Builds must use the exact checkout pinned by
`third_party/llama-cpp-source.lock.json`, and `scripts/verify_source_checkout.py` rejects a
mismatched or dirty checkout.

The Android product is one first-party shared library, `libreachy_llama.so`, with a narrow
versioned C ABI from `include/reachy_llama.h`. Pinned llama.cpp and ggml are linked into that
library statically; upstream symbols are hidden from consumers.

No GGUF/model binary belongs in this directory or repository. RMA-131/RMA-132 own model
metadata and installation, RMA-133 owns model selection, and RMA-135 owns device-specific
resource/thermal tuning.
