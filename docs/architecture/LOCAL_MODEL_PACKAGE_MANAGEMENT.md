# RMA-132 safe local-model download and import

**RMA:** 132  
**Scope:** package acquisition, integrity, installation, recovery, deletion, and approved-path
resolution. Model selection/benchmarking remains RMA-133; inference remains RMA-134.

## Trust boundary

RMA-131 manifests describe the expected artifact but do not authorize arbitrary filesystem or
network access. RMA-132 turns one validated manifest plus one explicit acquisition source into a
verified app-private installation.

The package manager never accepts a model path for loading. Imports are caller-opened `Stream`
instances, downloads use an explicit `ILocalModelDownloadTransport`, and the only loadable path
object is `LocalModelApprovedArtifact`. Its constructor is internal to the core assembly and the
package manager issues it only after rechecking the installed file's exact byte length and SHA-256.

A filename, `.gguf` extension, user-selected document path, or successful download is therefore not
an approval by itself.

## Managed store ownership

`LocalModelPackageManager` owns one dedicated absolute directory. It refuses to own a filesystem
root, a symlink/reparse-point root, or a nonempty directory that does not contain its exact marker.

The layout is:

```text
<store>/
  .reachy-local-model-store-v1
  installed/
    <manifest-id>/
      <artifact-sha256>/
        <manifest artifact relative path>
  staging/
    <manifest-id>/
      <artifact-sha256>/
        artifact.download.part
        artifact.download.meta
        artifact.import.part
  quarantine/
    <manifest-id>/
      <artifact-sha256>/
        corrupt-<opaque-id>.gguf
```

All model-specific child paths are generated from already-validated RMA-131 manifest identifiers,
hashes, and safe relative artifact paths. Generated paths are canonicalized and required to remain
inside the managed root. Existing managed directories/files are rejected when they are
symlinks/reparse points.

The ownership marker is important for cleanup: pointing the manager at an unrelated nonempty
directory cannot authorize recursive deletion.

## Storage preflight

Every new import/download checks available bytes before model data is written. The production
`DriveInfoLocalModelStorageProbe` measures free bytes on the filesystem containing the managed
store. Tests can supply a deterministic probe.

The check requires:

```text
available >= remaining model bytes + configured safety reserve
```

The default artifact ceiling is 8 GiB and the default free-space reserve is 64 MiB. Both are
explicit `LocalModelPackageOptions`; neither changes the manifest's expected file size.

A resumed download checks only its remaining bytes plus reserve. Failure returns
`InsufficientStorage`; acquisition is not attempted and no alternate model is selected. RMA-183
also rechecks the same invariant every 4 MiB while model bytes are being written. If storage falls
below the remaining-byte requirement, the exact manifest-bound download partial and metadata remain
for a later resume; a non-resumable import partial is removed. A write-side I/O failure is re-probed
for storage pressure before it is classified as a generic I/O failure.

## Download contract

`DownloadAsync` receives one exact `LocalModelManifest`, one explicit artifact URI, and one
explicit `ILocalModelDownloadTransport`.

The initial URI must be absolute HTTPS, contain no credentials or fragment, fit the bounded URI
length, and use the same host and port as `manifest.Identity.SourceUri`. A transport cannot choose a
second model or URI on package-manager failure.

`HttpLocalModelDownloadTransport` is the first-party streaming transport:

- requests `Accept-Encoding: identity`;
- uses `Range: bytes=<offset>-` when resuming;
- streams with `ResponseHeadersRead` rather than buffering the whole GGUF;
- validates 206 `Content-Range`;
- treats HTTP 416 as an explicit clean-restart request;
- accepts HTTP 200 as a safe full-body restart when a server ignores a range;
- rejects other statuses without reading an error body; and
- requires any final redirected URI reported by `HttpClient` to remain HTTPS and credential/fragment
  free.

There is no automatic alternate host/model/provider fallback. Cross-host HTTPS redirects are not
the same as model fallback: they are part of the one explicit HTTP transaction and the final bytes
must still satisfy the manifest's exact size and SHA-256. Broader hostile-URL/SSRF policy remains
part of RMA-163.

## Resume or clean restart

A download partial is resumable only when `artifact.download.meta` exactly binds it to:

- schema marker `rma132-download-v1`;
- SHA-256 of the explicit source URI string;
- manifest expected byte size; and
- manifest artifact SHA-256.

If the sidecar is missing, malformed, source fingerprint changes, manifest size/hash changes, or the
partial is larger than the expected artifact, the manager discards that state and starts from byte
zero.

When a transport supports resume, its response offset must exactly match the retained partial
length. An unexpected offset is a visible `ResumeProtocolViolation`; bytes are never spliced
silently. If a source explicitly cannot resume, or returns a full response from offset zero, the
partial is truncated and the operation performs a clean restart.

An early EOF leaves a manifest-bound download partial for a later exact resume. Oversized content,
wrong SHA-256, or contradictory resume metadata is discarded rather than reused.

## Import contract

`ImportAsync` accepts a readable stream, not a path. Android document-picker/platform code may open
a content URI and pass its stream, but the core never converts an arbitrary external path into an
approved model.

Imports always stage from byte zero. They are deliberately non-resumable because document streams
do not provide a stable identity/range contract. Cancellation, early termination, excess bytes, or
hash mismatch cannot publish an installed artifact. Recovery removes abandoned
`artifact.import.part` files.

## Integrity and atomic publication

Both acquisition paths write only under `staging/`.

Before publication the manager requires:

1. exact byte count;
2. no extra byte beyond the manifest count; and
3. exact lowercase SHA-256 equality with the RMA-131 artifact hash.

Only then is the staging file moved to its generated final path. Staging and installed trees are
under the same managed root, so publication is a same-store filesystem rename rather than
copy-then-delete.

If an existing final path is corrupt, it is never returned as approved. A verified replacement
moves the corrupt file to `quarantine/` first and then publishes the verified staging file. The new
final file is re-resolved and rehashed before `LocalModelApprovedArtifact` is returned.

RMA-132's approval proves exact manifest byte identity and managed-store containment. It does not
claim that GGUF-declared architecture/tokenizer metadata is runtime-compatible or that the model is
recommended. RMA-133/RMA-134 must perform those later semantic/runtime checks before generation.

## App-termination recovery

`RecoverAsync` is deterministic and catalog-bound:

- abandoned import partials are removed;
- a download partial is retained only when its exact sidecar is valid and its size is not larger
  than the manifest;
- malformed/incomplete download state is removed;
- installed artifacts are revalidated; and
- known corrupt installed artifacts are reported, not silently treated as absent or deleted.

A subsequent `DownloadAsync` resumes a retained partial from its exact current byte count. A crash
after final rename but before staging metadata cleanup is also safe: resolving the exact final
artifact wins, and no second copy is published.

## Deletion and orphan cleanup

`DeleteAsync` derives the exact installed path from a manifest. It exposes no arbitrary-delete path.

`CleanupOrphansAsync` receives the current manifest catalog and removes staging entries that cannot
correspond to a known manifest, quarantine contents, and installed files that are not exact expected
manifest paths. Known corrupt manifest artifacts are retained and reported instead of silently
deleted. Recursive deletion never follows a reparse-point directory.

## Concurrency and cancellation

Package operations are serialized per manager instance so download/import/delete/cleanup cannot
race the same store. Waiting and streaming reads/writes honor cancellation.

Download cancellation can leave the already-written, manifest-bound partial for an exact later
resume. Import cancellation removes its non-resumable partial. No cancellation path publishes an
approved artifact.

## Failure and fallback policy

Failures are typed through `LocalModelPackageFailure` and carry bounded operational detail.
Notably:

- storage-probe failure is not treated as "probably enough space";
- source failure does not select another URI or model;
- resume mismatch does not append anyway;
- hash mismatch does not install;
- a corrupt installed model does not produce an approved path;
- an unowned store does not get cleaned; and
- missing models do not invoke a cloud provider.

RMA-133 owns model evaluation/recommendation. RMA-134 owns inference. RMA-135 owns runtime resource
and thermal policy. RMA-160 owns durable model-manifest/settings persistence, and RMA-163 owns the
later broader untrusted-file/URL hardening pass.
