# Authoritative Unity rendering

This document defines the RMA-050 through RMA-052 presentation contract. MuJoCo
remains the only source of robot body transforms. Unity imports and renders
visual assets, interpolates immutable MuJoCo body-pose snapshots, and detects any
competing transform writer. It does not simulate or cosmetically replace robot
motion.

## Deterministic visual asset path

The source asset flow is:

1. `scripts/import_reachy_assets.py` imports the pinned Reachy Mini MJCF and
   every referenced mesh while preserving provenance.
2. `scripts/prepare_reachy_unity_assets.py` converts STL files to
   Unity-importable OBJ files and writes `UNITY_RENDER_MAP.json`.
3. Unity consumes that render map to create the generated Reachy presentation
   prefab.
4. `ReachyPresentationPipeline` binds the generated optional diagnostic overlay
   from the imported `MODEL_MAP.json` body and joint topology.

Generated assets remain excluded from Git because they are deterministic
derivatives of the pinned upstream package. Missing, malformed, mismatched, or
escaping asset paths fail visibly. The generated presentation contains the
complete MJCF body hierarchy, converted visual geometry and materials, one
`ReachyPresentationBody` marker for every non-world body, and a fixed Unity-only
presentation camera and lighting. The MuJoCo `studio_close` and `eye_camera`
definitions remain model metadata and are never the application presentation
camera.

### RMA-050 generated prefab contract

The full pinned presentation contains 18 non-world body transforms, 161 visual
instances, 41 referenced visual meshes, and 41 materials. Body indices and parent
paths are canonical and complete; the unnamed source body receives only the
presentation identity `__body_15`.

Each generated mesh entry records source path/hash, source scale, output path/hash,
triangle count, and `scale_baked_into_vertices=true`. Scale is applied before the
coordinate-basis conversion, while generated Unity body and visual transforms use
unit local scale. This prevents missing or double-applied MJCF mesh scale.

The scene contract fixes the independent camera position, target, field of view,
clip planes, background, AudioListener, directional key light, shadows, and
ambient illumination. Camera and light are root objects rather than descendants of
the authoritative robot hierarchy. Exact validation evidence is recorded in
`docs/validation/RMA_050_UNITY_PREFAB_VALIDATION_2026-07-30.md`.

The pinned model has 18 non-world bodies. Seventeen have upstream names. The one
unnamed body is assigned the deterministic canonical identity `__body_15` by the
asset generator and runtime mapper. The generated contract requires all 18
identities to be nonempty, unique, and in canonical MuJoCo body order.

## Coordinate conversion

The source model uses a right-handed, Z-up MuJoCo world frame and `w, x, y, z`
quaternions. Unity uses a left-handed, Y-up frame. The explicit runtime
conversion is:

```text
unity_position = (mujoco_x, mujoco_z, mujoco_y)
unity_quaternion_xyzw = (-mujoco_x, -mujoco_z, -mujoco_y, mujoco_w)
```

The quaternion is normalized before use. This is the runtime equivalent of the
matrix rule recorded by `UNITY_RENDER_MAP.json`:

```text
R_unity = M * R_mujoco * inverse(M)
```

where `M` swaps the MuJoCo Y and Z axes. The conversion is presentation-only and
never feeds a Unity transform back into MuJoCo.

## Production state source

The production state-format-v1 payload contains model hash, sequence,
simulation time, continuity identity, qpos, qvel, actuator observations,
canonical body IDs and poses, calibration identity, warning counts, constraint
counts, and maximum residuals.

The managed state reader and parser validate envelope version, total size,
checked offsets, array counts, canonical IDs, model identity, finiteness, and
quaternion validity. A malformed, mismatched, stale, or unavailable source fails
visibly. It is not replaced by reference fixtures or a kinematic source.

The dedicated simulation worker owns mutable stepping and publishes immutable
state. The renderer reads managed pose snapshots; it does not consurrently
operate on MuJoCo. Native ABI version 2 additionally enforces one nonblocking
exclusive operation per handle and reports same-handle contention as retryable
`HANDLE_BUSY`.

## Snapshot and interpolation contract

`IReachyAuthoritativePoseSource` publishes the latest two immutable
`ReachyAuthoritativePoseSnapshot` objects. Each snapshot contains:

- a monotonically increasing sequence within one continuity epoch;
- simulation time;
- a discontinuity identifier changed by reset or model reload;
- a fixed ordered set of MuJoCo world-space body poses.

`ReachyAuthoritativeRenderer` validates body count, canonical model-body order,
body identity, sequence order, time order, and finite poses before writing
transforms. Rendering is evaluated against simulation timestamps, not Unity
frame count. The default presentation time is slightly behind the newest
snapshot so interpolation occurs between the two authoritative samples.

A reset or model reload changes the discontinuity identifier. The renderer then
snaps to the newer snapshot and does not interpolate through an impossible pose.
A snapshot mismatch, invalid ordering, or missing binding faults the renderer;
it does not retain a moving cosmetic fallback.

`ReachyAuthoritativePoseBuffer` is thread-safe and retains only the newest pair.
Publishing an out-of-order sample in the same continuity epoch is rejected.

## Invariant enforcement

After applying a pose, the renderer records the exact expected Unity world
transform for every authoritative body. On the next late-frame update it checks
for position or rotation drift before applying another snapshot. Drift faults
and disables the renderer, making Animator, script, Timeline, or physics
interference visible instead of silently overwriting it.

The authoritative hierarchy rejects these component classes on a mapped body or
its visual descendants:

- `Rigidbody` and `Rigidbody2D`;
- `Joint` and `Joint2D`;
- `ArticulationBody`;
- `Animator` and legacy `Animation`;
- `PlayableDirector`.

The renderer executes late in the frame and performs no successful-path
collection or array allocation. Body bindings and expected-pose arrays are
created during configuration.

## Optional diagnostics

The generated prefab contains exactly one disabled
`ReachyPresentationDebugOverlay`. Its body-axis bindings come from the canonical
generated body mapping, and its 16 joint labels and body associations come from
the imported `MODEL_MAP.json`. The presentation pipeline rejects missing body
paths, duplicate or malformed joint entries, unexpected topology, and a
serialized overlay that does not retain the complete mapping.

Enabling the overlay draws local X/Y/Z body axes and joint labels without writing
any body transform or feeding data into MuJoCo. It is diagnostic presentation
only; it is not an animation, physics source, pose source, or fallback motion
path.

## Physical acceptance

Self-hosted run `30534082314` on commit
`c109b13b7909efee017d32352f4ba2a973cf1447` built and verified the production
ARM64 IL2CPP APK, installed it on the LG G6, and passed authoritative-rendering
acceptance after the RMA-030 native concurrency hardening.

The physical scenario verified:

- all 18 canonical body bindings and a nonzero production model hash;
- ordered authoritative sequences and simulation timestamps;
- body yaw and head motion;
- both antenna bodies;
- all six Stewart links;
- reset continuity advancement and discontinuity snapping;
- renderer structure and runtime health;
- no hidden kinematic fallback.

The device harness wakes and unlocks the phone, collapses system overlays,
acknowledges Android's immersive-mode confirmation, enables stay-awake, launches
the exact Unity activity, verifies focused-window ownership, captures structured
JSON and screenshot evidence, and restores the device power policy. A prior
black-screen timeout was traced to the phone being asleep, not to the production
pose source or native handle contention.
