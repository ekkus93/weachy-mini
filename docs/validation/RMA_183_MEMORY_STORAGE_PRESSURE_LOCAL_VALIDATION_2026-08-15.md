# RMA-183 local validation — 2026-08-15

## Validation target

RMA-183 must make memory and storage pressure explicit without corrupting active inference, discarding a valid resumable download, weakening durable-state ownership, or introducing silent fallback.

## Focused static contract

`scripts/tests/test_rma183_memory_storage_pressure.py` verifies:

- the single `Application.lowMemory` subscription/unsubscription path;
- camera texture-cache release and Unity unused-asset reclamation;
- local-LLM registration, idle unload, active-generation retention, and disposal cleanup;
- periodic model-download storage rechecks plus resumable-partial retention;
- diagnostic export preflight and explicit insufficient-storage handling;
- narrowly scoped cleanup UI wiring and durable-state preservation wording;
- managed contract registration; and
- closure of all four RMA-183 roadmap items.

## Managed contracts

The source tree adds managed contracts for the portions that do not require Unity:

- `Rma183MemoryStoragePressureContractTests` exercises the memory-pressure registry and proves a low-storage diagnostic export fails before it creates the output directory or final bundle.
- the package-manager copy primitive rechecks storage every 4 MiB and maps confirmed write-side storage exhaustion to the existing typed `InsufficientStorage` result; the unchanged download catch path retains its manifest-bound partial and metadata for exact resume.
- `ReachyMini.LocalLlm.Tests` proves an idle model is unloaded and can be explicitly reloaded, while active generation and active reload transitions are retained without cancellation or duplicate native unload.

## Local execution limits

This sandbox does not provide the .NET SDK or Unity Editor, so managed/Unity compilation cannot be executed locally. The focused Python contract and repository static checks are run locally instead. The user is monitoring GitHub Actions separately; no CI polling is part of this Ralph loop.

## Physical-device claim

No physical Android low-memory or disk-full result is fabricated here. `Application.lowMemory` is wired to the production application host, but OEM/device callback timing and actual storage-pressure behavior require later representative-device execution. RMA-184 remains the device-matrix owner.
