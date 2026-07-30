# RMA-050 Unity prefab validation

**Date:** 2026-07-30
**Source implementation commit:** `c821d591ce1ad50d08b8861edb1a8abeb17b53d5`
**Clean validated commit:** `28c582e9e23c5b61e0c8dfac0d8b6f423064ac40`

## Scope

This record covers RMA-050 only: deterministic visual-mesh and material import,
the complete Unity presentation hierarchy, model scale and coordinate conversion,
the fixed presentation camera, basic lighting, and independence from the simulated
Reachy camera frame. RMA-051 state interpolation and RMA-052 rendering-invariant
closure remain separate tasks.

## Pinned identity

The presentation derives from the clean Pollen Robotics source checkout at
`a739a6e461eb6d722901f1cfc225265ffc85c28d`. The authoritative MJCF SHA-256 is
`efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`.
Generated assets remain deterministic derivatives and are not committed.

The source path is:

1. import the pinned MJCF, meshes, license, provenance, and model map;
2. convert STL triangles into Unity-coordinate OBJ files;
3. emit `UNITY_RENDER_MAP.json` with source/output hashes, scale metadata,
   materials, body hierarchy, visual instances, camera exclusions, and transform
   authority;
4. build the Unity prefab and scene from that manifest;
5. bind the optional diagnostic overlay from the imported model map.

## Full-model prefab contract

The pinned presentation contains:

- 18 non-world MuJoCo bodies in canonical index order;
- 17 upstream body names plus deterministic presentation identity `__body_15`;
- one `ReachyPresentationBody` transform for every body;
- 161 visual-geometry instances;
- 41 distinct visual meshes referenced by those instances;
- 41 distinct materials, including the source lens transparency;
- no Rigidbody, Joint, ArticulationBody, Animator, source MuJoCo camera, or other
  presentation-side physics fallback.

The manifest validator now rejects body index/order drift, duplicate or invalid
paths, unknown parents, malformed or duplicate meshes/materials/visual paths,
non-finite scale or color values, invalid hashes, visual-to-mesh output mismatch,
and inclusion of either source camera.

## Scale and coordinate contract

`UNITY_RENDER_MAP.json` records each source mesh scale explicitly as
`source_scale` and requires `scale_baked_into_vertices=true`. The converter applies
that scale before the basis transformation, so generated Unity transforms retain
`localScale == Vector3.one` and scale cannot be applied twice.

The conversion remains:

- MuJoCo right-handed Z-up to Unity left-handed Y-up;
- position `(x, y, z)` to `(x, z, y)`;
- normalized MuJoCo quaternion `wxyz` to Unity-basis manifest quaternion
  `(w, -x, -z, -y)`;
- reversed mesh winding to preserve outward-facing geometry after handedness
  conversion.

The official pinned meshes currently use the implicit unit source scale. A focused
synthetic fixture applies nonuniform scale `(2, 3, 4)` and verifies the generated
vertices, normals, winding, manifest scale metadata, and failure on non-finite
scale.

## Scene contract

The generated scene contains one root-level Unity-only presentation camera:

- framing ID `fixed_front_three_quarter`;
- user navigation disabled;
- position `(0.62, 0.36, -0.62)` metres;
- look target `(0, 0.16, 0)`;
- 35-degree field of view;
- near/far clips `0.01` and `20` metres;
- one AudioListener and a fixed solid background.

It also contains one root-level directional key light with intensity `1.15`, soft
shadows, rotation `(38, -32, 0)` degrees, and flat ambient lighting. Camera and
light are not descendants of the Reachy prefab. The MuJoCo `studio_close` and
`eye_camera` entries remain excluded model metadata and cannot become the
application presentation camera.

## Failure and regression coverage

Focused Python tests cover deterministic conversion, source immutability,
nonuniform scale baking, coordinate/quaternion conversion, material fidelity,
camera exclusion, malformed STL rollback, unsafe output paths, and non-finite mesh
scale rejection.

Unity EditMode/PlayMode coverage verifies exact body/visual/mesh/material counts,
canonical parent relationships, generated asset references, finite mesh bounds and
material colors, unit presentation scales, no embedded camera in the prefab, exact
scene camera/light configuration, root-level independence, one enabled generated
build scene, and absence of Unity physics or animation fallback components.

## Validation results

Hosted Quality Gates run `30591010118` passed on clean commit
`28c582e9e23c5b61e0c8dfac0d8b6f423064ac40`, including Ruff, actionlint,
ShellCheck, static policy, all Python converter tests, pinned official-model visual
conversion, native warnings/sanitizers, managed tests, and Android lint/tests.

Self-hosted `kawa` run `30591010149` passed on the same exact commit, including
generated-presentation preparation, production ARM64 MuJoCo staging, Unity
EditMode/PlayMode tests, ARM64 API-26 IL2CPP build and verification, installed LG
G6 lifecycle acceptance, physical authoritative-rendering acceptance, evidence
uploads, and APK upload.

## Result

RMA-050 has a deterministic, source-bound and fail-closed Unity prefab/scene
contract. The complete rendered body hierarchy, visual assets, scale/basis mapping,
presentation camera, and lighting are verified in generated Unity content and on
the physical Android artifact. This record does not close RMA-051 or RMA-052.
