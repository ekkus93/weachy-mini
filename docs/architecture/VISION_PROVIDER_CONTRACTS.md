# Vision Provider Contracts

## Scope

RMA-110 defines the stable managed boundary between the transformed Reachy-eye
frame source, lightweight visual trackers, and semantic vision-language
providers. It does not select a concrete tracker or VLM. RMA-111 and later
milestones implement providers against this boundary.

The contract is deliberately fail-closed. A provider result is never accepted
merely because an asynchronous call completed. The result must still match the
selected provider instance, selection epoch, request identifier, and source
frame identity.

## Provider separation and identity

The three provider kinds are distinct:

- `IReachyVisionFrameSource` owns acquisition of an immutable frame lease;
- `IVisualTracker` produces bounded tracking observations from one transformed
  frame; and
- `IVisionLanguageProvider` produces semantic text from one transformed frame
  and an explicit prompt.

Every provider publishes a `ProviderDescriptor` containing a stable provider
identifier, a runtime instance identifier, version, location, and provider kind.
The runtime instance identifier is part of every request and result. A
`VisionProviderSelection` also publishes a monotonically increasing selection
epoch. Replacing a provider increments that epoch even when the stable provider
identifier is unchanged.

A result from an old instance or epoch is returned as `Superseded`; its payload
is not forwarded. Provider identity mismatch is a `ContractViolation` and
requires provider reset.

## Owned frame lease

`ReachyVisionFrame` owns an `IReachyVisionFrameResources` lease. This is
necessary because the RMA-102 renderer reuses render targets and asynchronous
providers cannot safely borrow a target that may be overwritten by the next
camera frame.

The resource owner exposes:

- a stable owner identifier and generation;
- immutable width and height;
- color and validity-mask resource presence and encoding;
- typed access to provider-native GPU resources; and
- idempotent asynchronous disposal.

The frame is the sole owner of the lease. Trackers and VLM providers borrow the
frame for the duration of one request and must not dispose it. A frame-source
result that is stale, invalid, mismatched, or superseded is disposed by the
executor before returning the failure.

## Frame and coverage requirements

Normal perception accepts only `TransformedReachyEye` frames. They must carry:

- camera identifier;
- camera session and source sequence;
- capture timestamp;
- authoritative MuJoCo sequence and continuity identifier;
- color resource;
- explicit validity-mask resource;
- valid and total pixel counts; and
- coverage state plus the planner-facing turning-stop signal.

The coverage measurement must match the resource dimensions. `Normal` and
`Degraded` frames may create visual observations. `Unusable` and `Unavailable`
frames may not.

Raw phone frames are a separate `RawPhoneDebug` origin and are available only
through the `ExplicitRawDebug` frame-source purpose. They never substitute for a
missing or unusable transformed frame. Raw debug coverage remains explicitly
`Unavailable` because the Reachy-eye validity contract does not apply to it.

## Cancellation, deadlines, and failures

Every request carries a caller-generated request identifier and an explicit
positive timeout no longer than five minutes. `VisionProviderExecutor` links the
caller cancellation token to the provider invocation and races completion
against the deadline.

Outcomes are typed:

- `Cancelled` means the caller cancelled; provider reset is not required;
- `TimedOut` means the deadline expired; the provider is quarantined until reset;
- `ProviderFailure` preserves the exception type in diagnostics and requires
  reset;
- `ContractViolation` means provider metadata or result identity is invalid;
- `InvalidFrame` means the frame is stale or observation-ineligible;
- `Unavailable` means a required disclosed capability or consent is absent; and
- `Superseded` means selection changed while work was in flight.

Timeout does not trigger a retry. Provider failure does not trigger a fallback.
The executor observes late task faults so they are not silently lost, but never
accepts a late payload after timeout, cancellation, or provider replacement.

## Network disclosure

A provider marked `LocalNetwork` or `Cloud` requires explicit network disclosure
acknowledgement before semantic analysis is invoked. The executor returns
`Unavailable` without calling the provider when acknowledgement is absent.
No API key or private media is included in provider descriptors or diagnostics.

## Capability metadata

Frame sources declare GPU color, GPU validity-mask, cancellation, maximum frame
dimensions, and maximum outstanding frame leases. Trackers declare supported
face/person/object/motion classes, GPU-frame consumption, cancellation, and
maximum concurrency. VLM providers declare visual-question and scene-description
support, cancellation, maximum concurrency, and prompt length.

Unknown, empty, or internally inconsistent capabilities fail during contract
construction. A provider cannot claim RMA-110 compatibility while omitting
cancellation or validity-mask support.

## Non-goals

RMA-110 does not:

- choose ML Kit, MediaPipe, LiteRT, or another tracker;
- implement a local or cloud VLM;
- schedule VLM requests;
- retain world-model entities; or
- permit continuous VLM execution at camera frame rate.

Those are RMA-111 through RMA-115 concerns. This milestone establishes the
identity, ownership, cancellation, timeout, coverage, and failure semantics they
must obey.
