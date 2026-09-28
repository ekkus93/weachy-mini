# RMA-190 Permission Denial/Revocation A54 Validation — 2026-09-26

## Scope

This document records the passing physical Android acceptance evidence for the RMA-190 permission denial/revocation scenario on the Samsung A54.

This is a physical Android validation record for the evidence branch. It does not by itself merge the branch or update the authoritative `master` checklist.

## Validated run

- Repository: `ekkus93/weachy-mini`
- Branch: `ralph/rma190-permission-end-to-end`
- Validated branch head: `7072a5043d4632398d02174b6fdc8525fd8fbeaa`
- Workflow: `A54 RMA-190 Permission End-to-End`
- Run ID: `36233165870`
- Job ID: `108380029889`
- Job: `RMA-190 permission denial/revocation on A54`
- Result: `success`
- Device serial: `R5CW31AX4FL`
- Device manufacturer/model: `samsung` / `SM-A546E`
- Android SDK: `36`
- ABI: `arm64-v8a`

## Passing path

The successful run completed:

1. source-contract validation for the permission acceptance lane;
2. Unity and Android installation resolution;
3. generated Reachy Unity presentation preparation;
4. production MuJoCo runtime staging;
5. ARM64 API-26 device APK build;
6. APK existence verification;
7. physical A54 pinning;
8. the RMA-190 permission denial/revocation acceptance script.

## Acceptance claims

The emitted report records:

- `permission_requested_on_startup: false`
- `initial_access_denied_by_absent_permission: true`
- `not_requested_observed: true`
- `granted_state_observed: true`
- `revoked_state_observed: true`
- `camera_inventory_hidden_without_permission: true`
- `real_camera_inventory_observed_when_granted: true`
- `final_android_permission_granted: false`
- `application_evidence_transport: application_persistent_json`
- `synthetic_camera_inventory_used: false`
- `physical_reachy_required: false`
- `user_clicked_deny: false`
- `status: Passed`

The nested RMA-090 camera discovery evidence in the same job records a fresh physical camera permission cycle with real A54 camera inventory: rear count `2`, front count `2`, and no fabricated intrinsics claim (`intrinsics_count: 0`, `CalibrationFallbackRequired`).

## Evidence boundaries

This run closes the physical evidence requirement for the RMA-190 permission denial/revocation scenario on the evidence branch.

It does not close unrelated RMA-190 scenarios, RMA-125 human-utterance offline conversation acceptance, RMA-135 local-LLM/physics thermal acceptance, RMA-191 security/privacy review, RMA-192 license review, RMA-193 documentation, or the broader RMA-194 release checklist.

It also does not override the repository clean-CI merge gate. The generic PR CI run for the same branch head still failed with the zero-step/no-log `JOB_NOT_FOUND` infrastructure signature. The branch should remain open and ready until generic CI/mergeability is restored or an explicitly authorized equivalent merge qualification is recorded.
