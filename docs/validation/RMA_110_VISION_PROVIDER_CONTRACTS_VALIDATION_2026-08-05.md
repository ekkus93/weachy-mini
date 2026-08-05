# RMA-110 Vision Provider Contracts Validation

**Milestone:** RMA-110

**Accepted implementation SHA:** `64587bae3b977ff16f6a9d3f7b416af0b1f64a62`

**Clean closeout baseline SHA:** `133735ae0e7bcbf6c0e7a30d366b7860272f8ecc`

**Date:** 2026-08-05

**Status:** Complete; final evidence-only SHA validation required

## Implemented contract

RMA-110 separates the authoritative transformed frame source, lightweight
tracking, and semantic VLM provider boundaries:

- `IReachyVisionFrameSource` produces owned frame leases;
- `IVisualTracker` consumes transformed observation-eligible frames; and
- `IVisionLanguageProvider` consumes explicit semantic requests.

`ProviderDescriptor` and capability records retain provider kind, locality,
stable provider identity, per-instance identity, limits, and feature support.
`VisionProviderSelection` publishes a monotonic selection epoch so a result
from a replaced provider cannot be accepted by a newer selection.

`ReachyVisionFrame` owns `IReachyVisionFrameResources` through
`IAsyncDisposable` and retains source sequence, timestamp, camera/session,
calibration, model, authoritative simulation sequence/continuity, dimensions,
orientation, mirror state, color availability, validity-mask availability,
valid coverage, coverage class, and observation eligibility.

Normal tracking, VLM, world-model, behavior, and diagnostics requests require
a transformed Reachy-eye frame with an owned color resource, validity mask,
and usable coverage. A raw phone frame is rejected unless the distinct
`ExplicitRawDebug` purpose was explicitly requested. Missing resources,
disposed leases, stale sequences, unusable coverage, and identity drift fail
closed.

## Cancellation, timeout, and failure behavior

Every request carries a `VisionRequestContext` with provider identity,
selection epoch, request identity, and an explicit bounded timeout. The
executor returns typed `Cancelled`, `TimedOut`, `ProviderFailure`,
`ContractViolation`, `InvalidFrame`, `Unavailable`, and `Superseded` results.

Caller cancellation and timeout are distinct. A timeout cancels the provider
token, requires provider reset, observes any later fault, and returns without
accepting a late result. Provider faults remain visible and also require reset.
A provider switch increments the selection epoch; any older in-flight
completion is returned as `Superseded` and its owned frame is disposed.

The executor does not retry, select a fallback provider, reuse stale output,
swallow provider failure, or silently substitute a raw frame. Remote VLM
invocation requires explicit network-disclosure acknowledgement.

## Managed regression contracts

`Rma110VisionProviderContracts.RunAsync()` is awaited from the managed test
project's real asynchronous `Main` entry point. It verifies:

- explicit provider kinds, identities, locality, limits, and capabilities;
- transformed frames require owned color, validity, and coverage resources;
- raw fallback and stale sequence rejection;
- typed caller cancellation;
- timeout quarantine with exactly one invocation and no retry;
- visible provider faults with reset required;
- provider-switch supersession of late results;
- result identity mismatch rejection;
- cloud-disclosure enforcement before invocation; and
- exactly-once asynchronous frame-resource disposal.

Fake frame sources, trackers, VLM providers, frame leases, and backing resources
use deterministic `await using` ownership. The permanent gate rejects the
former `[ModuleInitializer]` plus `GetAwaiter().GetResult()` bootstrap, which is
not a safe host for an asynchronous cancellation suite.

## Ralph-loop failures and repairs

The loop retained the rejected candidates as evidence:

- `4b8b60b400dc4dd40f4394024d29f160adb9440b` failed managed compilation because
  `Diagnostic<T>` lacked the reference-type constraint required by
  `Execution<T>`.
- An intermediate executor draft did not match the committed public contract API
  and was rejected rather than preserved through compatibility aliases.
- The permanent closeout gate surfaced CA2000 ownership errors in fake providers
  and frame resources. The analyzer remained an error; fixtures were repaired
  with deterministic asynchronous disposal.
- Those ownership repairs exposed the synchronous module-initializer bootstrap.
  The asynchronous cancellation test could deadlock during type initialization,
  so the suite was moved into the project's async `Main` and explicitly awaited.

No analyzer suppression, compatibility adapter, retry, fallback provider,
raw-frame substitution, or weakened cancellation assertion was added. All
payload chunks, patch scripts, applicators, repair workflows, progress
diagnostics, cancellation workflows, and evidence-stage files were removed
before the clean closeout baseline.

## Permanent RMA-110 validation

Permanent workflow run `31050417256`, job `92456020415`, passed on exact
implementation SHA `64587bae3b977ff16f6a9d3f7b416af0b1f64a62`.
It validated:

- the complete managed camera and vision contract executable under
  warnings-as-errors;
- provider identity, ownership, cancellation, timeout, supersession, privacy,
  and routing source contracts;
- the async `Main` entry-point requirement;
- deterministic `await using` fixture ownership;
- rejection of the synchronous module-initializer bootstrap;
- rejection of retry/fallback patterns and temporary repair files; and
- final commit-status publication.

## Hosted CI validation

Hosted CI run `31050417844` passed on the same exact SHA:

- Android job `92456021804`;
- static policy job `92456021857`;
- native strict-warning and sanitizer job `92456021880`;
- pinned Reachy-model job `92456021897`; and
- managed warnings-as-errors job `92456022046`.

## Real-graphics and physical validation

Local Unity Android Validation run `31050417574`, job `92456090409`, passed on
the same exact SHA.

Unity artifact `8948243455` has digest
`sha256:1ddb0f7c42ed1a1c840b85d95ab81e2af0e9bc70d62a506b0f7ca9e6cbd877cd`
and records:

- EditMode `125/125` passed;
- PlayMode `1/1` passed;
- OpenGL Core with Mesa llvmpipe; and
- no `NullGfxDevice` fallback.

The same run passed ARM64 API-26 APK build and verification, RMA-090 camera
discovery, RMA-091 CameraX acquisition, RMA-092 physical GPU texture
acceptance, RMA-022 lifecycle acceptance, authoritative rendering, every
evidence upload, APK upload, and final commit-status publication.

RMA-092 evidence records:

- CameraX `CLOSED` before the rotated rear-camera restart and front-camera
  switch;
- physical Vulkan/Adreno GPU output;
- rear-camera rotations at 0 and 90 degrees;
- a mirrored, non-uniform front-camera capture at 270 degrees;
- exact timestamp correspondence; and
- zero stale accepted or uploaded texture frames.

Accepted artifacts are:

- Unity tests: `8948243455`,
  `sha256:1ddb0f7c42ed1a1c840b85d95ab81e2af0e9bc70d62a506b0f7ca9e6cbd877cd`;
- RMA-090: `8948306356`,
  `sha256:5fa89eeddfccd4e70508ca7efc51b31e28a56cef231319cf9b380192b22fb7c7`;
- RMA-091: `8948340996`,
  `sha256:7ad1cab4e436b3fa0efaeac192b5a08ed5abfd1fb1381f31fe1164c75d53a5f4`;
- RMA-092: `8948366730`,
  `sha256:88deb44f75212bc3b6ee70e0a62397f7ed3770a806415015f5f8a9ffbaa81c3f`;
- lifecycle: `8948393449`,
  `sha256:7d88d08e452d1a6105dcda8b722cd8f4b146de1134d304a53828c3b2a1337d16`;
- authoritative rendering: `8948408637`,
  `sha256:cc9187aa39ad75815a21c5e4c4e8c08ce4200b893b5435e08826eabc18465941`;
  and
- APK: `8948424246`,
  `sha256:ceb63bfea9b9063865c9bf36281f93a3ff79199caf58b9b1502a602ff2354672`.

## Final closeout

The clean closeout baseline
`133735ae0e7bcbf6c0e7a30d366b7860272f8ecc` contains the updated authoritative
TODO and no staging script or workflow. The commit containing this finalized
report is evidence-only and changes no product code, production workflow, or
test behavior.

Exact SHAs remain mandatory. That final evidence-only commit must pass the
permanent RMA-110 workflow, hosted CI, and the complete self-hosted
Unity/Android chain before final sign-off.
