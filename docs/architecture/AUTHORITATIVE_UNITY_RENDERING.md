# Authoritative Unity rendering

This document defines the RMA-050 through RMA-052 presentation contract. MuJoCo remains the only source of robot body transforms. Unity imports and renders visual assets, interpolates immutable MuJoCo body-pose snapshots, and detects any competing transform writer. It does not simulate or cosmetically replace robot motion.

## Deterministic visual asset path

The source asset flow is:

1. `scripts/import_reachy_assets.py` imports the pinned Reachy Mini MJCF and every referenced mesh while preserving provenance.
2. `scripts/prepare_reachy_unity_assets.py` converts STL files to Unity-importable OBJ files and writes `UNITY_RENDER_MAP.json`.
3. Unity consumes that render map to create the generated Reachy presentation prefab.

Generated assets remain excluded from Git because they are deterministic derivatives of the pinned upstream package. Missing, malformed, mismatched, or escaping asset paths must fail visibly. The generated presentation contains the complete MJCF body hierarchy, converted visual geometry and materials, one `ReachyPresentationBody` marker for each model body, and a fixed Unity-only presentation camera and lighting. The MuJoCo `studio_close` and `eye_camera` definitions remain model metadata and are never the application presentation camera.

## Coordinate conversion

The source model uses a right-handed, Z-up MuJoCo world frame and `w, x, y, z` quaternions. Unity uses a left-handed, Y-up frame. The explicit runtime conversion is:

```text
unity_position = (mujoco_x, mujoco_z, mujoco_y)
unity_quaternion_xyzw = (-mujoco_x, -mujoco_z, -mujoco_y, mujoco_w)
```

The quaternion is normalized before use. This is the runtime equivalent of the matrix rule recorded by `UNITY_RENDER_MAP.json`:

```text
R_unity = M * R_mujoco * inverse(M)
```

where `M` swaps the MuJoCo Y and Z axes. The conversion is presentation-only and never feeds a Unity transform back into MuJoCo.

## Snapshot and interpolation contract

`IReachyAuthoritativePoseSource` publishes the latest two immutable `ReachyAuthoritativePoseSnapshot` objects. Each snapshot contains:

- a monotonically increasing sequence within one continuity epoch;
- simulation time;
- a discontinuity identifier changed by reset or model reload;
- a fixed ordered set of MuJoCo world-space body poses.

`ReachyAuthoritativeRenderer` validates body count, canonical model-body order, body identity, sequence order, time order, and finite poses before writing transforms. Rendering is evaluated against simulation timestamps, not Unity frame count. The default presentation time is slightly behind the newest snapshot so interpolation occurs between the two authoritative samples.

A reset or model reload changes the discontinuity identifier. The renderer then snaps to the newer snapshot and does not interpolate through an impossible pose. A snapshot mismatch, invalid ordering, or missing binding faults the renderer; it does not retain a moving cosmetic fallback.

`ReachyAuthoritativePoseBuffer` is thread-safe and retains only the newest pair. Publishing an out-of-order sample in the same continuity epoch is rejected.

## Invariant enforcement

After applying a pose, the renderer records the exact expected Unity world transform for every authoritative body. On the next late-frame update it checks for position or rotation drift before applying another snapshot. Drift faults and disables the renderer, making Animator, script, Timeline, or physics interference visible instead of silently overwriting it.

The authoritative hierarchy rejects these component classes on a mapped body or its visual descendants:

- `Rigidbody` and `Rigidbody2D`;
- `Joint` and `Joint2D`;
- `ArticulationBody`;
- `Animator` and legacy `Animation`;
- `PlayableDirector`.

The renderer executes late in the frame and performs no successful-path collection or array allocation. Body bindings and expected-pose arrays are created during configuration.

## Current integration boundary

The coordinate conversion, immutable pose-pair buffer, simulation-time interpolation, discontinuity handling, structural rejection, and drift tests are implemented without authorizing a fake production source. The current production `reachy_sim` backend still reports unavailable and its state ABI currently exposes only the state header. Therefore the application must remain visibly unbound until the production MuJoCo backend publishes the ordered body-pose payload required by `IReachyAuthoritativePoseSource`.

Reference fixtures and editor tests prove the presentation contract, but they are not a runtime fallback and are not packaged as simulated motion. RMA-051/RMA-052 physical acceptance remains blocked until the real backend is connected and device evidence demonstrates that sleep/wake, head, Stewart links, and antenna transforms originate from MuJoCo snapshots.
