# RMA-190 consolidation permission-lane update — 2026-09-25

## Scope

This update corrects the permission-denial/revocation row in
`docs/RMA_190_CONSOLIDATION_STATUS_2026-09-24.md` after PR #84 was opened and after PR #53 obtained passing physical A54 evidence.
The original consolidation document said `ralph/rma190-permission-denial-revocation-e2e`
had no open PR. That was true at the time of the earlier audit but is no longer current.

## Current permission lane status

- PR #53, `ralph/rma190-permission-end-to-end`, is the current physically qualified permission-denial/revocation evidence lane. It passed `A54 RMA-190 Permission End-to-End` at source/evidence head `7072a5043d4632398d02174b6fdc8525fd8fbeaa` in run `36233165870`, then passed exact-head requalification at `80e37b0dbf35a6684f8b6eecc0a5dcde719e62d2` in run `36235582188`.
- PR #53's generic PR CI still failed independently at exact head `80e37b0dbf35a6684f8b6eecc0a5dcde719e62d2` in run `36235584403` with the repository-wide zero-step/`JOB_NOT_FOUND` infrastructure signature. That is not source evidence, and it remains the merge gate blocker.
- PR #84 tracks `ralph/rma190-permission-denial-revocation-e2e` at
  `fd1443a9e3485dadad01f64bb104dab701a2e3dd` and still represents a separate source-contract lane.
- Neither branch should be discarded until PR #53 and PR #84 have been compared
  and any unique source-contract coverage has been transplanted or explicitly preserved.
- PR #84's first generic PR CI attempt also failed with the repository-wide
  zero-step/`JOB_NOT_FOUND` infrastructure signature. That is not source evidence.

## Closure boundary

The permission-denial/revocation RMA-190 checkbox remains open until one canonical
lane has clean exact-head merge-gate CI plus the required physical Android permission-denial and
revocation evidence merged to `master` and reconciled in the authoritative TODO.
