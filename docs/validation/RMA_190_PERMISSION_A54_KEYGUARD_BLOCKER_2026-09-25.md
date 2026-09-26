# RMA-190 Permission A54 Keyguard Blocker — 2026-09-25

## Scope

This note records the current physical A54 status for the RMA-190 permission denial/revocation lane.

The branch is `ralph/rma190-permission-end-to-end`. The earlier run below used exact branch head:

- `8f672b3b77a5f5be4ff9a620e05687aebf4466c8`

## Workflow evidence

- Workflow: `A54 RMA-190 Permission End-to-End`
- Run ID: `36197725278`
- Job ID: `108277289395`
- Job: `RMA-190 permission denial/revocation on A54`
- Result: failure

The failure is not classified as a source-code failure. The run completed all setup/build/device-pin steps successfully, then failed inside the physical acceptance step because the phone was awake but still blocked by secure keyguard.

Successful steps before the blocker:

1. `Check out repository`
2. `Set up Python`
3. `Validate permission acceptance source contracts`
4. `Resolve local Unity and Android installations`
5. `Prepare generated Reachy Unity presentation`
6. `Stage production MuJoCo runtime for Unity Android`
7. `Build ARM64 API-26 device APK`
8. `Verify ARM64 API-26 device APK`
9. `Pin one physical Android device`

The failing step was:

- `Run RMA-190 permission denial/revocation acceptance`

The relevant device diagnostic from the job log was:

```text
Could not prepare an awake device that is not blocked by secure keyguard.
Power state:   mWakefulness=Awake
mWakefulness=1
Keyguard state: mKeyguardShowing=true
An unoccluded PIN/pattern/password keyguard cannot be bypassed by this helper.
```

The foreground helper correctly refused to bypass an unoccluded PIN/pattern/password keyguard. That is an expected safety boundary for the physical-device harness, not evidence that the RMA-190 permission source path is defective.

## Current status

The user reported on 2026-09-26 that the A54 phone is connected again to the server where the self-hosted runner is running. This lifts the earlier "device disconnected/unavailable" blocker for this lane.

The exact physical acceptance path should be retried now that the device is connected. The retry must still fail closed if the phone is behind an unoccluded PIN/pattern/password keyguard, if more than one physical Android device is attached, or if the connected device does not satisfy the workflow's ARM64/API-26+ requirements.

This branch update intentionally adds this status file to the A54 workflow path filter so future status-only hardware-readiness updates can retrigger the branch-specific physical workflow without changing source behavior.

This lane remains open until a fresh exact-head physical acceptance run passes and its run ID, job ID, branch head, and report evidence are recorded.
