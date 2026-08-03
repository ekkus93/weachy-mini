# Android Camera Capability Discovery

## Scope

RMA-090 introduces permission and capability discovery for Android device
cameras. It does not bind CameraX preview or `ImageAnalysis`, open a camera
device, retain camera buffers, or provide frames to Unity. Those responsibilities
remain in RMA-091 and RMA-092.

The fixed Unity front/three-quarter camera remains the robot presentation camera.
Android device cameras are a separate input capability whose permission,
inventory, availability, orientation, analysis sizes, and intrinsics are exposed
to application state and diagnostics.

## Permission policy

`ReachyAndroidCameraDiscovery` never requests permission during `Awake`, startup,
resume, or periodic refresh. It requests `android.permission.CAMERA` only through
`RequestAccessOrRefresh`, which is bound to an explicit camera control.

The bridge records whether permission was requested or granted in `PlayerPrefs`.
This permits a process restarted after `pm revoke`, Android settings changes, or
an OEM permission kill to report `Revoked` instead of incorrectly returning to
`NotRequested`.

Permission states are explicit:

- `NotRequested`: no request has been initiated;
- `Requesting`: an Android permission dialog is outstanding;
- `Granted`: capability inventory is available;
- `Denied`: a later user action may request access again;
- `PermanentlyDenied`: the next action opens Android application settings;
- `Revoked`: permission was previously granted and is no longer present;
- `Unsupported`: the current platform is not an Android player;
- `Faulted`: permission is granted but discovery failed with diagnostics.

The first denial is never classified as permanent solely because an OEM returns
no rationale. Permanent denial requires a later request and a false rationale
result.

## Android bridge

The Java library uses Camera2 metadata because CameraX does not expose every
required characteristic. It registers `CameraManager.AvailabilityCallback` and
enumerates `CameraCharacteristics` without calling `openCamera`.

For each camera it reports:

- stable camera ID;
- front, rear, external, or unknown facing;
- sensor orientation in degrees;
- Camera2 hardware level;
- current availability, including in-use/unavailable;
- every distinct YUV 4:2:0 analysis output size;
- active sensor-array dimensions;
- `LENS_INTRINSIC_CALIBRATION` when present.

Global Camera2 failures retain specific categories such as disabled,
disconnected, in-use, maximum-cameras-in-use, permission denied, or generic
camera access error.

## Intrinsics and fallback

Platform intrinsics are recorded as Android calibration metadata, not as Reachy
calibration. When Android does not expose intrinsics, the capability explicitly
requires a versioned checkerboard calibration. Until such calibration exists,
RMA-091 may use only an explicitly uncalibrated pinhole estimate derived from the
active array and selected analysis resolution. The fallback must never be labeled
measured or calibrated.

## Application integration

`ReachyDiscoveredCameraApplicationService` composes two independent concerns:

1. it validates that the fixed non-navigable Unity presentation camera remains
   active;
2. it publishes Android device-camera discovery health.

Even with permission and a complete inventory, the service remains degraded
rather than ready because no frame-acquisition runtime exists before RMA-091.
The main screen exposes permission state, front/rear counts, availability,
orientation, largest analysis size, and intrinsics source. Camera preview remains
an actionable RMA-091 unavailable state.

## Validation

RMA-090 validation consists of:

- managed warnings-as-errors tests for immutable state and fail-closed
  invariants;
- Unity edit-mode tests with an injected Android platform adapter;
- static manifest, Java, and application-composition checks;
- full Unity edit-mode/play-mode regression;
- ARM64 API-26 IL2CPP build and APK verification;
- physical-device acceptance proving no startup permission request, granted
  inventory discovery, front/rear enumeration, resolution/orientation reporting,
  intrinsics provenance, and revocation recovery;
- the existing installed lifecycle and authoritative-rendering gates.
