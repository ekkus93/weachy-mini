# RMA-190 solver fault/reset consolidation — 2026-09-24

## Scope

This record documents preservation of the focused solver-fault source-contract lane and
the controlled-reset managed/native test-harness lane on current `master` after the earlier
branch/PR lanes became stale.

The current `master` transplant preserves the fully readable managed/native coverage from:

- `ralph/rma190-solver-fault-reset`
- `ralph/rma190-solver-fault-reset-e2e`

The production-runtime recovery lane in `ralph/rma190-solver-recovery` was audited
separately because it modifies shipping runtime behavior rather than only managed/native
test harnesses. It is intentionally not transplanted by this record.

## Coverage now present on master

Current `master` now contains:

- `scripts/tests/test_rma190_solver_fault_reset.py`
- `managed/ReachyMini.Core.Tests/Program.Rma190SolverFaultControlledResetTests.cs`
- `managed/ReachyMini.Core.Tests/Rma032NativeTestControls.cs`
- `native/reachy_sim/tests/reachy_sim_blocking_backend.h`
- `native/reachy_sim/tests/reachy_sim_fake_backend.c`
- `scripts/tests/test_rma190_solver_fault_controlled_reset.py`

The transplanted coverage injects a one-shot native numeric solver failure, verifies the
managed worker reaches a faulted fail-closed state, rejects a hidden in-place reset, then
requires explicit handle recreation followed by an explicit neutral reset before published
authoritative progress resumes.

## Remaining closure requirements

This record still does **not** close the RMA-190 `Solver fault and controlled reset`
checkbox by itself. Closure still requires exact-head CI and required native/runtime
recovery evidence after GitHub Actions stops failing before job steps/logs are available.
