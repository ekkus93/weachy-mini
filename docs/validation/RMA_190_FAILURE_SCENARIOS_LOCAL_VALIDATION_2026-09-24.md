# RMA-190 Failure Scenarios Local Validation — 2026-09-24

## Scope

This note records local validation for the `ralph/rma190-failure-scenarios` branch at commit `f8dd380e9108950ddaceece8dd30b7d068536ad4`.

The branch adds deterministic failure-scenario coverage for RMA-190 production recovery paths, including permission/provider/network/model/solver-style failure handling contracts. This record is source/local qualification evidence only. It is not physical Android acceptance evidence and does not close any RMA-190 roadmap checkbox by itself.

## Local validation performed

From the uploaded repository checkout corresponding to current `master` plus the `ralph/rma190-failure-scenarios` branch:

```text
python3 scripts/tests/test_rma190_failure_scenarios.py
# exit 0
```

The native managed-contract build also completed successfully:

```text
cmake -S . -B build/rma190-failure-scenarios-local -G Ninja -DBUILD_TESTING=ON
cmake --build build/rma190-failure-scenarios-local --target reachy_sim_managed_contract --parallel
# exit 0
```

## Hosted CI status

At validation time, GitHub Actions for this repository is failing jobs before any step starts, with zero job steps and unavailable logs. That condition is treated as CI infrastructure failure rather than source failure.

The branch remains subject to exact-head hosted CI/device qualification before merge. Any TODO checkbox that requires physical Android evidence must remain open until the corresponding exact-head run produces the required machine-readable evidence.
