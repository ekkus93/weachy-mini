# RMA-170 Local Validation — 2026-08-14

## Result

RMA-170 is locally validated in the sandbox checkout.

- `python3 scripts/tests/test_rma170_structured_logging.py`: 6/6 passed.
- Combined RMA-170/RMA-171 focused unittest set: 12/12 passed.
- `PYTHONPATH=. python3 -m pytest -q scripts/tests`: 350/350 passed after the
  pre-existing RMA-134 split-source harness was repaired.
- Python static tests compile with `python3 -m compileall -q scripts/tests`.
- Changed diagnostics sources contain no trailing whitespace in the local check.

The sandbox has no `dotnet`, `csc`, `mcs`, `msbuild`, or Unity editor executable,
so the managed and Unity test fixtures are included but are not claimed as locally
executed. No physical Android behavior was added by RMA-170.

## Covered invariants

The permanent contracts cover stable identity/correlation, monotonic timing,
default redaction, URL/header secret handling, default-deny bundle data classes,
first/final rate-limit visibility, discriminator-preserving bursts, deterministic
JSON serialization, provider HTTP categorization, and use of the structured Unity
sink by the high-risk ordinary runtime paths.
