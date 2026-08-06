# RMA-111 Lightweight On-Device Tracking

## Purpose

RMA-111 installs the first real `IVisualTracker` implementation. It performs
bounded face and person inference on the transformed Reachy-eye frame without
calling a VLM, sending image data off-device, or falling back to another
provider.

## Selected mobile stack

The Android backend uses the bundled Google ML Kit artifacts:

- `com.google.mlkit:face-detection:16.1.7`; and
- `com.google.mlkit:segmentation-selfie:16.0.0-beta6`.

The bundled variants package their models in the APK and are immediately
available after installation. The implementation does not use the Google Play
Services model-download variants. Face detection is the stable primary path.
Selfie segmentation is explicitly beta and is isolated behind
`IReachyTrackingDetectionBackend`; it emits at most one aggregate `person`
detection and can be replaced without changing the managed tracker contract.
Object and motion capabilities remain false because RMA-111 has no accepted
performance evidence for them.

## Frame and coordinate contract

The tracker never consumes the raw phone camera frame. The input is an owned
`ReachyVisionFrame` whose origin is `TransformedReachyEye` and whose coverage is
observation eligible.

`ReachyUnityVisionFrameFactory` clones the RMA-102 color and validity render
textures before the reusable warp targets can be overwritten. The owned lease
retains the exact RMA-101/RMA-102/RMA-103 frame identity and coverage metadata.
For inference, `ReachyUnityTrackingFrameResources`:

1. downsizes color and validity to a maximum dimension of 640 while preserving
   aspect ratio;
2. uses bilinear color sampling and point-sampled validity;
3. performs bounded `AsyncGPUReadback` rather than synchronous `ReadPixels`;
4. converts both buffers to top-left row-major order; and
5. publishes RGBA8 color plus an 8-bit validity mask with the unchanged frame
   identity.

ML Kit therefore reports normalized boxes directly in transformed Reachy-eye
coordinates. No post-detection raw-to-transformed coordinate guess is allowed.

## Validity enforcement

The managed tracker samples the validity mask at every detection center. A face
or person centered on a value below 128 is omitted before stable association.
The backend cannot override this policy. Missing staging resources, changed
frame identity, unsupported detection classes, malformed bounds, or unavailable
coverage fail visibly.

## Stable local IDs and expiry

ML Kit face tracking IDs are treated only as association hints. The managed
`ReachyStableTrackStore` owns application-local IDs:

- `face-000001`, `face-000002`, ...; and
- `person-000001`, `person-000002`, ....

Association is deterministic and uses class, provider-ID continuity, intersection
over union, center distance, and lexical local-ID tie breaking. The engineering
baseline expires a track after more than four missed frames or 1.5 seconds
unseen. Camera, session, or simulation-continuity changes clear association
state. ID counters are not reset, so an expired entity is never presented as a
new observation under a reused local ID during the provider lifetime.

Frame ordering is strict within one camera session and continuity. Duplicate or
backward sequence/timestamp input fails closed rather than reusing prior output.

## Concurrency, cancellation, and ownership

The provider advertises one concurrent operation. A second request returns an
explicit busy/unavailable result; it is not queued or retried. The RMA-110
executor retains the authoritative request timeout, caller cancellation,
provider selection epoch, result identity, and supersession checks.

The tracker borrows the frame lease and never disposes it. The caller owns frame
lifetime. The tracker owns and disposes the Android backend. Frame resources defer
texture destruction until any active GPU readback completes, and disposal is
idempotent.

ML Kit inference tasks are not substituted after cancellation. Cancellation is
visible to managed callers; any backend drain remains bounded by the enclosing
RMA-110 timeout and no new provider is selected automatically.

## Android bridge

`ReachyMlKitTrackingBridge` accepts a bounded top-left RGBA buffer, starts one
face task and one segmentation task, and returns a schema-versioned JSON payload.
It caps input at 2048 x 2048 and output at 64 detections on the managed side.

Face confidence is emitted as `1.0` because ML Kit face detection does not
provide a calibrated overall face confidence. It must not be interpreted as a
probability. Person confidence is the mean segmentation score for mask pixels at
or above 0.65.

The bridge has no network transport, model-download API, VLM path, retry loop, or
provider fallback.

## Physical acceptance fixture

Physical acceptance embeds the 250-pixel Wikimedia Commons thumbnail of
`BarackObamaportrait.jpg`, an official United States Senate portrait identified
as public domain. The exact downloaded bytes and SHA-256 are generated once by
the source applicator and recorded in the generated C# fixture and third-party
notice.

The device acceptance runs the real bundled ML Kit backend three times:

1. detect at least one face;
2. retain the same managed local face ID on the next frame; and
3. suppress the same face after its transformed-frame center is marked invalid.

The report also proves that VLM invocation count is zero, object/motion
capabilities are false, and the bundled backend identity is selected.

## Explicit non-goals

RMA-111 does not implement:

- semantic scene descriptions;
- continuous VLM execution;
- general object classification;
- optical-flow or motion tracking;
- world-model persistence; or
- automatic behavior decisions.

Those belong to RMA-112 and later milestones.
