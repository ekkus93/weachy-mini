# RMA-171 Local Validation — 2026-08-14

## Result

- `python3 scripts/tests/test_rma171_diagnostics_screen.py`: 6/6 passed.
- Combined RMA-170/RMA-171 focused unittest set: 12/12 passed.
- `PYTHONPATH=. python3 -m pytest -q scripts/tests`: 350/350 passed.
- The RMA-134 split-provider static harness was repaired as a baseline prerequisite;
  no RMA-171 behavior depended on the obsolete monolithic source path.

Managed contract and Unity Editor fixtures are present for typed availability,
section severity propagation, display text, legacy binding compatibility, and
main-screen visibility. They are not claimed as locally executed because the
sandbox has no .NET or Unity toolchain.

## Fail-visible checks

The screen explicitly labels missing production reprojection timing and homography
coverage telemetry as unavailable. Android thermal telemetry outside an Android
player is also explicit. Runtime/service faults are represented by state/counts;
raw exception/private text is not copied into the diagnostics screen.
