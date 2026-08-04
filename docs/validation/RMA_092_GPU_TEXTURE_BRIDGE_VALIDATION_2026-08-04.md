# RMA-092 Android Camera GPU Texture Bridge Validation — 2026-08-04

## Result

RMA-092 is complete on the validated implementation commit. CameraX
`YUV_420_888` frames are delivered through a bounded detached direct-buffer ring,
uploaded into Unity R8 plane textures, converted by the retained GPU shader into
an RGB `RenderTexture`, and exposed to preview and analysis consumers with
session, sequence, timestamp, crop, orientation, color, and mirror metadata.

The physical-device evidence distinguishes a genuinely dark unattended rear
camera from a failed GPU conversion. The same Vulkan/Adreno device passed a
deterministic YUV-to-RGB shader probe, and its front camera produced a real
non-uniform RGB capture through the production bridge.

## Validated implementation

- Exact implementation commit:
  `21cdff23da91fd53bdd81b689f93d78e395d7c99`
- Commit message:
  `RMA-092: validate dark camera scenes without masking GPU faults`
- Hosted workflow: `RMA-091 CameraX Acquisition`
  - run `30952901855`
  - job `92139068169`
  - conclusion: success
- Self-hosted workflow: `Local Unity Android Validation`
  - run `30952901895`
  - job `92139068851`
  - runner: `kawa`
  - conclusion: success

## Implementation contract

The validated bridge provides:

- a three-slot Java ring with explicit `FREE`, `WRITING`, `READY`, and `LEASED`
  ownership states;
- exactly one CameraX-plane packing copy into detached direct Y/U/V buffers;
- row-stride and pixel-stride handling for `YUV_420_888`;
- no retention of `ImageProxy` or CameraX-owned plane buffers;
- tokenized close-once leases that prevent overwrite while Unity is sampling;
- JNI direct-buffer addresses with exact plane-length validation;
- reusable Unity `TextureFormat.R8` Y/U/V textures;
- a reusable linear ARGB32 output `RenderTexture`;
- retained BT.601/BT.709 and limited/full-range YUV conversion shader;
- crop, right-angle rotation, and front-camera mirror mapping;
- exact session/sequence/timestamp correspondence;
- sampleable-output invalidation on stop, switch, pause, revocation, fault, or
  component destruction;
- CPU reference conversion limited to tests/development builds;
- no production GPU readback or PNG conversion.

## Hosted contract validation

Hosted run `30952901855` passed the permanent managed and static policy gate on
the exact implementation SHA. The gate covers:

- descriptor validation and immutable texture metadata;
- CPU-reference BT.601 and BT.709 known-answer vectors;
- crop, rotation, mirror, output-size, timestamp, and plane-length tests;
- the bounded Java ownership ring and direct-buffer lease contract;
- exactly-once Android buffer release behavior;
- the retained `Resources/ReachyCameraYuv420ToRgb.shader` path;
- production prohibition of `ReadPixels`, `GetPixels32`, and PNG encoding;
- acceptance-only readback and physical stage diagnostics;
- bootstrap installation through both RMA-091 and RMA-092 launch extras;
- permanent physical-device script requirements, stale-frame checks, timestamp
  correspondence, evidence filenames, and SHA-256 manifests.

## Self-hosted regression validation

Self-hosted run `30952901895` passed all steps on the exact implementation SHA:

1. deterministic Unity presentation generation;
2. production MuJoCo runtime staging;
3. Unity edit-mode and play-mode tests;
4. ARM64/API-26 IL2CPP APK build;
5. APK architecture and minimum-API verification;
6. installed RMA-090 camera discovery acceptance;
7. installed RMA-091 CameraX acquisition acceptance;
8. installed RMA-092 camera GPU texture acceptance;
9. installed RMA-022 lifecycle acceptance;
10. installed authoritative-rendering acceptance;
11. verified APK and all evidence uploads.

No downstream gate was skipped. The final commit status was published as success.

## Physical-device environment

The installed APK was exercised on the permanent physical runner device:

- manufacturer: LGE;
- model: LG-H872;
- Android API: 26;
- ABI: `arm64-v8a`;
- graphics API: Vulkan;
- GPU: Adreno 530.

The device ran the production texture bridge and the development-only evidence
components. The evidence components observed rather than replaced the production
Y/U/V uploads, material, shader, and RGB render target.

## Rear-camera evidence

The first rear session reported:

- output rotation: 90 degrees;
- accepted frame sequence: 13;
- closest stage-marker sequence: 12;
- live Y range: 0–5;
- live U range: 127–127;
- live V range: 127–127;
- synthetic physical-GPU RGB range: 0–255;
- synthetic alpha: opaque;
- evidence mode: `dark_scene_synthetic_gpu_probe`.

The rear RGB output was black, which is the correct result for those neutral,
below-black/black limited-range source values. It was not accepted merely because
it was black: the retained shader first had to convert a deterministic YUV
gradient to a wide opaque RGB range on the same Vulkan/Adreno path, and the live
source and live output had to agree.

## Rotated rear-camera evidence

After orderly CameraX stop, forced Android display rotation, and a new rear
session, the evidence reported:

- output rotation: 0 degrees;
- accepted frame sequence: 7;
- closest stage-marker sequence: 4;
- live Y range: 0–1;
- live U range: 126–127;
- live V range: 127–127;
- synthetic physical-GPU RGB range: 0–255;
- evidence mode: `dark_scene_synthetic_gpu_probe`.

The descriptor rotation changed from 90 to 0 degrees. The bridge therefore
proved that display/camera orientation changes propagate into the RGB texture
contract even though the unattended rear scene itself remained dark.

## Front-camera evidence

After orderly rear teardown and explicit front-camera selection, the front stage
used the stronger live-camera path:

- output rotation: 0 degrees;
- accepted frame sequence: 14;
- live Y range: 0–255;
- live U range: 103–160;
- live V range: 113–158;
- evidence mode: `live_camera_texture`;
- remote RGB texture: `front-texture.png`;
- RGB dimensions: 1280×960;
- descriptor lens facing: front;
- descriptor mirror flag: true.

Visual inspection of the captured RGB texture showed distinct warm wall tones,
dark metal bars, cables, highlights, and shadows. This is direct physical-device
evidence that live CameraX plane data crossed the detached JNI boundary, uploaded
into the Unity textures, and produced plausible non-uniform color through the
retained shader. The corresponding device screenshot showed that the same RGB
render target was also drawn in the running Unity application.

No person-identification claim is part of this evidence.

## Timestamp, stale-frame, and ownership evidence

Each rear, rotated-rear, and front stage required:

- a ready texture bridge state;
- positive and monotonic session sequence and timestamps;
- at least one exact acquisition/texture metadata match;
- matching timestamp, camera ID, lens facing, and output rotation;
- valid output dimensions;
- correct front-only mirror declaration;
- a declared, non-unknown YUV standard and range;
- zero accepted stale frames.

The permanent RMA-091 regression in the same run also passed rapid lifecycle,
backpressure, switching, rotation, and ImageProxy-close behavior. The subsequent
RMA-022 and authoritative-rendering gates passed, proving that the camera bridge
did not regress application pause/resume or the fixed simulation/rendering
contract.

## Evidence artifacts

Artifacts from self-hosted run `30952901895`:

- RMA-092 camera texture report: artifact `8910067968`
  - digest
    `sha256:b3fa33bc814c153a2440d6928f04e64fc15f04410090d37e46c7b397aa7a8394`
- RMA-091 camera acquisition report: artifact `8910040108`
  - digest
    `sha256:15a7a7efea2b6de6ed7bd9b0e0be974f98477b676a147a5306e365c06138e4dc`
- RMA-090 camera discovery report: artifact `8910004662`
  - digest
    `sha256:345a91bb35207451f407b0da1f13b5e11aa858dd5c8117ac72ca0b5be7943e75`
- Unity test results: artifact `8909937777`
  - digest
    `sha256:045d83829370d5b419d3edf13732f089f7c6072eb68d418bc16df1e755fb9610`
- Lifecycle report: artifact `8910095377`
  - digest
    `sha256:a1e455a774d2c36c0bd13c948f6c9404905cfe8dbec6ef8795405f699643091d`
- Authoritative-rendering report: artifact `8910111336`
  - digest
    `sha256:234057fd0f50e441fad84eb7cbdd21ff7e946c7f7062562d2127c26b57302dcc`
- Verified ARM64/API-26 APK: artifact `8910139813`
  - digest
    `sha256:972c59ecb5cd6b468ef159532ad9ce3eebe4f6521613e0cfa4cb95e78bfbd972`

The RMA-092 report contains per-stage JSON reports, stage-marker files, verdicts,
device screenshots, the live front RGB texture, a machine-readable summary, and
`SHA256SUMS`.

## Acceptance criteria disposition

- **Convert CameraX frames to a Unity-consumable GPU texture with minimal
  copies:** passed. The production path packs detached Y/U/V once, uploads R8
  planes, and performs RGB conversion on the GPU without a CPU RGB copy.
- **Correct YUV conversion, color range, rotation, and front-camera mirroring:**
  passed through CPU known-answer tests, the deterministic physical-GPU probe,
  real front-camera color evidence, changed rear rotation, and front mirror
  descriptor enforcement.
- **Maintain timestamp correspondence:** passed with exact metadata matches and
  zero stale accepted frames.
- **Add a CPU reference conversion for tests only:** passed; the reference is
  excluded from non-development production builds and unused by the runtime
  conversion path.
- **Preview and analysis show correct orientation and color on representative
  devices:** passed on the LG-H872/API-26 Vulkan/Adreno device for rear rotation
  handling and live front-camera color; both preview and analysis expose the
  same authoritative render target.
- **No stale or closed camera buffer is sampled:** passed by detached ownership,
  tokenized leases, zero stale-frame evidence, permanent lifecycle tests, and
  the RMA-091 ImageProxy-close regression.

## Architecture reference

The permanent ownership, conversion, lifecycle, and dark-scene evidence design
is documented in:

`docs/architecture/ANDROID_CAMERA_GPU_TEXTURE_BRIDGE.md`
