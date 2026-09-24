# RMA-190 solver fault/reset consolidation — 2026-09-24

## Scope

This record documents consolidation of the focused solver-fault source-contract lane and
the controlled-reset managed/native test-harness lane onto PR #69:

- branch: `ralph/rma190-solver-fault-reset`
- consolidated head before this note: `79a321d1b91cc1fc45c691f2fc33d0f75ad223ab`

The consolidation preserves the existing PR #69 focused source contract and transplants
unique coverage from:

- `ralph/rma190-solver-fault-reset-e2e`

The older branch should not be closed or deleted until this consolidated PR #69 lane is
either merged or its unique changes are otherwise preserved.

## Coverage now present on PR #69

PR #69 now contains:

- `scripts/tests/test_rma190_solver_fault_reset.py`
- `managed/ReachyMini.Core.Tests/Program.Rma190SolverFaultControlledResetTests.cs`
- `managed/ReachyMini.Core.Tests/Rma032NativeTestControls.cs`
- `native/reachy_sim/tests/reachy_sim_blocking_backend.h`
- `native/reachy_sim/tests/reachy_sim_fake_backend.c`
- `scripts/tests/test_rma190_solver_fault_controlled_reset.py`

The transplanted coverage injects a one-shot native numeric solver failure, verifies the
managed worker reaches a faulted fail-closed state, rejects a hidden in-place reset, then
requires explicit handle recreation followed by an explicit neutral reset before
published authoritative progress resumes.

## Remaining related lane

`ralph/rma190-solver-recovery` modifies shipping production runtime code and still needs
a separate production-path audit before transplant. It is intentionally not merged into
this branch by this consolidation note.

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Solver fault and controlled reset`
checkbox by itself. Closure still requires exact-head CI and required native/runtime
recovery evidence after GitHub Actions stops failing before job steps/logs are available.
