# RMA-173 silent-failure regression validation — 2026-09-30

## Scope

This report records exact-head repository regression evidence for all eight RMA-173 fail-closed obligations. It does not replace physical-device acceptance owned by RMA-125, RMA-135, RMA-184, or remaining RMA-190 scenarios.

## Accepted source head

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `4a88c17ef62c95642e2b69dd2c6f6a1f34b5b459`
- Workflow: `RMA-173 Silent Failure Regressions`
- Run: `36705968320`
- Result: **success**

The same exact source head also passed `RMA Release Review Contracts` run `36705968307` and the A54-backed `Local Unity Android Device Validation No Artifacts` run `36705968304`.

## Proven RMA-173 obligations

The successful RMA-173 workflow executes one named regression step for each required silent-failure class and passed all eight:

1. on-device ASR failure cannot silently activate cloud ASR;
2. offline TTS failure cannot silently activate a network voice;
3. local LLM failure cannot silently activate OpenAI;
4. invalid camera content cannot silently reuse stale pixels;
5. solver failure cannot silently switch motion authority to Unity animation;
6. calibration failure remains bound to the production calibration path and cannot use a Unity calibration-fallback animation path;
7. dropped command/state buffers remain visible through overflow/drop plus fault/diagnostic contracts;
8. malformed provider responses remain parser/validation failures and cannot fabricate a successful behavior intent.

The permanent regression source is `scripts/tests/test_rma173_silent_failure.py`; the permanent gate is `.github/workflows/rma173-silent-failure-regressions.yml`.

## Qualification boundary

RMA-173 is a repository-wide no-silent-fallback contract. Detailed calibration correctness/provenance remains governed by RMA-100/RMA-104; provider physical behavior remains governed by the corresponding provider/device gates. Passing RMA-173 does not fabricate physical speech, thermal, performance, or calibration measurements.

## Release-review relationship

Exact-head `RMA Release Review Contracts` run `36705968307` passed on the same SHA. The existing `docs/validation/RMA_191_193_RELEASE_REVIEW_VALIDATION_2026-09-29.md` records the systematic RMA-191/RMA-192/RMA-193 review boundary. Those reviews are independent of device-only acceptance and can be reconciled in the authoritative TODO without closing unrelated RMA-125/RMA-135/RMA-184/RMA-190 gates.
