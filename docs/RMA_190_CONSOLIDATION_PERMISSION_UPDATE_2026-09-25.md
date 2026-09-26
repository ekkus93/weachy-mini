# RMA-190 consolidation permission-lane update — 2026-09-25

## Scope

This update corrects the permission-denial/revocation row in
`docs/RMA_190_CONSOLIDATION_STATUS_2026-09-24.md` after PR #84 was opened.
The original consolidation document said `ralph/rma190-permission-denial-revocation-e2e`
had no open PR. That was true at the time of the earlier audit but is no longer current.

## Current permission lane status

- PR #84 now tracks `ralph/rma190-permission-denial-revocation-e2e` at
  `fd1443a9e3485dadad01f64bb104dab701a2e3dd`.
- PR #53, `ralph/rma190-permission-end-to-end`, still contains supporting
  focused source-contract coverage and local validation history.
- Neither branch should be discarded until PR #53 and PR #84 have been compared
  and any unique source-contract coverage has been transplanted or explicitly preserved.
- PR #84's first generic PR CI attempt failed with the repository-wide
  zero-step/`JOB_NOT_FOUND` infrastructure signature. That is not source evidence.

## Closure boundary

The permission-denial/revocation RMA-190 checkbox remains open until one canonical
lane has exact-head CI plus the required physical Android permission-denial and
revocation evidence merged to `master`.
