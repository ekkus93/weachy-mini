# RMA-190 Model Corruption and OOM Local Validation — 2026-09-24

## Scope

This note records local source-contract validation reported for the `ralph/rma190-model-corruption-oom` branch. It is not Android physical evidence, local-model benchmark evidence, memory/thermal performance evidence, or roadmap checkbox closure.

The branch covers deterministic RMA-190 model-corruption and out-of-memory failure evidence while preserving fail-closed local-model behavior.

## Local validation reported by the inspected lane

Executed from the uploaded current-master checkout with the branch checked out:

```text
python3 scripts/tests/test_rma190_model_corruption_oom.py
# exit 0
```

The local contract verifies branch-level source and documentation requirements that can be checked without hosted CI, Unity, Android, or a real installed GGUF model.

## Current-master boundary

The model-failure source itself was not transplanted into current `master` in this reconciliation commit because at least one managed source file was returned by Ralph as a redacted URL-bearing record during review. Current `master` therefore retains this as evidence-boundary documentation only.

## Hosted qualification status

GitHub Actions for this repository has repeatedly failed jobs before any step starts, with empty step lists and unavailable logs. That failure mode is treated as CI infrastructure, not source failure.

This RMA-190 row still requires readable-source preservation, exact-head hosted qualification, and any required Android/device/model evidence before TODO reconciliation.
