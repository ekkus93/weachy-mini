# RMA-132 safe local-model package validation

**Status:** Implementation accepted on `d50e44d83b14e1e1420dc347164671db6593d73c`; final roadmap evidence SHA pending  
**Date:** 2026-08-07

## Accepted contract

RMA-132 establishes the verified package-acquisition and managed-installation boundary for local LLM artifacts. The accepted implementation preserves all of the following:

1. Downloads/imports check free storage before writing model bytes.
2. Every acquisition writes under the dedicated managed staging tree; no external arbitrary path is promoted directly.
3. Resumable downloads are bound to the exact manifest size/hash and explicit source URI fingerprint.
4. A resume offset mismatch fails visibly; unsupported resume performs an explicit clean restart.
5. Imports are readable streams and are intentionally non-resumable.
6. Exact byte count and SHA-256 are required before same-store atomic publication.
7. Installed paths are derived from manifest identity/hash and are revalidated before a `LocalModelApprovedArtifact` is issued.
8. App-termination recovery keeps only valid resumable download state and removes abandoned import partials.
9. Delete and orphan cleanup are confined to a marker-owned store and never follow managed reparse-point directories.
10. No source/model/provider fallback, benchmark selection, or cloud inference is introduced.

## Package and trust behavior

`LocalModelPackageManager` owns a dedicated absolute marker-bound store. It refuses filesystem-root ownership, symlink/reparse-point store roots, and unrelated nonempty directories without the exact store marker. Model-specific installed and staging paths are generated from already-validated RMA-131 manifest identity, expected SHA-256, and safe relative artifact path.

Imports accept a caller-opened `Stream`, not an arbitrary filesystem path. Downloads accept one explicit `Uri` and one `ILocalModelDownloadTransport`; the initial URI must be absolute HTTPS, credential/fragment free, and use the manifest-provenance host and port. The first-party `HttpLocalModelDownloadTransport` streams with `ResponseHeadersRead`, requests byte-identity content, uses HTTP byte ranges for resume, validates `Content-Range`, and supports a visible clean restart when a source rejects or ignores a range.

Download partials are resumable only while their sidecar exactly binds the retained bytes to the explicit source-URI fingerprint, manifest file size, and manifest artifact SHA-256. Source/manifest drift or malformed partial metadata restarts from zero instead of splicing unrelated bytes. Imports are deliberately non-resumable and abandoned import partials are removed during recovery.

Both acquisition paths require the exact manifest byte count, reject an extra byte, verify SHA-256, and only then move the staged artifact to the generated installed path. The final artifact is revalidated before the package manager returns the assembly-internal `LocalModelApprovedArtifact`. A corrupt installed file never produces an approved path. A verified replacement quarantines the corrupt final file before publishing the replacement.

Deletion is manifest-derived rather than path-driven. Cleanup removes loose staging-root files, loose manifest-level files, unknown staging hash trees, unexpected children in known staging directories, quarantine contents, and installed orphans without following reparse-point directories. Known corrupt artifacts corresponding to a current manifest are reported rather than silently treated as healthy or silently discarded.

## Ralph-loop corrections

The first strict core build rejected obsolete array-based asynchronous stream calls, a missing memory-based stream override, and instance helpers that analyzers required to be static. Those findings were repaired directly with memory-based async APIs and static helpers; no analyzer was suppressed.

The following managed-test build rejected a repeatedly allocated constant ABI array and instance SHA-256 hashing. The harness was corrected to use a static readonly ABI fixture and `SHA256.HashData`; production behavior did not change.

After all 15 executable package cases passed, the independent Python source contract exposed an over-broad arbitrary-path assertion: it mistook a local recovery variable named `importPath` for an `ImportAsync` path parameter. The policy was narrowed to inspect the public method signature specifically and require `Stream source` while rejecting string path parameters.

Pre-acceptance review then found a genuine cleanup edge that the original behavior matrix did not cover: loose files outside staging hash directories could survive orphan cleanup. The production cleanup and its existing orphan-cleanup case were hardened before acceptance to cover staging-root files, manifest-level files, unknown hash trees, and unexpected children within known staging directories. Ruff lint/format findings in the Python policy were corrected without changing its semantics.

No fallback, integrity relaxation, analyzer disablement, warning suppression, or fake successful installation was added during this loop.

## Accepted automated evidence

Accepted implementation SHA:

`d50e44d83b14e1e1420dc347164671db6593d73c`

Dedicated RMA-132 workflow run `31212296409`, job `92977704407`, completed successfully on that exact SHA. It passed:

- `ReachyMini.Core` Release build with warnings as errors;
- all 15 `ReachyMini.LocalModelPackages.Tests` deterministic package behaviors;
- deterministic package-management static contracts;
- Python contract compilation;
- exact-SHA source hashing and evidence generation; and
- evidence artifact upload.

Artifact `9007154955`, `rma132-local-model-packages-d50e44d83b14e1e1420dc347164671db6593d73c`, has digest:

`sha256:3babe8eea5088de9e6b4f45da8115f562f03b051c233cb31ecedd3310f36f7c3`

Hosted CI run `31212296177` also completed successfully on the same exact SHA. Static policy, managed warnings-as-errors/native lifecycle, native warnings/sanitizers, Android lint/Java/tests, and pinned Reachy-model validation all passed.

The managed suite uses only tiny synthetic bytes and temporary directories. No real GGUF, model download, API key, benchmark result, model selection, or model inference was required or performed for RMA-132.

## Scope boundary

RMA-132 approves exact installed bytes and a managed path only. It does not make the RMA-082 explanatory install/select UI operational by itself and does not choose a model. RMA-133 still owns benchmark-backed candidate/default selection. RMA-134 must validate runtime/GGUF compatibility and consume an approved artifact rather than an arbitrary path. RMA-135 owns resource/thermal governance. RMA-160 owns durable manifest/settings persistence and RMA-163 owns the broader hostile import/URL hardening pass.
