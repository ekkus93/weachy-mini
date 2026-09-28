# RMA-190 deterministic failure acceptance status — 2026-09-28

## Scope

This note records the current review boundary for the consolidated deterministic failure acceptance lane:

- Branch: `ralph/rma190-deterministic-failure-acceptance`
- Inspected head: `ff5a0de1e16a110798e95f17a1dbbb44b17563d6`
- Compared against `master`: `9e288b01de140e8d36adacd0bff9c90ec960fb44`

The branch adds a dedicated deterministic failure workflow and managed acceptance code intended to cover network loss, rate limit, malformed response, cancellation, model corruption, model OOM/reload recovery, solver fault, and controlled reset.

## Transplant decision

This branch was not transplanted into the release-reconciliation PR as source in this pass because two managed acceptance files are redacted by Ralph output:

- `managed/ReachyMini.Core.Tests/Program.Rma190DeterministicFailureAcceptance.LocalModel.cs`
- `managed/ReachyMini.Core.Tests/Program.Rma190DeterministicFailureAcceptance.Network.cs`

The readable portions overlap existing preserved lanes:

- network/provider failure consolidation;
- model corruption/OOM consolidation;
- solver fault/reset consolidation.

Preserving only this boundary avoids reconstructing source from redacted data or duplicating acceptance semantics without exact file visibility.

## Remaining requirements

The RMA-190 deterministic failure scenarios remain open until their readable source is merged or otherwise preserved, exact-head CI or an equivalent configured validation path passes, and the authoritative TODO is reconciled with concrete evidence. No hidden fallback, provider-success, model-failure, or solver-recovery success is claimed by this status note.
