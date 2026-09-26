# Ralph-loop CI infrastructure blocker — 2026-09-26

## Scope

This note records a repository qualification gate observed while working the authoritative roadmap in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.

It does **not** close any RMA checklist item and does **not** claim that any pending pull request is source-qualified.

## Observed signature

Current exact-head pull-request CI runs for multiple independent branches complete in a few seconds with all expected jobs marked failed before any steps are reported. Ralph Bridge failure inspection reports no failed-step excerpts and `JOB_NOT_FOUND` for every job log.

Representative exact heads observed during the 2026-09-26 loop:

- `762fd6a93dda65d660ae43abad3f2d53e8f256e0` — `ralph/rma002-two-environment-build-evidence`, CI run `36220620783`.
- `efa137b9d70fa9aef66baab23641980f15855c4f` — `ralph/rma190-consolidation-status`, CI run `36224000076`.
- `2274271211a7b7fb8b679f2ea895129a630c0020` — `ralph/rma191-security-review-contracts-20260925`, CI run `36224011857`.
- `9c13614f78849e2a2b469f0d4e9c8b6c7f2af945` — `ralph/rma192-license-attribution-review`, CI runs `36217867530`, `36217867557`, and `36217867369`.
- `925e00cf7c9a9273bcd1623bda92601459cf627a` — `ralph/rma194-deferred-feature-fidelity-gate`, CI run `36222945712`.
- `f45d0a8db45fa5d820aa4df6966f0d2d992f13b5` — `ralph/rma194-release-acceptance-status`, CI run `36224078717`.
- `42f2922e2884fdf40f95e3f9a808985ad0feadf7` — this documentation branch before this update, CI run `36229941010`; the documentation-only PR reproduced the same zero-step `JOB_NOT_FOUND` signature.

The current `master` head `b188ce3b18635d24da79cb762e245c52e361ab5e` also has a later workflow-run status publication failure (`36224599730`) with the same no-step/no-log evidence boundary.

## Classification

This signature is being classified as a GitHub Actions infrastructure, runner, log-retention, or workflow-visibility condition until a job log exposes an actionable repository failure.

A compiler error, test assertion, formatting failure, or static-analysis diagnostic would be source work and should be fixed on the affected branch. The observed evidence is different: jobs terminate before exposing any steps or logs, so there is no repository diagnostic to repair from this run data.

## Rerun gate

A Ralph Bridge attempt to rerun failed jobs for CI run `36224078717` was rejected with:

```text
WRITE_PERMISSION_REQUIRED: GitHub Actions write permission is required for this Phase 2 operation.
```

That means the loop cannot currently repair the condition by rerunning failed Actions jobs through Ralph Bridge.

## Required next action

Before any pending roadmap PR is merged on the basis of CI, exact-head qualification must be re-established by one of the following:

1. GitHub Actions begins producing normal job logs/steps again and an exact-head run passes for the PR being merged.
2. Ralph Bridge receives the required Actions write permission and successfully reruns failed jobs, followed by an exact-head pass.
3. An authorized equivalent exact-head qualification path is provided and recorded with concrete run/SHA evidence.

Until then, pending branches may be reviewed and kept ready, but their TODO checkboxes must remain open unless their own required evidence already exists independently of the failed CI runs.
