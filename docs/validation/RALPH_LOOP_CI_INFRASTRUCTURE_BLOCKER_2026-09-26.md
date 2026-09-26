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
- `42f2922e2884fdf40f95e3f9a808985ad0feadf7` — this documentation branch before the prior update, CI run `36229941010`; the documentation-only PR reproduced the same zero-step `JOB_NOT_FOUND` signature.
- `4117261432e0f4243f5f603ee2b18f65d32b64b5` — this documentation branch before the previous update, CI run `36230046936`; the second documentation-only CI run again reproduced five zero-step `JOB_NOT_FOUND` jobs.
- `560f2a313450e9570e9fede518098323eb702606` — this documentation branch before this update, CI run `36230946592`; the third documentation-only CI run again reproduced five zero-step `JOB_NOT_FOUND` jobs.
- `fab7ba35cdeee3d021fd773fa8b1ecf3c04d30c9` — this documentation branch immediately before the current reconciliation, CI run `36231328047`; the generic CI failure persisted.
- `3ecd11c27b99fd0d2104edaeaf6005e1988df9ba` — `ralph/rma194-release-acceptance-status`, CI run `36243960610`; a later documentation/status-only head still reproduced the hosted-CI failure.
- `d09e84580c2cb4e276147eb8fcb3b40e58babff2` — this documentation branch after reconciling the passing A54 evidence, CI run `36247080879`; the fresh central blocker update itself still reproduced the hosted-CI failure.

The current `master` head `b188ce3b18635d24da79cb762e245c52e361ab5e` also has a later workflow-run status publication failure (`36224599730`) with the same no-step/no-log evidence boundary.

## A54 device status update

The A54 is connected to the self-hosted-runner server, so physical-device availability is no longer an active blocker for A54-dependent lanes.

The RMA-190 permission lane subsequently obtained real physical evidence. `ralph/rma190-permission-end-to-end` passed `A54 RMA-190 Permission End-to-End` run `36233165870` at head `7072a5043d4632398d02174b6fdc8525fd8fbeaa`, then passed exact-head requalification run `36235582188` at evidence-updated head `80e37b0dbf35a6684f8b6eecc0a5dcde719e62d2`. The latter completed APK build/verification, physical A54 pinning, and permission denial/revocation acceptance successfully.

The generic PR CI for that same exact head still failed independently as run `36235584403` with five zero-step jobs and unavailable `JOB_NOT_FOUND` logs. This separation is useful evidence that the A54/self-hosted physical lane is operational while the generic hosted CI gate remains unhealthy.

Other A54-dependent branches still require their own scenario-specific exact-head evidence; the permission result must not be generalized to unrelated physical acceptance criteria.

## Classification

This signature is being classified as a GitHub Actions infrastructure, runner, log-retention, or workflow-visibility condition until a job log exposes an actionable repository failure.

A compiler error, test assertion, formatting failure, or static-analysis diagnostic would be source work and should be fixed on the affected branch. The observed evidence is different: jobs terminate before exposing any steps or logs, so there is no repository diagnostic to repair from this run data.

## Rerun gate

A Ralph Bridge attempt to rerun failed jobs for CI run `36224078717` was rejected with:

```text
WRITE_PERMISSION_REQUIRED: GitHub Actions write permission is required for this Phase 2 operation.
```

Later rerun attempts for the self-reproduced documentation-only CI run `36230046936`, the now-A54-unblocked permission branch run `36201301960`, and the refreshed RMA-194 status branch run `36243960610` returned the same `WRITE_PERMISSION_REQUIRED` error. That means the loop cannot currently repair the condition by rerunning failed Actions jobs through Ralph Bridge.

## Local validation gate

Ralph Bridge local validation was also probed on exact head `4117261432e0f4243f5f603ee2b18f65d32b64b5` with the `static` profile and was rejected with:

```text
LOCAL_RUNNER_DISABLED: Local validation is disabled by Ralph Bridge policy.
```

A later local-validation probe on the RMA-193 documentation branch head `e0a9e7c215128df7e70883817afd7fb9bede4a5f` with the same `static` profile returned the same `LOCAL_RUNNER_DISABLED` policy error.

This means local Ralph validation is not currently available as a substitute qualification path for the affected branches.

## Required next action

Before any pending roadmap PR is merged on the basis of CI, exact-head qualification must be re-established by one of the following:

1. GitHub Actions begins producing normal job logs/steps again and an exact-head run passes for the PR being merged.
2. Ralph Bridge receives the required Actions write permission and successfully reruns failed jobs, followed by an exact-head pass.
3. Ralph Bridge local validation is enabled with an approved repository profile that covers the affected TODO acceptance criteria, and the exact branch head passes that profile.
4. An authorized equivalent exact-head qualification path is provided and recorded with concrete run/SHA evidence.

Until then, pending branches may be reviewed and kept ready, but their TODO checkboxes must remain open unless their own required evidence already exists independently of the failed CI runs.
