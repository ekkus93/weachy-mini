# RMA-092 Repository Cleanup — 2026-08-04

## Purpose

This note records the final repository cleanup performed after the RMA-092 GPU
texture bridge implementation and physical-device validation were complete.

## Removed scaffolding

Commit `21f067c827c63ea1dbb19c0827e1ae0a5fbfc2eb` removed both temporary integration
artifacts:

- `.github/workflows/rma092-core-integration-apply.yml`;
- `scripts/apply_rma092_core_integration.py`.

The workflow was a write-enabled, apply-once mechanism used to integrate the
bounded CameraX texture changes. Retaining it after completion would have left a
manual path capable of attempting to reapply obsolete source transformations to
`master`. Its companion Python patch script was likewise no longer part of the
runtime, test, or permanent validation design.

## CI issue that exposed the cleanup gap

Repository CI run `30954096616` on commit
`7b7f9d0a99704d1941568d47b538451871806971` failed Ruff lint because the obsolete
Python patch script contained three overlong embedded source lines. Native,
managed, and Reachy-model jobs passed; the failure was confined to static lint.

The correct repair was removal of completed integration scaffolding, not
formatting unused patch machinery. The permanent RMA-092 contracts remain in:

- `.github/workflows/rma091-camera-acquisition.yml`;
- `.github/workflows/local-unity-android-validation.yml`;
- `scripts/run_rma092_camera_texture_acceptance_android.sh`;
- the production Java, JNI, Unity, and shader sources;
- the RMA-092 architecture and validation documents.

## Validation boundary

The validated implementation remains commit
`21cdff23da91fd53bdd81b689f93d78e395d7c99`, with hosted run `30952901855` and
self-hosted run `30952901895`. This cleanup note is committed through the normal
user-authorized repository path so permanent hosted and self-hosted workflows
validate the final clean repository tree.
