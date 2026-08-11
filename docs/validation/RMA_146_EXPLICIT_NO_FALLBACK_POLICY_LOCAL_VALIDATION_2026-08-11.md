# RMA-146 Local Validation — 2026-08-11

## Local checks

Passed after the final source review:

- `python3 scripts/tests/test_rma146_no_fallback_policy.py` — 10/10
- `python3 scripts/tests/test_rma146_fallback_policy_persistence.py` — 6/6
- `python3 scripts/tests/test_provider_source_set_integrity.py` — 5/5
- Python `compileall` over `scripts/tests` — pass
- `git diff --check` — pass

The sandbox has no local .NET/Unity compiler, so managed/Unity compilation is not claimed here.

## Behavioral fixture

`managed/ReachyMini.Core.Tests/Rma146ProviderFallbackPolicyContractTests.cs` is a module-initializer contract intended for the managed build. It verifies:

- a mock ASR failure under the default policy leaves provider selection unchanged;
- an explicitly allowlisted same-boundary fallback obtains and consumes a one-time authorization;
- token reuse fails;
- an on-device → cloud TTS switch requires matching privacy confirmation;
- privacy confirmation is one-use;
- ASR/TTS/LLM/VLM policies remain independent;
- retry and local quality reduction remain disabled until explicitly enabled.

## Source-set integrity

The repository source-set guard now requires all RMA-146 components:

- `ReachyProviderFallbackPolicyContracts.cs`
- `ReachyProviderFallbackPolicyEngine.cs`
- `ReachyAuthorizedProviderSelectionExtensions.cs`
- `ReachyFallbackPolicyPersistence.cs`

This specifically prevents a repeat of the partial-source checkpoint failure that previously left application consumers without their provider contract.

## Remaining external gates

- managed warnings-as-errors compile;
- Unity script compilation/package validation.
