# Official Reachy Mini model import contract

RMA-040 uses the Pollen Robotics Reachy Mini MJCF as an immutable, pinned solver
input. The source lock is `third_party/reachy-mini-source.lock.json`.

## Source identity

The accepted source is:

- repository: `https://github.com/pollen-robotics/reachy_mini.git`;
- commit: `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- model: `src/reachy_mini/descriptions/reachy_mini/mjcf/reachy_mini.xml`;
- model SHA-256: `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`.

The importer accepts only a clean checkout at that exact commit. It copies the
MJCF, every mesh referenced by the model asset section, and the upstream license.
The imported MJCF and mesh bytes are not rewritten. `PROVENANCE.json` records the
source commit and the size and SHA-256 digest of every copied file.

## Topology identity

The source lock and generated `MODEL_MAP.json` require:

- 18 non-world bodies, including 17 named bodies and one stable anonymous body;
- 16 joints: body yaw, six active Stewart hinges, seven passive ball joints, and
  two antenna hinges;
- 9 position actuators mapped to the expected joints;
- 5 Stewart-platform equality connections;
- 13 named sites and 2 source cameras.

The lock contains the complete ordered body-path list, not only representative
names. This makes body renaming, reparenting, insertion, removal, or order drift a
model-import failure before native state indices or Unity transform identities can
change. The anonymous camera-frame body is pinned at body index 15 and receives the
presentation identity `__body_15` without changing the source MJCF.

`MODEL_MAP.json` is the machine-readable body, joint, actuator, equality, site, and
camera map. The compiled MuJoCo validation additionally requires every named map
entry to resolve in the loaded model and requires the exact compiled dimensions:
19 bodies including world, 16 joints, 9 actuators, 5 equalities, 13 sites,
`nq=37`, and `nv=30`.

## Solver and presentation separation

The source cameras `studio_close` and `eye_camera` remain in the immutable MuJoCo
model as model metadata. They are not imported as Unity `Camera` objects and do not
control the presentation. `UNITY_RENDER_MAP.json` marks both source cameras as
excluded and declares an independent Unity-only fixed presentation camera.

The versioned `reachy_stl_to_unity_obj_v1` transformation converts visual STL data
to Unity coordinates and records source and output hashes. It is a presentation
artifact only. It does not modify the solver MJCF, collision geometry, body
hierarchy, joint ranges, inertias, actuator definitions, or equality constraints.

## Acceptance evidence

The automated contract covers:

- deterministic repeated import and complete file provenance;
- clean-checkout and exact-revision rejection;
- exact counts, names, joint types, actuator mappings, equality pairs, camera
  attributes, and complete ordered body paths;
- explicit rejection of body reparenting even when counts and names are unchanged;
- official-model compilation and 100 finite uncommanded MuJoCo steps;
- production ARM64 staging and Android native lifecycle loading;
- generation of all 18 Unity body transforms with unique canonical identities;
- explicit exclusion of MuJoCo source cameras from the Unity prefab.
