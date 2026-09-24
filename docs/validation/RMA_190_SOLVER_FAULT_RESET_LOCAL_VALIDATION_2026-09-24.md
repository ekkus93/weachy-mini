# RMA-190 Solver Fault Reset Local Validation — 2026-09-24

## Scope

This note records local source-contract validation for the `ralph/rma190-solver-fault-reset` branch. It is not Android physical evidence, native runtime stress evidence, or roadmap checkbox closure.

The branch covers deterministic solver-fault and controlled-reset evidence for RMA-190 while preserving fail-closed runtime recovery behavior.

## Local validation

Executed from the uploaded current-master checkout with the branch checked out:

```text
python3 scripts/tests/test_rma190_solver_fault_reset.py
# exit 0
```

The local contract verifies branch-level source and documentation requirements that can be checked without hosted CI, Unity, Android, or a physical device.

## Hosted qualification status

GitHub Actions for this repository is currently failing jobs before any step starts, with empty step lists and unavailable logs. That failure mode is treated as CI infrastructure, not source failure.

This branch still requires exact-head hosted qualification and any required native/Android evidence before merge or TODO reconciliation.
