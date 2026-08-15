# RMA-172 Diagnostic Bundle Export — Local Validation

**Date:** 2026-08-14
**Scope:** RMA-172 implementation on top of the locally integrated RMA-163/RMA-170/RMA-171 tree.

## Validation performed

### Focused diagnostics/security regression

Command:

```bash
python3 -m unittest \
  scripts.tests.test_rma163_import_security \
  scripts.tests.test_rma170_structured_logging \
  scripts.tests.test_rma171_diagnostics_screen \
  scripts.tests.test_rma172_diagnostic_bundle_export \
  -v
```

Result: **23/23 passed**.

This covers the imported-content diagnostic admission gate, structured-log redaction and
rate limiting, typed diagnostics-screen behavior, bundle bounds/atomicity, redacted-only
selection, manifest policy, retained-log buffering, and UI/application export wiring.

### Complete Python static regression suite

Command:

```bash
PYTHONPATH=. python3 -m pytest -q scripts/tests
```

Result: **361/361 passed**.

### Repository canonical static gate

Command:

```bash
bash scripts/ci.sh --static-only
```

Result: **exit 0**. The canonical unittest discovery reported **346/346 passed** after the
toolchain-manifest, scaffold, documentation-link, inventory, model-parameter-audit,
reference-trace-lock, and Python compile checks passed.

The line

```text
Electrical baseline generation failed: generated electrical baseline header is stale
```

is emitted by an intentional negative regression fixture; the enclosing test and canonical
static gate both return success.

### Diff hygiene

`git diff --check` passes.

## Managed/Unity execution limitation

This sandbox does not provide `dotnet`, `csc`, `mcs`, or a Unity Editor executable, so the
new managed RMA-172 ZIP/buffer contract tests are committed but are **not claimed as
executed locally**. The repository static checks validate their registration/source wiring;
managed/Unity execution remains for the normal toolchain/CI or developer environment.

## Acceptance mapping

- Version/configuration: exported from the typed Providers, Versions, and Device sections.
- Performance/health: exported from Simulation, Rendering, and Camera sections with
  availability/degradation reasons preserved.
- Redacted logs: bounded RMA-170 records are retained in memory and re-redacted during
  archive creation; overwritten-record counts are visible in the manifest.
- Sensitive content: production selection is `RedactedOnly`; private text, raw media, and
  credential selections fail closed and no raw settings/transcript/media source is wired.
- Manifest: records schema/format, user selection, redaction policy, default exclusions,
  denied RMA-170 data classes, log counts, entry byte counts, classifications, and SHA-256
  digests.
- Storage: the application uses its controlled persistent diagnostics directory; export is
  temporary-file + atomic move and refuses overwrite.

No physical-device acceptance is required to validate the RMA-172 core export contract.
The Android UI action and archive creation should still be exercised in the normal Unity
Android validation environment before release acceptance.
