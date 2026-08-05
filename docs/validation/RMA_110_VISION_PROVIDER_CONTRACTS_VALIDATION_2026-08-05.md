# RMA-110 Vision Provider Contracts Validation

**Milestone:** RMA-110

**Accepted implementation SHA:** `bc611b700b6bb212d4a04a927e5935d326345e05`

**Closeout baseline SHA:** `cf8e215f15fc879b2bf74b3bf1e26e76dff5213f`

**Date:** 2026-08-05

**Status:** Complete

## Implemented contract

The implementation separates the authoritative transformed frame source,
lightweight tracking, and semantic VLM provider boundaries:

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
token, quarantines the provider instance by requiring reset, observes any
later fault, and returns without accepting a late result. Provider faults
retain the exception type in diagnostics and also require reset. A provider
switch increments the selection epoch; any older in-flight completion is
returned as `Superseded` and its owned frame is disposed.

The executor does not retry, select a fallback provider, reuse stale output,
swallow provider failure, or silently substitute a raw frame. Remote VLM
invocation requires explicit network-disclosure acknowledgement.

## Managed regression contracts

`Rma110VisionProviderContracts` is discovered by a module initializer and
verifies:

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

## Candidate failures and repair

The first clean candidate, `4b8b60b400dc4dd40f4394024d29f160adb9440b`,
failed managed compilation because `Diagnostic<T>` lacked the reference-type
constraint required by `Execution<T>`. A direct repair attempt briefly exposed
an executor draft that did not match the committed public contract API and was
rejected. The executor was restored from the atomic implementation commit and
only two compiler-required changes were retained:

- `where T : class` on `Diagnostic<T>`; and
- `CancellationToken.None` on the independent timeout clock, preserving the
  distinction between caller cancellation and timeout.

No compatibility adapter, retry, fallback, or weakened contract was added.
The temporary payload, applicator, repair, and closeout files were removed
before the final clean closeout baseline.

## Accepted hosted validation

Hosted CI run `31046134407` completed successfully on exact implementation
SHA `bc611b700b6bb212d4a04a927e5935d326345e05`:

- pinned Reachy-model job `92441974641`;
- Android job `92441974646`;
- native strict-warning and sanitizer job `92441974680`;
- static policy job `92441974685`; and
- managed warnings-as-errors job `92441974688`.

## Accepted real-graphics and physical validation

Local Unity Android Validation run `31046135097`, job `92442017380`,
completed successfully on the same exact implementation SHA.

Unity artifact `8946596611` has digest
`sha256:7f6be3d70f9217fbbf423d1a4e51ab7a4a82452f266b5881a47bdb440c842f94`
and records:

- EditMode `125/125` passed;
- PlayMode `1/1` passed;
- real OpenGL Core/Mesa rendering rather than `NullGfxDevice`; and
- all existing reprojection, coverage, CameraX close-barrier, and lifecycle
  Unity contracts remained green.

The same run passed ARM64 API-26 APK build and verification, RMA-090 camera
discovery, RMA-091 CameraX acquisition, RMA-092 physical GPU texture
acceptance, RMA-022 lifecycle acceptance, authoritative rendering, every
evidence upload, APK upload, and final commit-status publication.

Accepted artifacts are:

- Unity tests: `8946596611`,
  `sha256:7f6be3d70f9217fbbf423d1a4e51ab7a4a82452f266b5881a47bdb440c842f94`;
- RMA-090: `8946668433`,
  `sha256:24ebc0cb93ea35133d0c307c737ee5f936c0241f0297dca9e2a07b62972e5e5a`;
- RMA-091: `8946707369`,
  `sha256:3ab723dd120394e488fac6a8cde3929bbec8a3dfb6f5f8bf081f3c4333b1acef`;
- RMA-092: `8946739605`,
  `sha256:592ad4d771e0a1364a90cd07061aa496e97be94e25d51e17e89e7f42e3196ee1`;
- lifecycle: `8946772501`,
  `sha256:e94d73ad8cf366cae117049d0825ed06b4f885755bddd12d0977891450ae0c86`;
- authoritative rendering: `8946790088`,
  `sha256:a7c5527d390e97ee2a13d9e5f0ad77a1313aeb30438a33f88abea2d1b6cdb95f`;
  and
- APK: `8946814062`,
  `sha256:93960ea4828ad722841cfae1c33d9c9729eb44dc5725e32f8c8f5b8feac0c84d`.

## Closeout validation

The clean closeout baseline SHA
`cf8e215f15fc879b2bf74b3bf1e26e76dff5213f` contains:

- the completed authoritative RMA-110 TODO section;
- the accepted implementation and validation report;
- the hardened permanent RMA-110 workflow; and
- no payload chunk, patch script, applicator, repair, or cancellation workflow.

The first long-form closeout workflow wrapper was rejected by GitHub before a
job was created because its embedded YAML was invalid. It changed no product,
test, TODO, or validation file. A minimal wrapper then executed the separately
validated Python patch successfully, and both the patch file and wrapper were
removed before the clean baseline above.

The permanent RMA-110 workflow watches the contracts, routing policy, managed
regression suite, architecture, validation report, and authoritative TODO. It
rejects retry/fallback patterns, tracked build output, missing evidence tokens,
and repository whitespace errors.

Exact SHAs remain mandatory. The final evidence-only commit that records this
clean baseline must pass the permanent RMA-110 workflow, hosted CI, and the
complete self-hosted Unity/Android chain before final sign-off.
