# RMA-091 CameraX Frame Acquisition Validation — 2026-08-04

## Result

RMA-091 is complete. The Android application binds lifecycle-aware CameraX
`Preview` and `ImageAnalysis` use cases, publishes bounded YUV frame metadata to
the managed Unity application, and fails closed across lifecycle, camera,
permission, and session transitions.

RMA-091 intentionally does not access image planes, copy camera pixels to CPU
memory, or expose a Unity-consumable GPU texture. The bound preview is connected
to a private discard surface until RMA-092 supplies the production texture
bridge.

## Validated implementation

- Exact physical-device implementation commit:
  `25b496917d47f53e217d67ae7d996b91fa5dce81`
- Self-hosted workflow: `Local Unity Android Validation`
  - run `30934825724`
  - job `92078267747`
  - runner: `kawa`
  - conclusion: success
- ShellCheck-hardened camera implementation baseline:
  `76e809355771cfcd5edc5ca13107c6a02fb4af22`

Between the exact physical implementation SHA and the ShellCheck-hardened
baseline, only `scripts/android_device_acceptance_foreground.sh` and
`scripts/run_rma065_hosted_gate.sh` changed. The later evidence-addendum commits
modify documentation and a bounded helper that was removed after use. None of
those commits modify CameraX, Unity camera acquisition, Android build
orientation, or RMA-091 acceptance behavior.

Two final device-specific hardening defects were repaired before acceptance:

1. `2d4580d9b1beea5938143ebcb18b4cfeea44f5bc` made CameraX pause and resume
   lifecycle calls nonblocking so Unity cannot deadlock while Android is
   suspending the activity.
2. `3ac648ddca0f644546d4dbe3761caea1daa01518` and the final implementation
   commit released the activity's portrait lock at runtime and enforced
   `android:screenOrientation="unspecified"` in the generated Gradle manifest.

## Acquisition contract

The accepted implementation provides the following permanent behavior:

- CameraX `Preview` and `ImageAnalysis` are bound together to an explicit
  lifecycle owner and exact Camera2 camera identifier.
- CameraX is pinned to version `1.6.1` in the packaged Android library.
- Analysis output remains `YUV_420_888` and uses
  `STRATEGY_KEEP_ONLY_LATEST`, preventing an analyzer backlog from retaining
  obsolete frames.
- Analysis runs on one dedicated executor. Async provider, camera-state, and
  analyzer callbacks carry a generation and session identity so callbacks from
  a stopped or superseded session cannot publish state.
- Each accepted frame carries a monotonic sequence and timestamp, camera ID,
  lens facing, sensor orientation, output rotation, dimensions, crop rectangle,
  pixel format, and intrinsics with explicit provenance.
- The analyzer reads metadata only. It does not call `getPlanes()`, request
  CameraX RGBA output, or copy pixel data into Unity.
- `ImageProxy.close()` has one explicit call site and is executed from the
  analyzer `finally` block, including stale-session and failure paths.
- Stop and camera-switch operations invalidate the current generation, clear
  the analyzer, detach preview, unbind CameraX use cases, destroy the lifecycle
  owner, close the private preview surface, and stop the analyzer executor.
- Permission revocation and camera-unavailable conditions publish typed visible
  states rather than retaining a stale running state or fabricating frames.

## Automated validation

The exact physical-validation run first passed:

- 85 of 85 Unity edit-mode tests;
- 1 of 1 Unity play-mode tests;
- the ARM64/API-26 IL2CPP APK build and artifact verification.

The RMA-091 Unity tests included:

- `RapidStartStopAndFrontRearSwitchUseDistinctSessions`;
- `PauseResumeAndPermissionRevocationRemainVisible`;
- `DuplicatePollAndStaleMetadataDoNotReplaceLatestFrame`;
- `UnavailableCameraFailsClosedWithoutStartingPlatform`;
- `OwnedOmissionAndBusySignalKeepSessionButDisconnectStopsFailClosed`.

The permanent hosted RMA-091 workflow also enforces:

- the pinned CameraX dependency set;
- lifecycle-bound `Preview` and `ImageAnalysis`;
- `KEEP_ONLY_LATEST` backpressure and `YUV_420_888` output;
- exact Camera2 camera selection;
- required timestamp, orientation, crop, format, and intrinsics metadata;
- absence of image-plane access and CPU/RGBA conversion;
- one `ImageProxy.close()` site in a `finally` block;
- generation-based invalidation of asynchronous callbacks;
- the managed lifecycle, stale-frame, switch, and unavailable-camera tests.

Standard CI run `30934826259` on the implementation SHA passed its managed,
native, Android, and pinned-Reachy-model jobs. Its overall result was not used as
completion evidence because the static job failed on two unrelated ShellCheck
warnings. Those warnings were subsequently corrected by commits
`f5860ce6b29d624d9cb88e10e4259e3963e52768` and
`76e809355771cfcd5edc5ca13107c6a02fb4af22`.

## Physical-device validation

The installed development APK was exercised on:

- serial: `LGH87250967ab9`
- manufacturer: LGE
- model: LG-H872
- Android API: 26
- ABI: `arm64-v8a`
- launcher activity:
  `com.ekkus.weachymini/com.unity3d.player.UnityPlayerGameActivity`

The accepted sequence proved:

1. With CAMERA granted, acquisition started rear camera ID `0` in session 1.
   The first checkpoint contained 9 accepted frames and the progress checkpoint
   contained 22, proving continuous analyzer progress.
2. Every observed frame retained positive timestamps, monotonic metadata,
   `Yuv420888` format, valid crops, and valid intrinsics metadata with explicit
   uncalibrated-estimate provenance.
3. Pressing Home produced managed and CameraX `Paused` state with
   `application_pause_count = 1`. Foregrounding the same Unity activity produced
   `application_resume_count = 1`, returned the same session to `Running`, and
   advanced accepted frames from 28 to 31.
4. Explicit stop and restart produced a distinct rear-camera session 2 without
   stale metadata from session 1.
5. Rotation preparation stopped the stream cleanly. After Android display
   rotation changed from 0 to 1, rear-camera session 3 reported frame rotation
   `0` instead of the initial `90`, proving that output rotation followed the
   actual display orientation.
6. An orderly stop and front-camera start produced session 4 on camera ID `1`,
   front lens facing, sensor orientation `270`, and valid frame metadata.
7. Revoking CAMERA and relaunching produced `PermissionRevoked`, no active
   session, no frame, and a visible permission diagnostic.

The cumulative accepted report recorded:

- status: `passed`;
- four distinct camera sessions;
- 58 frame observations;
- rear and front frames observed;
- initial rotation: 90 degrees;
- rotated output: 0 degrees;
- one pause and one resume transition;
- zero stale frames;
- zero faulted transitions;
- zero unavailable transitions during the successful stream sequence;
- final permission-revocation state: `PermissionRevoked`.

The same exact-SHA workflow also passed RMA-090 camera discovery, RMA-022 native
lifecycle acceptance, authoritative-rendering acceptance, and the final APK
upload.

## Evidence artifacts

All artifacts below belong to workflow run `30934825724` and exact commit
`25b496917d47f53e217d67ae7d996b91fa5dce81`.

- RMA-091 camera device report: artifact `8902852311`
  - digest `sha256:5e5251a39a8dc14bf7da91f828ad0d56cbe0ee1667a49950c4273f671a73e462`
- Unity test results: artifact `8902734956`
  - digest `sha256:d02eaeba888f2657b89e5657df138c27f5692a11e856be3f1385cc8077d92d4f`
- RMA-090 camera discovery report: artifact `8902812302`
  - digest `sha256:ce93adb9abf7f11b7f8d8dd0ce32709cd8113f347072efb0f81ab619612bb0ac`
- RMA-022 lifecycle report: artifact `8902883435`
  - digest `sha256:160625a9754774b18f60b10ed44a16ab2634bfadbdcba9cc2325241cbcc4bc90`
- Authoritative-rendering report: artifact `8902901308`
  - digest `sha256:f449e4f81f152b870ab10c7247c848f339f9cee53991a5fe7e70e14661f47b0c`
- Verified ARM64/API-26 APK: artifact `8902921211`
  - digest `sha256:05f2f535a4e1ed7ca8a59e2c519e0892b0cfedd8a8c828cfa3323a2cd642220d`

## Roadmap completion and cleanup

- Validation record added in commit
  `7a148c5a8c5711a84c93de960291632257b32b11`.
- The authoritative RMA-091 TODO block was marked complete in commit
  `1fcba6781e814bae6efdf17149866f907c96e98a` after the validation file already
  existed at its permanent path.
- The one-time bounded apply workflow and helper were removed in commits
  `198d2cd9cd78d272975d56b5c52b1be28faadce4` and
  `05b582591c65d106bb0ffa11eb049e3043855cc0`.
- The net repository change from the ShellCheck-hardened baseline is limited to
  this permanent validation record and the authoritative TODO completion.

## Boundary retained for RMA-092

RMA-091 proves lifecycle-safe frame acquisition and authoritative metadata only.
The private preview discard surface is deliberate. No camera image is yet
converted into, retained by, or sampled from a Unity GPU texture. RMA-092 must
implement the production YUV texture bridge, color-range handling, rotation,
front-camera mirroring, timestamp correspondence, and a CPU reference converter
without weakening the RMA-091 ownership and close-once guarantees.
