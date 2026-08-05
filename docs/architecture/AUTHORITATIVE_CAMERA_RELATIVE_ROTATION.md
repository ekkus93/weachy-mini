# Authoritative Camera Relative Rotation

## Status

This document defines the RMA-101 implementation contract. It extends the
RMA-100 coordinate and calibration contract and is normative for the dynamic
rotation consumed by RMA-102.

## Authoritative model binding

The pinned Reachy Mini MJCF is:

- repository: `pollen-robotics/reachy_mini`;
- commit: `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- model SHA-256:
  `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`.

The model defines:

- the named optical site `camera_optical`;
- the fixed camera `eye_camera`;
- an anonymous fixed-offset body containing `eye_camera`.

The generated authoritative presentation assigns that anonymous body the stable
canonical identity `__body_15`, matching MuJoCo body ID 15. The existing native
authoritative state already serializes solved `mjData.xquat` values for every
non-world body. RMA-101 therefore consumes body 15 from the published solved
state; it does not add an alternate target-orientation channel.

The optical site differs from the camera body by the fixed proper rotation:

```text
R_cameraBody_from_optical =
[ 1  0  0 ]
[ 0 -1  0 ]
[ 0  0 -1 ]
```

The neutral optical frame in MuJoCo world coordinates is:

```text
R_mujocoWorld_from_neutralOptical =
[ 0  0  1 ]
[-1  0  0 ]
[ 0 -1  0 ]
```

These values and their provenance are retained in
`models/reachy-mini/camera-reprojection-binding.json`.

## Dynamic rotation

For each accepted authoritative state:

```text
R_world_from_currentOptical =
    R_world_from_cameraBody(actual mjData.xquat)
    * R_cameraBody_from_optical

R_currentReachy_from_neutralReachy =
    inverse(R_world_from_currentOptical)
    * R_world_from_neutralOptical
```

The inverse is the transpose because every accepted operand is a proper
rotation. Body position is not read by the calculator. Level 1 therefore removes
translation by construction rather than calculating it and discarding it later.

The complete current phone-to-Reachy rotation is:

```text
R_currentReachy_from_currentPhone =
    R_currentReachy_from_neutralReachy
    * R_neutralReachy_from_neutralPhone
    * R_neutralPhone_from_currentPhone
```

The middle term comes from the selected RMA-100 calibration profile. The final
term is an explicit phone/camera orientation sample. An identity final term
means the phone remains at its calibrated neutral physical orientation.

## Correspondence and failure policy

Each successful sample carries:

- authoritative model hash;
- authoritative sequence;
- simulation time;
- continuity ID;
- phone orientation timestamp;
- authoritative camera body ID.

Within one continuity, a sequence that does not advance is rejected as stale.
A continuity change permits a sequence reset. Capture fails closed when:

- the calibration model key differs from the pinned binding;
- model hash is zero;
- the authoritative body count differs from the pinned model;
- body ID 15 is missing or duplicated;
- the body quaternion is invalid;
- no state is currently published;
- an already-consumed sequence is presented again.

Requested head targets, Unity presentation transforms, and visual interpolation
are not inputs. Only the solved MuJoCo body quaternion published by the
simulation worker is authoritative.

## Sign contract

The Reachy optical frame remains `+X` image-right, `+Y` image-down, `+Z`
forward. For a positive current-camera rotation:

- positive yaw about optical `+Y` maps neutral forward toward current `-X`;
- positive pitch about optical `+X` maps neutral forward toward current `+Y`;
- positive roll about optical `+Z` maps neutral right toward current `-Y`.

These signs follow from mapping neutral rays into the current camera frame,
which uses the inverse of the camera's world rotation.

## RMA-102 handoff

RMA-102 must consume `CurrentReachyFromCurrentPhone` and build:

```text
H = K_reachy
    * R_currentReachy_from_currentPhone
    * inverse(K_phone)
```

It must preserve the authoritative sequence, simulation time, continuity ID,
and phone timestamp through the GPU-warp evidence path.
