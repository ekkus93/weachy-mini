# RMA-170 Structured Logging and Diagnostic Event Hardening

## Scope

RMA-170 establishes one production diagnostic-event boundary for ordinary runtime
logging. The boundary keeps event identity and operational context useful without
serializing credentials, private conversation content, or raw media.

## Event contract

Every `ReachyDiagnosticRecord` carries:

- stable component and event identifiers;
- severity and typed error category;
- monotonic elapsed milliseconds;
- optional bounded session and turn identifiers;
- typed fields with an explicit data classification;
- occurrence/suppression counts and a rate-limit-summary flag.

`ReachyRuntimeDiagnostics` is the Unity sink. It serializes deterministic JSON and
selects `Debug.Log`, `Debug.LogWarning`, or `Debug.LogError` from event severity.
Acceptance/evidence marker logs remain separate because test automation depends on
their literal text.

## Redaction boundary

`ReachyDiagnosticRedactor` is applied before a record reaches any sink. Secret,
private-text, raw-audio, raw-image, and raw-media fields are replaced with
`[redacted]`. Credential-bearing headers are redacted. URL userinfo, query, and
fragment data are removed. Free/public text is bounded and common credential
markers are cut off before their values.

The diagnostic-bundle policy is default deny for secret/private/raw-media data
classes. RMA-172 may export only data admitted by that policy unless a later,
explicitly consented sensitive-content path is designed and reviewed.

## Repeated-event policy

The logger emits the first event in a burst immediately. Matching events inside a
5-second window are counted rather than emitted repeatedly. A flush or the next
window emits a final summary containing total and suppressed counts. Burst keys
include operation/provider/status/error discriminators so unrelated failures with
the same event ID do not collapse together.

## Runtime migration

The application host, main-screen bootstrap, CameraX bootstrap/UI-thread bridge,
authoritative renderer, and authoritative production runtime use the structured
boundary for ordinary runtime failures. These paths retain operation/category and
exception *type* where useful; they do not write raw exception messages to logs.
