# RMA-190 front-camera A54 validation — 2026-09-24

## Branch and source under test

- Canonical lane: PR #72, `ralph/rma190-camera-requalify`
- Head under latest physical run: `16195b5db180c70a12310d6367523b23e080a26d`
- Base source: current `master` `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Device: Samsung A54 reported by adb as serial `R5CW31AX4FL`, `arm64-v8a`, SDK `36`

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

## Current conclusion

The RMA-190 front-camera scenario is **not closed**. The A54 device path is working and the build/install/pin prerequisites are good, but the camera acceptance still fails inside the app acceptance scenario. The earlier device report shows cancellation before success; the latest run proves that simply extending the internal timeout to 135 seconds did not close the scenario.

## Next debugging steps

1. Add finer-grained progress fields to `ReachyRma190CameraEndToEndAcceptance.Report` or a sidecar progress file so future physical failures identify the last completed phase: host/runtime discovery, camera discovery, front texture, baseline authoritative state, calibration fixture install, baseline transformed frame, gesture execution, rotated frame capture, and report validation.
2. Keep the RMA-190 front-camera TODO checkbox open until a physical run passes and the resulting report is inspectable.
3. Do not treat the current branch as merge-ready solely because source contracts pass; physical acceptance still fails.
