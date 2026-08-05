# RMA-104 Reprojection Test Suite Validation

**Milestone:** RMA-104

**Date:** 2026-08-05

**Status:** Implementation validation pending

## Intended evidence

The accepted implementation SHA must pass:

- the permanent RMA-104 managed and static contract workflow;
- hosted CI static, managed, native, sanitizer, Android, and pinned-model jobs;
- real-graphics Unity EditMode and PlayMode tests;
- ARM64 API-26 APK build and verification;
- RMA-090 camera discovery;
- RMA-091 CameraX acquisition;
- RMA-092 physical GPU texture acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance; and
- artifact and final commit-status publication.

The final report will record exact run, job, artifact, and digest evidence.
No milestone completion claim is valid until the exact implementation SHA
passes the complete chain.
