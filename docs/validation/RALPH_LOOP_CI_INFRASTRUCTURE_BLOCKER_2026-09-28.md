# Ralph-loop CI infrastructure blocker — 2026-09-28

## Scope

This note records the repository qualification gate observed while working the authoritative roadmap in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` through Ralph Bridge.

It does **not** close any RMA checklist item and does **not** claim that any pending pull request is source-qualified.

## Current repository baseline

- Current `master`: `9e288b01de140e8d36adacd0bff9c90ec960fb44`.
- Consolidated release-reconciliation PR: #91, branch `ralph/rma-release-reconciliation-20260927`.
- Latest PR #91 head before this blocker note: `dd68f842464f783bf0a7fb286735eabc73149153`.

## Observed hosted-CI signature

Exact-head pull-request CI runs for multiple independent branches complete in a few seconds with all expected jobs marked failed before any steps are reported. Ralph Bridge failure inspection reports no failed-step excerpts and `JOB_NOT_FOUND` for every job log.

Representative runs include:

- PR #91 head `e5d4fc7754e7b827652291934df81b930fac7852`, CI run `36373469948`.
- PR #91 head `8f523cb8c6260c46136a209eb0af0fc75b171bca`, CI run `36373601513`.
- PR #91 head `dd68f842464f783bf0a7fb286735eabc73149153`, CI run `36373752292`.
- Prior RMA-002 head `762fd6a93dda65d660ae43abad3f2d53e8f256e0`, CI run `36220620783`.
- Prior RMA-191 security-contract head `2274271211a7b7fb8b679f2ea895129a630c0020`, CI run `36224011857`.
- Prior RMA-192 license-attribution head `9c13614f78849e2a2b469f0d4e9c8b6c7f2af945`, CI runs `36217867530`, `36217867557`, and `36217867369`.
- Prior RMA-194 deferred-feature/fidelity head `925e00cf7c9a9273bcd1623bda92601459cf627a`, CI run `36222945712`.

The signature is materially different from a source failure: jobs expose no steps, no assertion output, no compiler diagnostics, no lint diagnostics, and no build logs.

## Classification

Until a run exposes a normal job log with an actionable repository diagnostic, this condition is classified as GitHub Actions, runner, log-retention, workflow-visibility, or repository Actions-permission infrastructure failure.

A compiler error, test assertion, formatting failure, static-analysis diagnostic, merge conflict, or source-level contract failure must still be fixed on the affected branch. The current evidence does not show such a diagnostic.

## Ralph Bridge remediation gates

The loop cannot currently repair the hosted-CI condition through available Ralph operations:

- Failed-job rerun attempts are blocked by Actions write permission (`WRITE_PERMISSION_REQUIRED`).
- Local validation using the `static` profile is blocked by Ralph policy (`LOCAL_RUNNER_DISABLED`).
- Dispatch validation using the `static` profile is not available (`VALIDATION_PROFILE_NOT_FOUND`).
- Guarded merge of PR #91 is rejected by the clean-mergeability gate while CI remains failed.

## Required requalification before merge/TODO closure

Before any pending roadmap PR is merged on CI grounds, exact-head qualification must be re-established by one of these concrete paths:

1. GitHub Actions begins producing normal job logs/steps again and an exact-head run passes for the PR being merged.
2. Ralph Bridge receives the required Actions write permission and successfully reruns failed jobs, followed by an exact-head pass.
3. Ralph Bridge local validation is enabled with an approved repository profile that covers the affected TODO acceptance criteria, and the exact branch head passes that profile.
4. Ralph Bridge dispatch validation is configured with an approved repository profile that covers the affected TODO acceptance criteria, and the exact branch head passes that profile.
5. An authorized equivalent exact-head qualification path is provided and recorded with concrete run/SHA evidence.

Until then, pending branches may be reviewed, repaired, deduplicated, and kept ready, but authoritative TODO checkboxes must remain open unless their own required non-CI evidence already exists independently and the relevant reconciliation has merged to current `master`.
