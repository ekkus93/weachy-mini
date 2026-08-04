# Android Camera GPU Texture Bridge

## Scope

RMA-092 converts CameraX `YUV_420_888` analysis frames into a Unity-consumable
RGB `RenderTexture` while preserving frame identity, timestamp, crop, rotation,
lens facing, mirroring, and declared YUV color interpretation.

The bridge is an input pipeline. It does not replace the fixed Unity
front/three-quarter presentation camera, perform Reachy-eye reprojection, or
provide the Phase 11 homography/validity-mask contract. RMA-100 through RMA-104
consume this output in later work.

## Data flow

The production path is:

```text
CameraX ImageAnalysis
  -> ReachyCameraTextureFrameBridge.publish(ImageProxy, metadata)
  -> packed detached direct Y/U/V ByteBuffers in a bounded three-slot ring
  -> Android FrameLease
  -> ReachyAndroidJavaCameraTextureFrameLease JNI direct-buffer addresses
  -> Unity R8 Y/U/V Texture2D uploads
  -> retained YUV conversion shader
  -> RGB RenderTexture
  -> preview and analysis consumers
```

The Java analyzer remains the sole owner of every `ImageProxy`. The texture
bridge copies each source plane once into detached direct buffers before the
analyzer's existing `finally` block closes the proxy. Unity never retains or
samples a CameraX-owned plane after closure.

## Bounded ownership and backpressure

`ReachyCameraTextureFrameBridge` owns a fixed ring of three slots. Each slot is
in exactly one state:

- `FREE`: available for a new publication;
- `WRITING`: reserved by the CameraX analyzer while plane bytes are packed;
- `READY`: complete and available to Unity;
- `LEASED`: held by one Unity-side frame lease and not overwritable.

Publication first uses a free slot. When no free slot exists, it may replace the
oldest unleased ready slot. It never blocks the analyzer and never overwrites a
leased slot. If all slots are leased or being written, the incoming texture frame
is dropped and the drop is visible in diagnostics.

A frame lease carries a generation, session ID, token, sequence, and timestamp.
Closing a lease releases only the slot whose token still matches. Duplicate or
stale close attempts cannot release a newer frame. Session start/stop invalidates
unleased stale slots, while an outstanding lease remains valid until its single
close operation.

## Plane packing

CameraX may expose row padding and interleaved chroma through arbitrary row and
pixel strides. The Java bridge therefore packs each plane into a tightly packed
buffer:

- Y length: `width * height`;
- U length: `ceil(width / 2) * ceil(height / 2)`;
- V length: the same chroma length.

Every source index is checked against the plane buffer's position and limit.
Invalid stride metadata, missing planes, unsupported pixel format, or a short
source plane fail visibly instead of producing a partially initialized frame.
The original `ImageProxy` and its plane buffers are not retained.

This is a minimal-copy design rather than a zero-copy EGL/Vulkan import design:
there is one required CameraX-to-detached-buffer copy and one Unity texture
upload for each Y, U, and V plane. It avoids an additional CPU RGB conversion
and keeps preview/analysis output on the GPU.

## JNI lease boundary

`ReachyAndroidJavaCameraTextureFrameLease` reads immutable frame metadata from
the Java lease and obtains native addresses with
`AndroidJNI.GetDirectBufferAddress`. A null or non-direct buffer is a hard
failure. The C# lease keeps the Java lease and all three `ByteBuffer` wrappers
alive until `Dispose`, calls Java `close()` exactly once, and then disposes the
JNI wrappers.

The Unity bridge validates that all three addresses are nonzero and that every
reported plane length exactly matches the descriptor before uploading data.

## Unity texture resources

`ReachyAndroidCameraTextureBridge` owns:

- one linear, non-mipmapped `TextureFormat.R8` texture for each Y/U/V plane;
- one retained conversion `Material`;
- one linear `RenderTextureFormat.ARGB32` RGB output.

Plane textures are reused while dimensions remain stable. The RGB render target
is reused while the rotated/cropped output dimensions remain stable. Resources
are destroyed on session invalidation, lifecycle teardown, component
destruction, or an upload/conversion fault.

The bridge publishes `Ready` only after all plane uploads and `Graphics.Blit`
complete. Its immutable snapshot contains the exact descriptor associated with
the current output texture. `PreviewTexture` and `AnalysisTexture` intentionally
refer to the same authoritative RGB render target.

## Shader retention and conversion

The production shader is retained at:

`Assets/ReachyMini/Runtime/Resources/ReachyCameraYuv420ToRgb.shader`

Its stable shader name is:

`Hidden/ReachyMini/CameraYuv420ToRgb`

The shader receives:

- Y, U, and V R8 textures;
- normalized crop scale and offset;
- clockwise quarter-turn count;
- front-camera mirror flag;
- BT.601 or BT.709 selection;
- limited-range or full-range selection.

Output UVs are converted to top-left camera-image coordinates, mirrored for the
front camera when required, inverse-rotated into source coordinates, mapped into
the CameraX crop, clamped by half a luma texel, and sampled from all three planes.

Limited-range conversion expands Y from nominal 16–235 and chroma from 16–240.
Full-range conversion retains normalized luma and centers chroma at 0.5. The
shader then applies the declared BT.601 or BT.709 matrix and saturates RGB into
the output render target.

The current Android bridge declares BT.709 for HD-class frames and BT.601 for
smaller frames, with limited range. That declaration is carried with each frame
and is covered by reference tests. Future platform metadata may replace this
heuristic only through an explicit, tested contract change.

## Orientation and mirroring

The output descriptor separates:

- source width/height;
- crop rectangle;
- sensor orientation;
- output rotation;
- lens facing;
- mirror state.

Output dimensions are crop width/height for 0° or 180° and swapped for 90° or
270°. Front-camera descriptors must be mirrored; rear and external descriptors
must not be mirrored. Invalid or non-right-angle orientation values are rejected.

The physical-device acceptance rotates the Android display between rear-camera
sessions and requires a changed output rotation. It then switches to the front
camera and requires the descriptor and rendered path to carry the front mirror
contract.

## Timestamp and session correspondence

Every texture descriptor carries the CameraX timestamp in nanoseconds together
with its session ID and sequence. Unity rejects:

- a frame from another active session;
- a frame from another camera ID;
- a sequence not newer than the last uploaded frame;
- a detached frame older than the currently published acquisition metadata.

Acceptance evidence observes exact acquisition/texture sequence matches and
requires the metadata timestamp, camera ID, lens facing, and rotation to agree.
Stopping, switching, revoking permission, pausing, or changing sessions clears
the sampleable texture state so stale RGB output cannot be presented as current.

## CPU reference boundary

`ReachyCameraYuv420CpuReference` exists only under
`UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`. It implements a deterministic CPU
reference for test vectors and is not used by the production preview or analysis
path. Production code is guarded against `ReadPixels`, `GetPixels32`, PNG
encoding, or other GPU-to-CPU readback.

## Physical-device evidence and dark scenes

`ReachyCameraTextureEvidence` and
`ReachyCameraTextureStageDiagnostics` are development/acceptance components.
They do not change production texture ownership or add production readback.

The acceptance policy has two fail-closed evidence modes:

1. **Live camera texture.** A real opaque, non-uniform RGB texture is captured
   and validated. This is the preferred mode.
2. **Dark-scene physical GPU proof.** An unattended camera may legitimately face
   a covered or unlit surface. A uniform black live output is accepted only when
   all of the following hold on the same physical device and graphics API:
   - a deterministic synthetic YUV gradient passes through the retained shader
     and produces a wide opaque RGB range;
   - the live Y/U/V texture backing is measured as neutral limited-range black;
   - the live RGB readback is correspondingly black;
   - the stage marker sequence closely tracks the current live descriptor;
   - all timestamp, session, orientation, mirror, dimensions, and stale-frame
     invariants remain valid.

This distinction prevents a covered test camera from creating a false failure
without allowing a broken shader, missing material, invalid render target,
zeroed JNI upload, or unexplained black output to pass.

## Validation gates

Permanent coverage consists of:

- managed warnings-as-errors and Unity tests for descriptor validation, plane
  sizes, mapping, crop, rotation, mirroring, timestamps, and CPU reference
  BT.601/BT.709 vectors;
- hosted static contracts for Java ownership, bounded slots, direct buffers,
  retained shader paths, production no-readback policy, bootstrap wiring, and
  acceptance-script requirements;
- ARM64/API-26 IL2CPP build and APK verification;
- installed RMA-090 discovery and RMA-091 acquisition regressions;
- installed rear, rotated-rear, and front RMA-092 physical-device stages;
- installed RMA-022 lifecycle and authoritative-rendering regressions.

The accepted implementation and exact evidence are recorded in
`docs/validation/RMA_092_GPU_TEXTURE_BRIDGE_VALIDATION_2026-08-04.md`.
