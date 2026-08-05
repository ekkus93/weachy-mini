# RMA-103 Valid-Coverage Policy Validation

**Milestone:** RMA-103  
**Date:** 2026-08-05  
**Status:** Implementation validation in progress

## Required evidence

The implementation commit must pass:

- managed warnings-as-errors contracts for exact coverage counting, threshold
  hysteresis, stale/identity rejection, continuity reset, and consumer metadata;
- the permanent `RMA-103 Valid Coverage Policy` workflow;
- hosted CI static, managed, native, Reachy-model, and Android jobs;
- Unity real-graphics EditMode and PlayMode tests;
- ARM64 API-26 IL2CPP build and verification;
- RMA-090, RMA-091, and RMA-092 physical camera acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance; and
- artifact and final commit-status publication.

This report must be updated with the exact accepted SHA, workflow run IDs, test
counts, device evidence, and artifact identities before RMA-103 is marked
complete in the authoritative TODO.
