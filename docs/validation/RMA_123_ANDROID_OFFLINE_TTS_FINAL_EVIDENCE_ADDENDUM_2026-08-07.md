# RMA-123 final exact-SHA evidence addendum

**Status:** Final exact-SHA physical signoff complete  
**Date:** 2026-08-07

This addendum closes the final documentation-SHA physical-validation boundary for RMA-123 without changing any RMA-123 production source.

## Final evidence SHA

`9872f5c103cc9a49cece408c22872f5f835ad787`

The existing RMA-123 validation document records the accepted implementation SHA `19d19a7f42a10475a7ce7650b96999bc61a9f86b` and its successful permanent contract, hosted CI, and physical validation. The later final evidence SHA above changed only RMA-123 documentation.

## Original final-SHA physical attempt

Physical workflow run `31182913660`, first job `92880321009`, reached the physical camera sequence after Unity tests, APK build/verification, RMA-090, and RMA-091 had passed. It then failed at RMA-092 camera-texture acceptance.

Because the final SHA changed documentation only and the identical production tree had already passed RMA-092 on the accepted implementation SHA, no source change, recovery fallback, retry loop, threshold relaxation, or camera-policy change was made.

## Unchanged-SHA rerun

The failed physical job was rerun on the **same exact final SHA**. Rerun job `92887382914` completed successfully.

The unchanged-SHA rerun passed:

- Unity EditMode/PlayMode tests;
- ARM64 API-26 APK build and verification;
- physical device pinning;
- RMA-090 camera discovery acceptance;
- RMA-091 CameraX acquisition acceptance;
- RMA-092 CameraX texture acceptance;
- RMA-111 lightweight tracking acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- all evidence/log uploads;
- APK upload;
- final commit-status publication.

This exact unchanged-SHA success demonstrates that the first RMA-092 failure was a transient physical capture/readback-class failure rather than an RMA-123 source regression. No production or validation-source modification was justified by the failed first attempt.

## Final-run artifacts

Artifacts from run `31182913660` include:

- RMA-090 report `5366269529` and logcat `5366269561`;
- RMA-091 report `5366337982` and logcat `5366338016`;
- RMA-092 report `5366349532` and logcat `5366349556`;
- RMA-111 report `5366356514` and logcat `5366356547`;
- lifecycle report `5366400091` and logcat `5366400136`;
- authoritative-rendering report `5366405143` and logcat `5366405186`;
- consolidated physical-validation evidence `5366405745`;
- APK artifact `reachy-mini-android-arm64-api26`, artifact ID `5366409655`, size 50,798,994 bytes, SHA-256 `e9fd69c5596472ebb375f9795c5d5d715cf6e3e69e5df130d52f51c23d7205a2`.

## Coverage boundary unchanged

This closes RMA-123's repository/device regression signoff. It still does **not** claim positive audible offline TTS on the LG-H872, because the standard physical regression does not invoke RMA-123 or record speech output. The live offline-speech acceptance boundary documented in `RMA_123_ANDROID_OFFLINE_TTS_VALIDATION_2026-08-07.md` remains in force.
