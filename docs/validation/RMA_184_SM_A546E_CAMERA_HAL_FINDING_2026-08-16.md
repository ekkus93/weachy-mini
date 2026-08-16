# RMA-184 finding — SM-A546E vendor camera HAL aborts and reboots the device on rear camera start

**Date:** 2026-08-16
**Device:** Samsung SM-A546E (Galaxy A54 5G), Exynos 1380, Android 16 / API 36
**Status of this finding:** vendor defect confirmed by captured logcat; app-side workaround (analysis-only binding) applied and physically verified on the device
**Related roadmap items:** RMA-091 (CameraX frame acquisition), RMA-184 (representative-device matrix, mid class)

## Symptom

Starting the first rear CameraX session reboots the entire device roughly 15 seconds after the
start command. Reproduced four consecutive times through the Local Unity Android Validation
suite (always at "Install and run RMA-091 camera acquisition acceptance"; RMA-090 discovery,
which only enumerates cameras, always passed) and once via a controlled standalone
reproduction. `sys.boot.reason=reboot` with no `/sys/fs/pstore` record and no dropbox crash
entry for the app: the vendor HAL aborts and the platform restarts without recording an
app-attributable crash, so from CI the failure initially presented as a USB dropout.

## Root cause (captured logcat)

The vendor HAL attaches an internal Zero-Shutter-Lag stream to the IMPL_DEF preview stream
and then aborts on an assertion when its ZSL buffer is smaller than the full-sensor crop it
computes:

```
E WNC]DeviceNodeStreamMetaUtil: [updateCropSize:153]
    Max image(ServiceForZsl[0,3003]) buffer size (1440x1080)
      < Requested cropped image size (4080x3060)
E WNC]ASSERT: Assertion failed [void updateCropSize(...):153]
    #01 DeviceNodeStreamMetaUtil::updatePerFrame
    #03 SensorHWProcessorImpl::updateSrcImageMetadata
    #04 SensorHWProcessorImpl::requestCaptureImpl
    (/vendor/lib64/hw/camera.s5e8835.so)
```

4080x3060 is the full active sensor array; 1440x1080 is the stream size the app negotiated.
The abort kills the camera provider process and takes the device down with it.

## What the app requested (also captured)

- Two streams only: Preview (IMPL_DEF 0x22) and ImageAnalysis (YUV_420_888 0x23), both
  1440x1080, `TEMPLATE_PREVIEW`. No `ImageCapture` use case was bound, so the framework never
  requested ZSL; the HAL selected its `ZSL_OUTPUT` path unilaterally
  (`VendorCameraScenario: [0][ZSL_OUTPUT] ... size:1440x1080` against the preview stream).
- The standard escape hatch is ignored by this HAL: a
  `CONTROL_ENABLE_ZSL=false` capture-request option was delivered
  (`android.control.enableZsl false` visible in the session template) and the HAL created its
  ZSL stream and hit the same assert regardless, at the same ~15-second mark.

A HAL must reject an unsatisfiable stream configuration with an error; aborting — and thereby
rebooting the device — is a vendor defect. No third-party app should be able to trigger this.

## Workaround adopted

The bridge's Preview use case only ever fed `ReachyDiscardingPreviewSurfaceProvider`, which
discards every frame unread; all consumed frames come from the ImageAnalysis stream, which
the vendor ZSL does not touch. The binding is therefore now `ImageAnalysis`-only
(`ReachyCameraFrameBinder.java`), removing the IMPL_DEF stream the defective ZSL attaches to
without changing what the app consumes. The spec's "preview and image-analysis frames"
language (`docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md` §camera) remains satisfied in
substance — presentation is rendered from the analysis-fed GPU texture bridge, and the
discarded surface never contributed a displayed pixel — but the literal two-use-case binding
described by RMA-091's original completion evidence no longer exists.

## Physical verification of the workaround (2026-08-16, standalone reproduction)

With the analysis-only binding installed on the same device, the identical reproduction that
previously rebooted the phone at ~15 seconds on every attempt ran clean:

- rear camera: survived a 90-second watch with continuous device uptime; acquisition state
  `Running`, frame sequence 459, 439 accepted frames, 0 stale, no CPU pixel copy;
- front camera: survived a 40-second watch; acquisition state `Running`, 246 accepted frames;
- captured logcat for the run contains 0 HAL assertions, 0 `updateCropSize` errors, and 0
  `ZSL_OUTPUT` stream creations — with no IMPL_DEF preview stream configured, the vendor HAL
  never instantiates its ZSL machinery at all.

## Matrix consequence

SM-A546E camera acceptance is unblocked by the analysis-only binding. Mid-class
qualification in `models/reachy-mini/android-device-matrix.json` remains
`pending_measurement` until the full acceptance suite and long-run evidence are collected
through CI. This finding should be retained as a support-policy note for Exynos 1380
devices: any future rebinding of a preview-class (IMPL_DEF) stream re-exposes the
device-rebooting vendor defect, and `CONTROL_ENABLE_ZSL=false` is proven ineffective as a
guard. The LG-H872 (Android 8 / Adreno) does not exhibit the defect.
