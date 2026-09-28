# RMA-190 deterministic failure acceptance status — 2026-09-28

## Scope

This note records the current review boundary for the deterministic failure acceptance lane:

- Branch: `ralph/rma190-deterministic-failure-acceptance`
- Inspected head: `ff5a0de1e16a110798e95f17a1dbbb44b17563d6`
- Current `master` preservation head when this note was added: `c3b0f8c23c843b58569eaab8436fc811c5f67b69`

The branch adds a dedicated deterministic failure workflow and managed acceptance code intended to cover network loss, rate limit, malformed response, cancellation, model corruption, model OOM/reload recovery, solver fault, and controlled reset.

## Transplant decision

This branch is not transplanted into current `master` as source in this pass because two managed acceptance files are redacted by Ralph output:

- `managed/ReachyMini.Core.Tests/Program.Rma190DeterministicFailureAcceptance.LocalModel.cs`
- `managed/ReachyMini.Core.Tests/Program.Rma190DeterministicFailureAcceptance.Network.cs`

The readable portions overlap existing preserved lanes:

- network/provider failure consolidation;
- model corruption/OOM consolidation;
- solver fault/reset consolidation.

Preserving only this boundary avoids reconstructing source from redacted data or duplicating acceptance semantics without exact file visibility.

## Remaining requirements

The RMA-190 deterministic failure scenarios remain open until their readable source is merged or otherwise preserved, exact-head CI or an equivalent configured validation path passes, and the authoritative TODO is reconciled with concrete evidence. No hidden fallback, provider-success, model-failure, or solver-recovery success is claimed by this status note.
