# RMA-190 front-camera A54 validation — 2026-09-24

## Branch and source under test

- Canonical lane: PR #72, `ralph/rma190-camera-requalify`
- Latest source head: `d2ada8375b29088eb4e506ee98133c3ce5bbe1b6`
- Base source: current `master` `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Device used while available: Samsung A54 reported by adb as serial `R5CW31AX4FL`, `arm64-v8a`, SDK `36`

## Runs

### Temporary current-master diagnostic run

A temporary current-master workflow first verified the attached A54 runner/device path:

- branch: `ralph/a54-rma190-e2e-20260924`
- head: `924c0f169f0b05cfe5ad782985bac1370171f137`
- run id: `36058331090`

That run built the APK, pinned the A54, ran the RMA-190 core acceptance step, uploaded core evidence, then attempted camera acceptance. Because the workflow used `continue-on-error` to allow both evidence uploads, its final verdict step failed and could not be used as clean closure evidence.

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

### Source-side fix prepared after the A54 was disconnected

The A54 was no longer available after the diagnostic dump. The current PR #72 head now contains a source-side fix and a source-contract guard:

- head: `d2ada8375b29088eb4e506ee98133c3ce5bbe1b6`
- production change: `ReachyAndroidPerceptionDriver.TryBuildFrame` now constructs the identity `ReachyPhoneOpticalOrientationSample` using `descriptor.TimestampNanoseconds`.
- regression guard: `scripts/tests/test_rma190_camera_end_to_end.py` asserts the driver uses `descriptor.TimestampNanoseconds` and does not regress to `ReachyPhoneOpticalOrientationSample.Identity(NowNanoseconds())`.

Local sandbox validation performed before publishing the guard:

```text
python3 scripts/tests/test_rma190_camera_end_to_end.py
# Ran 5 tests in 0.003s — OK
```

The exact-head physical workflow for `d2ada8375b29088eb4e506ee98133c3ce5bbe1b6` may not be usable as closure evidence because the phone was disconnected after the diagnostic. Any subsequent physical-device failure at pin/install/acceptance must be interpreted in that context.

## Current conclusion

The RMA-190 front-camera scenario is **not closed**. The A54 device path worked while the phone was connected, and the source-side timestamp-correspondence defect has a concrete fix on PR #72, but the camera checkbox remains open until the fixed head gets a clean physical A54 acceptance run and inspectable passing report.

## Next debugging steps

1. Re-run `RMA-190 Front Camera End-to-End Android Acceptance` on PR #72 head `d2ada8375b29088eb4e506ee98133c3ce5bbe1b6` when a physical A54 is available again.
2. If the fixed head reaches a new failure, inspect `/sdcard/Android/data/com.ekkus.weachymini/files/rma190-camera-end-to-end-acceptance.json`; the current diagnostics should name the exact active phase plus the last perception-driver state/message.
3. Keep the RMA-190 front-camera TODO checkbox open until a physical run passes and the resulting report is inspectable.
4. Do not treat the current branch as merge-ready solely because source contracts pass; physical acceptance still needs to pass on the exact head.
