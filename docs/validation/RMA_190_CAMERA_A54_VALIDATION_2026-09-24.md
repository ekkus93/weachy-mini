# RMA-190 front-camera A54 validation — 2026-09-24

## Branch and source under test

- Canonical lane: PR #72, `ralph/rma190-camera-requalify`
- Latest physically validated source/test head: `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`
- Base source: current `master` `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Device used: Samsung A54 reported by adb as serial `R5CW31AX4FL`, `arm64-v8a`, SDK `36`

## Runs

### Temporary current-master diagnostic run

A temporary current-master workflow first verified the attached A54 runner/device path:

- branch: `ralph/a54-rma190-e2e-20260924`
- head: `924c0f169f0b05cfe5ad782985bac1370171f137`
- run id: `36058331090`

That run built the APK, pinned the A54, ran the RMA-190 core acceptance step, uploaded core evidence, then attempted camera acceptance. Because the workflow used `continue-on-error` to allow both evidence uploads, its final verdict step failed and could not be used as clean camera-closure evidence.

A follow-up standalone camera run removed `continue-on-error` from the camera acceptance step:

- branch: `ralph/a54-rma190-camera-20260924`
- head: `a4207fc9ba781a40439069ef81fb303928d54734`
- run id: `36059258794`

It passed source-contract validation, Unity/Android resolution, presentation generation, MuJoCo Android runtime staging, APK build, APK verification, and physical A54 pinning. The actual `scripts/run_rma190_camera_end_to_end_android.sh` acceptance step failed.

A device-side diagnostic dump from the installed app reported:

```json
{
  "schema_version": 1,
  "status": "failed",
  "error": "RMA-190 camera end-to-end acceptance was cancelled."
}
```

The report existed at `/sdcard/Android/data/com.ekkus.weachymini/files/rma190-camera-end-to-end-acceptance.json`.

### Canonical PR #72 retry with extended app timeout

This branch then increased `ReachyRma190CameraEndToEndAcceptance`'s internal timeout from `90_000` ms to `135_000` ms so it better matches the shell harness budget.

- head: `16195b5db180c70a12310d6367523b23e080a26d`
- run id: `36060214173`
- workflow: `RMA-190 Front Camera End-to-End Android Acceptance`

That run again passed source-contract validation, Unity/Android resolution, presentation generation, MuJoCo Android runtime staging, APK build, APK verification, and physical A54 pinning. The actual front-camera end-to-end acceptance step still failed. Ralph failure excerpts did not expose the final JSON report for this second run, so this document records the exact known failure state without claiming a pass.

### Phase-marker and driver-diagnostic runs

Additional canonical PR #72 runs added phase-level diagnostics and then perception-driver snapshot diagnostics. The A54 reports narrowed the failure from a generic timeout to a specific production pipeline rejection:

- `1b43628f6aec67f43320cf4917c4e66fc353a475` / run `36063973396`: timeout while `restoring camera acceptance state`; this was later identified as a cleanup-phase overwrite of the true primary phase.
- `3b7483475e2fb69d3f8a58756a5c00b9d421ed12` / run `36064816981`: timeout while `capturing baseline transformed front frame`.
- `c6840c799148c52bd5bb7496058f01e651d5186f` / run `36066778327`: timeout while `capturing baseline transformed front frame`; device-side compact dump run `36068367008` reported the last perception driver diagnostic:

```text
last perception state=NoCameraFrame; message=Homography reprojection did not produce a frame: The camera texture and phone orientation are not timestamp-corresponding.; revision=2709
```

That diagnostic means host/runtime discovery, camera discovery, front-camera acquisition, baseline authoritative state, and deterministic calibration fixture installation all progressed far enough to reach baseline transformed-frame capture. The blocker was the shipping homography path rejecting every frame because `ReachyAndroidPerceptionDriver` stamped the fail-closed identity phone-orientation sample with driver wall-clock time instead of the exact camera texture timestamp required by `ReachyCameraHomographyCalculator`.

### Source-side timestamp-correspondence fix

PR #72 then added a source-side fix and source-contract guard:

- production change: `ReachyAndroidPerceptionDriver.TryBuildFrame` now constructs the identity `ReachyPhoneOpticalOrientationSample` using `descriptor.TimestampNanoseconds`.
- regression guard: `scripts/tests/test_rma190_camera_end_to_end.py` asserts the driver uses `descriptor.TimestampNanoseconds`, asserts the explanatory timestamp-correspondence comment remains present, and guards against regressing to `ReachyPhoneOpticalOrientationSample.Identity(NowNanoseconds())`.

Local sandbox validation before publishing the guard:

```text
python3 scripts/tests/test_rma190_camera_end_to_end.py
# Ran 5 tests in 0.003s — OK
```

### Passing exact-head A54 validation

The A54 was reconnected and the canonical PR #72 physical workflow was run against the exact source/test head containing the timestamp-correspondence fix and hardened source contract:

- head: `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`
- run id: `36104066566`
- workflow: `RMA-190 Front Camera End-to-End Android Acceptance`
- job: `RMA-190 front camera E2E on Android`
- result: `success`

All workflow steps passed:

- source-contract validation;
- Unity/Android resolution;
- generated Reachy Unity presentation preparation;
- production MuJoCo Android runtime staging;
- ARM64 API-26 APK build;
- APK verification;
- physical A54 pin;
- `scripts/run_rma190_camera_end_to_end_android.sh` front-camera acceptance;
- evidence upload.

Ralph synthesized CI report for run `36104066566` reported `result="success"`, `head_sha="c2d730aafe7ad9ce396f6f431c95563c8ac6bc83"`, and no failed steps.

## Current conclusion

The RMA-190 front-camera scenario has clean physical A54 acceptance evidence for PR #72 source/test head `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`. The branch should still be treated as unmerged until normal PR review/merge gates complete; the authoritative `master` TODO checkbox should not be closed on `master` until the reconciled evidence reaches `master` safely.

## Next steps

1. Reconcile the authoritative TODO on a safe branch/PR with the exact evidence above.
2. Merge PR #72 only when the repository's merge policy is satisfied. Do not confuse the unrelated generic zero-step/log-unavailable CI failures with source failures.
3. If the branch receives any non-documentation source changes after `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`, rerun `RMA-190 Front Camera End-to-End Android Acceptance` before relying on this scenario as closed.
