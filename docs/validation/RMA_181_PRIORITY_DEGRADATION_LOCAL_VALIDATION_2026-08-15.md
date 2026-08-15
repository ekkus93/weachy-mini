# RMA-181 Priority Degradation — Local Validation

**Date:** 2026-08-15  
**Scope:** Local/static validation of RMA-181 implementation

## Implemented coverage

RMA-181 adds a deterministic degradation ladder with immediate escalation and
three-sample recovery hysteresis. Real existing subsystem classes implement the
policy target contract for local LLM governance, VLM scheduling/cancellation,
and lightweight tracking. A Unity target controls presentation frame rate and
optional visual effects. The runtime bridge consumes the existing RMA-135
resource and physics-budget sources.

The managed test sources exercise:

- render -> camera -> VLM -> LLM degradation order;
- fixed physics timestep and no step-skipping permission at every level;
- audio preservation at every level;
- VLM cancellation and `ResourceSuspended` admission;
- local LLM minimum-mode enforcement and recovery;
- tracking staging at 480 px under `CameraReduced`;
- explicit analysis throttling with no pixel staging, detector invocation, or
  stale-result reuse for a too-soon frame.

## Local commands

The focused static contract is:

```bash
python3 -m unittest scripts.tests.test_rma181_priority_degradation -v
```

The repository-wide static commands are:

```bash
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
TERM=xterm bash scripts/ci.sh --static-only
git diff --check
```

Observed results:

- focused RMA-181 static contracts: **6/6 passed**;
- complete `scripts/tests` discovery: **335/335 passed**;
- `TERM=xterm bash scripts/ci.sh --static-only`: **exit 0**, including the same
  335 Python contracts plus the repository's scaffold, documentation, inventory,
  parameter-audit, and trace-lock checks;
- `git diff --check`: clean.

The static suite intentionally exercises stale generated-header rejection in
some negative-path tests; those expected diagnostic lines do not represent a
failed static gate (the wrapper exited zero).

## Environment limitations

This sandbox does not provide the .NET SDK or Unity Editor, so the managed
projects and Unity assemblies cannot be compiled locally here. The changed
managed contract sources are retained for hosted CI to compile and execute.
No physical Android device run is claimed by this document.

The render, thermal, memory, and cadence thresholds in RMA-181 are engineering
policy defaults. They are not presented as calibrated device limits.
Representative low/mid/high Android measurements and published device profiles
remain owned by RMA-184.
