# OpenAI and OpenAI-compatible VLM adapters

**Milestone:** RMA-115
**Status:** Implementation contract
**Date:** 2026-08-06

## Scope

RMA-115 adds two explicit remote vision-language-model adapters over the
existing RMA-110 `IVisionLanguageProvider` contract:

- `OpenAiResponsesVisionLanguageProvider`;
- `OpenAiChatCompletionsVisionLanguageProvider`.

Both adapters reuse the same `IOpenAiVisionTransport` boundary. They do not own
HTTP, TLS, credentials, endpoint discovery, retries, streaming, secure secret
storage, provider-profile persistence, or automatic provider selection. Those
surfaces remain later Phase 15 work. The transport receives a bounded semantic
request and returns a structured result; it never receives a raw phone frame or
an application secret from the adapter contract.

RMA-115 does not add a model ID assumption. Every provider instance receives an
explicit model ID through `OpenAiVisionProviderConfiguration`.

## Endpoint styles

The endpoint style is fixed when a provider is constructed and must match its
transport:

- **Responses style** maps the coverage context and user prompt to text input
  items and the encoded frame to an `input_image` item.
- **Chat Completions style** maps coverage context to a system message and the
  prompt plus encoded frame to user content containing text and an image URL.

The shared request declares `StoreResponse=false` and `Stream=false`. A later
transport may implement the exact wire representation, but it may not change
endpoint style, model identity, image bytes, coverage context, or execution
policy. An endpoint-style mismatch fails provider construction rather than
silently trying another protocol.

## Transformed-frame privacy boundary

The adapters accept only an observation-eligible
`VisionFrameOrigin.TransformedReachyEye` frame. Raw phone debug frames,
unavailable or unusable coverage, missing validity masks, disposed resources,
and stale frame identities fail before encoding or transport invocation.

`IRemoteVlmImageEncoder` must prove in its result that:

1. the source identity exactly matches the requested frame;
2. the source was the transformed Reachy-eye frame;
3. the validity mask was applied before resizing;
4. the configured invalid-pixel policy was applied;
5. the encoded payload contains only valid transformed image content;
6. no upscaling occurred;
7. output dimensions, format, quality, and byte count remain within policy.

The encoded byte array is copied into an owned payload and zeroed when disposed.
The adapter disposes that payload after the single transport attempt. It does
not dispose the caller-owned input frame.

## Image resizing and quality policy

`RemoteVlmImagePolicy` is explicit and bounded. The default policy is:

- maximum width: 1024 pixels;
- maximum height: 1024 pixels;
- maximum encoded size: 4 MiB;
- format: JPEG;
- JPEG quality: 85;
- detail: automatic;
- invalid pixels: black fill;
- validity mask application before resize: required;
- upscaling: forbidden.

Target dimensions preserve aspect ratio and use floor rounding, so neither
axis can exceed its configured bound. Alternate PNG or JPEG policies may be
configured explicitly. A transport does not resize, recode, crop, or substitute
an image after the encoder has satisfied this policy.

## Coverage disclosure

The adapter creates a separate system/developer coverage context rather than
silently appending ambiguous text to the user's question. The context states:

- that the image is a transformed Reachy-eye view;
- whether coverage is normal or degraded;
- the measured valid-pixel fraction;
- that invalid regions were excluded or replaced before encoding;
- that the model must not infer content outside valid coverage;
- that no world-model history or recent-but-stale entity list is included as
  currently visible context.

This boundary deliberately carries no entity list. A future conversation layer
may supply a separately typed immutable current-entity snapshot, but RMA-115
cannot turn stale world-model history into present-tense visual evidence.

## Structured results and error preservation

The transport returns one of these typed outcomes: succeeded, cancelled, timed
out, unavailable, rejected, failed, invalid response, or contract violation.
A success requires non-empty semantic text and no error object. A failure must
have no semantic text and includes a structured `OpenAiVisionProviderError`.

Safe error fields retain category, provider code, HTTP status, provider request
ID, retryability, and a bounded diagnostic detail. The sanitizer removes
credential-bearing header forms, secret-like key/value forms, data URLs, image
payload markers, control characters, and long opaque tokens. Uncaught transport
exceptions expose only their exception type, never `Exception.Message`.

Provider failures are returned to `VisionProviderExecutor` as typed visible
failures. The adapter does not fabricate success, retry, change endpoint style,
change model ID, or invoke a fallback provider.

## Cancellation, concurrency, and lifecycle

The provider honors caller cancellation before encoding, between encoding and
transport, and through the shared transport call. Maximum concurrent operations
is explicit in the provider configuration and enforced without an unbounded
queue. Excess work returns a visible unavailable result.

Provider and encoded-image disposal are deterministic and idempotent. Disposal
does not dispose the externally owned encoder or transport because those may be
shared by independently selected provider instances.

## Independence from tracking and scheduling

RMA-111 face/person tracking remains a bundled on-device path and does not
require a VLM. RMA-113 remains the sole admission/scheduling policy for VLM
requests. The adapters contain no camera-frame loop, timer, world-model polling,
or automatic invocation path.

## Validation

`managed/ReachyMini.RemoteVlm.Tests` uses deterministic fake encoders and a mock
transport. It never opens a network connection. The suite verifies both endpoint
styles, transformed-frame enforcement, valid-only encoding, image policy,
coverage disclosure, cancellation, concurrency, structured result validation,
error redaction, single-call/no-fallback behavior, disposal, and the absence of
stale entity context.
