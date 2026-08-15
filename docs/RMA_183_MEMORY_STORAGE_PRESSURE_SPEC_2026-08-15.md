# RMA-183 memory and storage pressure hardening

## Scope

RMA-183 turns Android low-memory and low-storage conditions into explicit, recoverable application behavior. It sheds recreatable resources without weakening simulation correctness, does not corrupt active inference, prevents storage exhaustion from masquerading as artifact corruption, and gives the user a narrowly scoped cleanup action.

## Memory-pressure contract

1. **One Android ingress owns the callback.** `ReachyApplicationHostBehaviour` subscribes to `Application.lowMemory` only after application startup succeeds and unsubscribes during shutdown.
2. **Recreatable camera textures are released immediately.** `ReachyAndroidCameraTextureBridge.ReleaseForMemoryPressure` invalidates the detached RGB output and destroys Y/U/V plane textures plus the output render texture. The next valid camera frame recreates those resources through the existing pump path.
3. **Unused Unity assets are eligible for reclamation.** The callback requests `Resources.UnloadUnusedAssets()` after explicit resource owners have been notified.
4. **Optional model ownership is explicit.** `ReachyMemoryPressureRegistry` provides a lifecycle-owned registration seam for optional resource owners. `LocalLlmProvider` registers while alive and unregisters before disposal.
5. **Active local-LLM state is not corrupted.** A low-memory sweep never interferes with a model while load/reload or generation is active. It returns `RetainedActiveState`. An idle loaded model is synchronously unloaded under the provider lock, the native handle is cleared exactly once, and provider state becomes `Unavailable`. Explicit `ReloadAsync` is the recovery path; there is no silent model/provider fallback.
6. **Pressure handling is observable and failure-isolated.** The application emits `application.memory.low_handled` with participant, release, active-retention, and failure counts. Camera-cache release, registered optional resources, and Unity unused-asset reclamation are isolated so one failure does not prevent later reclamation steps.

## Model-download storage contract

The package manager keeps the existing preflight requirement:

```text
available >= remaining model bytes + configured safety reserve
```

RMA-183 adds a periodic in-stream recheck every 4 MiB. If free space falls below the remaining-byte requirement, `DownloadAsync` returns the typed `InsufficientStorage` failure while preserving the manifest-bound `.part` file and metadata for an exact later resume. A write-side `IOException` also probes free space for the next copy buffer before falling back to generic `IoFailure`, so a disk-full condition remains explicitly classified when the storage probe can confirm it.

The same bounded copy primitive also protects imports. Unlike a download, an import remains non-resumable and its staging partial is deleted on storage-pressure failure.

## Diagnostic-export storage contract

`ReachyStorageAwareDiagnosticBundleExporter` wraps the bounded bundle exporter with an injectable storage probe and a 16 MiB default safety reserve. Before any output directory or temporary bundle is written, it requires:

```text
available >= MaximumBundleBytes + safety reserve
```

`MaximumBundleBytes` remains 8 MiB. If an I/O failure occurs after preflight and the probe shows the export reserve has been lost, the storage-aware wrapper raises `ReachyDiagnosticBundleInsufficientStorageException`. The application coordinator converts that into a stable user-facing “not enough free storage” outcome rather than exposing raw paths or exception text. Temporary export files retain the existing `finally` cleanup guarantee.

## Cleanup UI

The Local Model settings panel exposes **CLEAN UP RECOVERABLE STORAGE**. The settings-panel cleanup action constructs a narrowly scoped cleanup coordinator for the app diagnostic directory. That coordinator has only this deletion authority:

- delete only top-level app-generated `reachy-diagnostics-*` bundle or bundle-temporary files from the configured diagnostic directory;
- never follow reparse points;
- request Unity cache cleanup through `Caching.ClearCache()`;
- never delete installed model artifacts, settings, credentials, provider configuration, or active user state.

The UI reports what it removed and makes the preservation rule explicit. Model-package deletion remains owned by the manifest-bound package-manager APIs; this generic cleanup action does not bypass those ownership checks.

## Failure and fallback policy

RMA-183 does not enlarge the physics timestep, skip arbitrary physics work, silently cancel an active generation, auto-reload an unloaded model, switch providers/models, overwrite an existing diagnostic bundle, or recursively delete unowned storage. Low memory and low storage remain visible conditions that callers can recover from explicitly.

## Out of scope

Physical low-memory kill behavior, OEM-specific storage-manager UI, and representative-device pressure thresholds remain device-validation concerns. RMA-184 owns the representative-device matrix.
