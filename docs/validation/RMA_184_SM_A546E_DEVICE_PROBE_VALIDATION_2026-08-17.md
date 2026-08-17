# RMA-184 device probe — SM-A546E (mid performance class)

**Date:** 2026-08-17
**Device:** Samsung SM-A546E (Galaxy A54 5G), Exynos 1380, serial R5CW31AX4FL
**Build:** `Builds/Android/weachy-mini-device-arm64-api26.apk` (2026-08-16 13:18 device-feasibility
build; includes the RMA-091 Preview-removal camera-HAL workaround from
`RMA_184_SM_A546E_CAMERA_HAL_FINDING_2026-08-16.md`)
**Related roadmap items:** RMA-184 (representative-device matrix, mid class)

## What this is

A single run of `scripts/run_rma184_device_probe_android.sh` (the `reachy_rma184_device_probe`
launch extra) against the physical device. This is the quick runtime-metadata probe defined by
RMA-184 -- it records SoC/RAM/graphics/camera/speech-service capability and evaluates
`ReachyRepresentativeDeviceSupportPolicy`. It is **not** the long-running (>=1800s)
soak/thermal qualification the roadmap's acceptance criteria still require; the matrix's
`measurement_status` for this device reflects that distinction.

## Result

```json
{
  "schema_version": 1,
  "status": "passed",
  "manufacturer": "samsung",
  "model": "samsung SM-A546E",
  "soc": "s5e8835",
  "logical_processor_count": 8,
  "system_memory_mib": 5426,
  "operating_system": "Android OS 16 / API-36 (BP4A.251205.006/A546EXXSLFZG3)",
  "android_api_level": 36,
  "graphics_api": "Vulkan",
  "graphics_device": "Mali-G68",
  "camera_permission": "Granted",
  "camera_count": 4,
  "available_camera_count": 4,
  "rear_camera_available": true,
  "front_camera_available": true,
  "on_device_asr": "Faulted",
  "offline_tts": "Available",
  "support_status": "SupportedWithLimitations",
  "support_diagnostic": "Core runtime supported; full offline interaction limited by explicit_on_device_asr_unavailable."
}
```

The probe completed cleanly: no reboot, no crash, no ANR. `system_memory_mib=5426` is
consistent with a 6 GiB RAM SKU (Unity's `SystemInfo.systemMemorySize` reports somewhat below
nominal due to reserved memory) -- this confirms the installed unit is the 6 GiB variant of the
two the SoC/model family ships in ([6, 8] GiB); the 8 GiB variant remains unverified.

## Open finding: on-device ASR reports Faulted, not just unavailable

`AndroidOnDeviceAsrProvider`'s readiness probe returns `SpeechAvailabilityState.Faulted`
(`android_on_device_asr_support_check_faulted` / `android_on_device_asr_probe_failed`), which
means an *exception* was thrown during capability or language-support discovery -- not the
simpler, expected "API < 31" or "no explicit on-device recognizer" unavailability path (this
device is API 36, well above the API 31 floor). The RMA-184 support-policy evaluation folds
this into the same `explicit_on_device_asr_unavailable` support reason as a clean
unavailability, so it does not fail the probe, but it is a distinct condition worth
investigating on its own: something about this device/ROM's `SpeechRecognizer` implementation
throws where the readiness check expects a normal (non-exceptional) unsupported/unavailable
result. Root cause not yet investigated -- the on-device logcat window had already rotated past
the probe run by the time this was noticed. A follow-up run with logcat captured live around
the probe (`adb logcat | tee` from before `am start`) is needed to get the actual exception
type/message before deciding whether this is a real provider bug or an expected One UI/Android
16 platform behavior.

## What this does not cover

- No long-running (>=1800s) soak measurement (physics p95, render p95, memory growth, state
  lag, thermal-degradation order) -- required before "Publish measured default profiles" and
  the RMA-184 acceptance criteria can close for this device.
- Offline TTS reports `Available` from capability discovery only; audible output has not been
  positively verified on this device (mirrors the LG-H872 entry's own
  `not_yet_positive_audible_acceptance` caveat).
- The on-device ASR fault above is unexplained, not just unmeasured.
