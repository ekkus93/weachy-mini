# RMA-110 Vision Provider Contracts Validation

**Milestone:** RMA-110

**Date:** 2026-08-05

**Status:** Implementation validation in progress; no milestone completion claim

## Candidate contract

The candidate implementation defines separate frame-source, lightweight-tracker,
and semantic-VLM interfaces. Requests and results retain provider instance
identity, provider selection epoch, request identity, transformed-frame identity,
validity coverage, explicit timeout, and cancellation behavior.

The candidate frame boundary uses an owned asynchronous resource lease. Normal
perception cannot receive a raw phone frame, a missing validity mask, unusable
coverage, a disposed lease, or a stale source sequence. Explicit raw access is a
separate debug-only purpose.

The executor returns typed `Cancelled`, `TimedOut`, `ProviderFailure`,
`ContractViolation`, `InvalidFrame`, `Unavailable`, and `Superseded` results. It
does not retry, substitute providers, reuse stale output, or silently accept late
results. Timeout and provider faults require provider reset.

## Required validation before completion

RMA-110 will not be marked complete until one exact implementation SHA passes:

- the permanent RMA-110 managed and static contract workflow;
- hosted static, managed warnings-as-errors, native/sanitizer, Android, and
  pinned Reachy-model CI;
- real-graphics Unity tests;
- ARM64 API-26 APK build and verification;
- RMA-090, RMA-091, and RMA-092 physical camera acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance; and
- evidence and APK uploads plus final status publication.

Exact SHAs, run IDs, job IDs, test counts, and artifact digests will be appended
only after those gates succeed.
