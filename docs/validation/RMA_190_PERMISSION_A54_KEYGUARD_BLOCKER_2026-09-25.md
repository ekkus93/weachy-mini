# RMA-190 Permission A54 Keyguard Status — 2026-09-25/26

## Scope

This note records the physical Samsung A54 status for the RMA-190 permission denial/revocation lane on `ralph/rma190-permission-end-to-end`.

It preserves the earlier keyguard blocker evidence and records the subsequent successful retry after the user confirmed the A54 was connected again to the self-hosted runner server.

## Earlier blocked run

The earlier run below used exact branch head:

- `8f672b3b77a5f5be4ff9a620e05687aebf4466c8`

Workflow evidence:

- Workflow: `A54 RMA-190 Permission End-to-End`
- Run ID: `36197725278`
- Job ID: `108277289395`
- Job: `RMA-190 permission denial/revocation on A54`
- Result: failure

That failure was not classified as a source-code failure. The run completed setup, source-contract validation, Unity/Android resolution, generated presentation prep, Android runtime staging, APK build/verify, and physical-device pinning, then failed inside the physical acceptance step because the phone was awake but still blocked by secure keyguard.

Relevant device diagnostic:

```text
Could not prepare an awake device that is not blocked by secure keyguard.
Power state:   mWakefulness=Awake
mWakefulness=1
Keyguard state: mKeyguardShowing=true
An unoccluded PIN/pattern/password keyguard cannot be bypassed by this helper.
```

The foreground helper correctly refused to bypass an unoccluded PIN/pattern/password keyguard. That remains expected harness behavior, not a source defect.

## Successful retry

The user reported on 2026-09-26 that the A54 phone was connected again to the server where the self-hosted runner is running. The branch was updated at `7072a5043d4632398d02174b6fdc8525fd8fbeaa` to record that status and retrigger the A54 workflow.

The retry succeeded:

- Workflow: `A54 RMA-190 Permission End-to-End`
- Run ID: `36233165870`
- Job ID: `108380029889`
- Branch head: `7072a5043d4632398d02174b6fdc8525fd8fbeaa`
- Result: `success`
- Device: Samsung `SM-A546E`, Android SDK `36`, ABI `arm64-v8a`
- Serial: `R5CW31AX4FL`

Passing steps:

1. `Check out repository`
2. `Set up Python`
3. `Validate permission acceptance source contracts`
4. `Resolve local Unity and Android installations`
5. `Prepare generated Reachy Unity presentation`
6. `Stage production MuJoCo runtime for Unity Android`
7. `Build ARM64 API-26 device APK`
8. `Verify ARM64 API-26 device APK`
9. `Pin one physical Android device`
10. `Run RMA-190 permission denial/revocation acceptance`

The acceptance report emitted by the job records:

```text
permission_requested_on_startup: false
not_requested_observed: true
granted_state_observed: true
revoked_state_observed: true
final_android_permission_granted: false
camera_inventory_hidden_without_permission: true
real_camera_inventory_observed_when_granted: true
synthetic_camera_inventory_used: false
physical_reachy_required: false
status: Passed
```

## Current closure boundary

The RMA-190 permission denial/revocation physical Android acceptance requirement is now satisfied by run `36233165870` for branch head `7072a5043d4632398d02174b6fdc8525fd8fbeaa`.

This branch still must not be merged or used to reconcile the authoritative `master` TODO until the repository clean-mergeability gate is satisfied. The generic PR CI run for the same head still failed with the repository-wide zero-step/no-log `JOB_NOT_FOUND` infrastructure signature, which is not a source-code failure but remains a merge gate under exact-head policy.

This file is included in the branch-specific A54 workflow path filter so status/evidence updates to this lane can retrigger the physical workflow without changing production source behavior.
