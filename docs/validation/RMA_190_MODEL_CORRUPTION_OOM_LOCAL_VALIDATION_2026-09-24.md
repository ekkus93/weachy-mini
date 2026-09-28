# RMA-190 Model Corruption and OOM Local Validation — 2026-09-24

## Scope

This note records local source-contract validation for the `ralph/rma190-model-corruption-oom` branch. It is not Android physical evidence, local-model benchmark evidence, memory/thermal performance evidence, or roadmap checkbox closure.

The branch covers deterministic RMA-190 model-corruption and out-of-memory failure evidence while preserving fail-closed local-model behavior.

## Local validation

Executed from the uploaded current-master checkout with the branch checked out:

```text
python3 scripts/tests/test_rma190_model_corruption_oom.py
# exit 0
```

The local contract verifies the branch-level source and documentation requirements that can be checked without hosted CI, Unity, Android, or a real installed GGUF model.

## Hosted qualification status

GitHub Actions for this repository is currently failing jobs before any step starts, with empty step lists and unavailable logs. That failure mode is treated as CI infrastructure, not source failure.

This branch still requires exact-head hosted qualification and any required Android/device evidence before merge or TODO reconciliation.
