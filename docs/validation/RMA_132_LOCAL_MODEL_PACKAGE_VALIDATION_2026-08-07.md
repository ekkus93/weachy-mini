# RMA-132 safe local-model package validation

**Status:** Candidate implementation; exact-SHA evidence pending  
**Date:** 2026-08-07

## Candidate acceptance contract

RMA-132 is acceptable only if all of the following remain true:

1. Downloads/imports check free storage before writing model bytes.
2. Every acquisition writes under the dedicated managed staging tree; no external arbitrary path is
   promoted directly.
3. Resumable downloads are bound to the exact manifest size/hash and explicit source URI
   fingerprint.
4. A resume offset mismatch fails visibly; unsupported resume performs an explicit clean restart.
5. Imports are readable streams and are intentionally non-resumable.
6. Exact byte count and SHA-256 are required before same-store atomic publication.
7. Installed paths are derived from manifest identity/hash and are revalidated before a
   `LocalModelApprovedArtifact` is issued.
8. App-termination recovery keeps only valid resumable download state and removes abandoned import
   partials.
9. Delete and orphan cleanup are confined to a marker-owned store and never follow managed
   reparse-point directories.
10. No source/model/provider fallback, benchmark selection, or cloud inference is introduced.

## Required automated evidence

The exact candidate SHA must pass:

- `ReachyMini.Core` warnings-as-errors;
- `ReachyMini.LocalModelPackages.Tests`;
- deterministic static package-management contracts;
- Ruff lint/format, ShellCheck/actionlint, and normal hosted repository CI.

The managed suite uses only tiny synthetic bytes and temporary directories. It covers exact import,
wrong hash, low storage, fresh download, exact-offset resume, explicit clean restart, resume
protocol violation, oversized import, tamper detection, deletion, termination recovery, orphan
cleanup, store ownership, provenance-origin enforcement, and source-change restart behavior.

The first strict build identified only analyzer-level async-stream and static-member findings. Those
were repaired directly in source with memory-based async stream operations and static helpers; no
analyzer was disabled and no package-integrity rule was weakened. The exact repaired candidate is
being revalidated before acceptance is recorded.

No real GGUF, model download, API key, benchmark result, model selection, or model inference is
required for RMA-132.

## Scope boundary

RMA-132 approves exact installed bytes and a managed path only. RMA-133 still owns benchmark-backed
candidate/default selection. RMA-134 must validate runtime/GGUF compatibility and use an approved
artifact rather than an arbitrary path. RMA-135 owns resource/thermal governance. RMA-160 owns
durable manifest/settings persistence and RMA-163 owns the broader hostile import/URL hardening
pass.
