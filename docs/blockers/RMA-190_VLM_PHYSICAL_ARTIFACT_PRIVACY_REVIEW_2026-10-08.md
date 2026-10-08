# RMA-190 rear-camera/VLM physical artifact privacy review

**Status: source privacy hardening committed; physical requalification and diagnostic parser correction still open (2026-10-08).** This review concerns the test runner's exported diagnostics,
not the shipping VLM encoder or a proven disclosure of actual camera content.

## Observed repository behavior

Before source hardening, `scripts/run_rma190_rear_camera_vlm_end_to_end_android.sh`
collected `adb logcat -d -v threadtime` without an app-PID filter,
`dumpsys media.camera`, `dumpsys activity activities`, and
`adb exec-out screencap -p` into its report directory. The physical
RMA-190 GitHub Actions workflow uploads that report directory as an artifact.

The screenshot could include a live camera preview. Full-device logcat,
camera-service dumps, and activity dumps may contain information about other
applications on the phone. These are **potential** privacy exposures, not
proof that a particular artifact contains raw camera imagery or credentials.
The structured acceptance report and sanitized fixture-request metadata
already provide the intended machine-readable pass/fail evidence.

## Required source remediation

1. Do not collect or upload a device screenshot in the VLM camera scenario.
   A screenshot is not required to prove the shipping transformed-frame path.
2. Replace full-device logcat with bounded, current-invocation diagnostics
   scoped to the shipping app PID and allowlisted RMA-190 stage/RMA-181
   resource-transition messages. If app PID or start time is unavailable,
   omit logcat rather than falling back to a global capture.
3. Avoid `adb logcat -c`, which clears the phone owner's global log buffer.
   Do not export full-device camera-service or activity inventories.
4. Retain the structured app report, app-scoped package metadata, APK/report
   hashes, `adb reverse` state, and sanitized fixture request metadata.
   Never persist the camera image, raw HTTP body, or authorization value.
5. Add a permanent regression contract asserting the physical runner does
   not invoke `screencap`, unfiltered `logcat`, or global camera/activity
   dumps. Validate the exact source head in hosted CI and, when available,
   requalify the shipping physical VLM scenario on the Android device.

## Implementation and exact-head qualification

Commit `235c0267509fb16029143e7a7a0f848586b16154` removed screenshot,
global camera/activity dumps, and global log clearing from the physical
runner. It introduced PID- and device-start-time-scoped logcat filtering,
bounded stage/resource-event output, and filtered app package metadata.
`scripts/tests/test_rma190_vlm_artifact_privacy.py` now runs in the
permanent release-review workflow. Commits `60685390b12042440280f021e42702c457224fc5`
and `dfde460f31db3e1f7b2d5306fe006fa72892ceac` reconciled the
existing RMA-190 physical-runner contract and Ruff formatting.

On exact head `dfde460f31db3e1f7b2d5306fe006fa72892ceac`,
release-review run `37860776077` and RMA-173 run `37860776036`
passed. Hosted CI and physical device validation were still pending when
this record was written. The earlier RMA-190 physical workflow
`37860425710` failed at an outdated static assertion, before the
device test; this was corrected in the subsequent commit.

**Remaining parser defect:** the inline Python diagnostic filter in the
runner currently contains doubled backslashes in the regular-expression
word boundaries and newline separators. That causes stage messages to
be missed and can render resource transitions without real line breaks.
This is a diagnostic-quality defect, not evidence of a private-media leak.
Two attempts to commit a corrected runner body were blocked by write-safety
checks; inspection confirmed `master` did not move after those attempts.
Correct the inline parser, add a functional fixture test for its emitted
lines, and rerun exact-head hosted and physical validation. Do not
misrepresent a static privacy contract pass as physical VLM acceptance.

**Acceptance boundary:** do not close the remaining RMA-190 rear-camera/VLM
scenario or infer release privacy acceptance from this review document.
