---
name: verify
description: Run the project's full local CI equivalent (static checks, native build/ctest, and managed dotnet tests) to confirm changes are good before considering work done. Use before finishing a task, before committing, or whenever asked to "verify", "run CI", or "check everything passes".
---

Run the repository's full CI script from the repo root:

```bash
./scripts/ci.sh
```

This runs, in order:
1. Static checks (no compilers required): `verify_toolchain.py --manifest-only`, `validate_scaffold.py`, `check_docs_links.py`, `validate_inventory.py`, `validate_model_parameter_audit.py`, `validate_reference_trace_lock.py`, `python3 -m compileall -q scripts`, `python3 -m unittest discover -s scripts/tests -v`.
2. Native build + ctest (`./scripts/build_native.sh`).
3. Managed `dotnet` tests (`managed/ReachyMini.Core.Tests/...`).

Report pass/fail for each stage. If a stage fails, show the relevant failing output (don't just say "it failed") and stop — do not proceed to later stages' fixes without diagnosing the failure first.

If only Python scripts under `scripts/` changed and a fast sanity check is wanted first (not a substitute for the full run before declaring work done), `./scripts/ci.sh --static-only` covers just the static phase with no compiler/SDK requirement.

Do not use warning suppressions, lint baselines, or pragma-disables to force a pass — per `docs/WARNING_POLICY.md`, warnings are treated as bugs and must be fixed, not silenced. Never modify third-party code to satisfy first-party checks.
