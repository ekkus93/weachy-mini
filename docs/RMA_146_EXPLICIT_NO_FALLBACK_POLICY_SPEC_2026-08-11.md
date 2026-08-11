# RMA-146 — Explicit No-Fallback Policy Engine Specification

Date: 2026-08-11

## Goal

Make every automatic provider fallback an explicit, fail-closed authorization decision. ASR, TTS, LLM, and VLM policy state is independent. Manual user provider selection remains distinct from automated failure recovery.

## Default policy

Every workload starts with the named `no-fallback` policy:

- local quality reduction: disabled;
- same-provider retry: disabled;
- cross-provider switch: disabled;
- network-provider switch: disabled;
- authorized target provider IDs: empty.

A policy that disables cross-provider switching cannot retain fallback targets. Network switching cannot be enabled unless cross-provider switching is also enabled.

## Automated provider switching

`ReachyProviderFallbackPolicyEngine` evaluates a `ReachyProviderSwitchRequest` containing source provider, target provider, workload, privacy boundaries, and a bounded reason code.

A cross-provider switch is authorized only when:

1. cross-provider switching is enabled for that workload;
2. the target provider ID is explicitly allowlisted;
3. a network-boundary target is separately permitted; and
4. any privacy-boundary change carries a matching one-time confirmation.

Successful evaluation yields a `ReachyAuthorizedProviderSwitch`. The authorization is bound to workload/source/target/reason and can be consumed once. ASR/TTS and semantic VLM selection helpers require this token before calling the underlying selection object.

## Privacy confirmation

A privacy confirmation is bound to policy name, workload, source/target provider IDs, source/target privacy boundaries, and reason code. It is one-use. A denied switch cannot be converted into an allowed switch merely by requesting confirmation.

## Retry and quality reduction

Same-provider retry and local quality reduction are separate decisions. They are disabled by default. Local quality reduction is only eligible for an `OnDevice` provider.

## Diagnostics

The engine retains at most 128 structured diagnostics containing only:

- workload/action;
- policy name;
- source and target provider IDs;
- reason code;
- decision code;
- timestamp.

No secret store, credential reference, API key, authorization header, request body, or provider response body is part of the diagnostic contract.

## Durable settings

RMA-146 uses a separate versioned file, `reachy-fallback-policies-v1.json`, rather than changing the existing general settings schema. The file contains exactly one named policy for each of ASR, TTS, LLM, and VLM. Invalid/incomplete files are quarantined and all workloads reset to `no-fallback`.

Policy updates are transactional with respect to persistence: if publishing the file fails, the in-memory engine policy is rolled back.

## Closure boundary

The source, static contracts, and managed behavioral fixture are implemented locally and checkpointed to GitHub. Formal closure still requires managed warnings-as-errors compilation and Unity script compilation of the exact source set.
