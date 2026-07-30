#!/usr/bin/env python3
"""Close only the verified RMA-050 documentation and checklist items."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs" / "IMPLEMENTATION_STATUS.md"
ARCHITECTURE = ROOT / "docs" / "architecture" / "AUTHORITATIVE_UNITY_RENDERING.md"


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(
            f"Expected exactly one match in {path.relative_to(ROOT)}, found {count}"
        )
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


todo_before = TODO.read_text(encoding="utf-8")
rma051_before = todo_before.split("## RMA-051", 1)[1].split("## RMA-052", 1)[0]
rma052_before = todo_before.split("## RMA-052", 1)[1].split("## RMA-060", 1)[0]

old_rma050 = """## RMA-050 — Build the Reachy Unity prefab

- [ ] Import visual meshes and materials through the asset pipeline.
- [ ] Create one Unity transform per rendered MuJoCo body or a documented mapped subset.
- [ ] Preserve model scale and coordinate conversion.
- [ ] Add the fixed presentation camera and basic lighting.
- [ ] Keep the presentation camera independent of Reachy's simulated camera frame.

"""
new_rma050 = """## RMA-050 — Build the Reachy Unity prefab

**Status:** Complete (2026-07-30)

- [x] Import visual meshes and materials through the asset pipeline.
- [x] Create one Unity transform per rendered MuJoCo body or a documented mapped subset.
- [x] Preserve model scale and coordinate conversion.
- [x] Add the fixed presentation camera and basic lighting.
- [x] Keep the presentation camera independent of Reachy's simulated camera frame.

**Completion evidence**

- The deterministic asset path imports the pinned MJCF, meshes, license,
  provenance, and model map; converts source STL triangles into Unity-coordinate
  OBJ files; emits `UNITY_RENDER_MAP.json`; and generates the prefab and scene.
- The prefab contains all 18 non-world MuJoCo bodies in canonical index and parent
  order, 161 visual instances, 41 referenced visual meshes, and 41 materials. The
  anonymous source body is represented only by deterministic presentation identity
  `__body_15`.
- Every mesh entry records source/output hashes, source scale, triangle count, and
  that scale was baked into vertices. Generated body and visual transforms retain
  unit local scale, preventing double application.
- The basis contract maps MuJoCo right-handed Z-up positions and `wxyz`
  quaternions into Unity left-handed Y-up coordinates, reverses mesh winding, and
  never modifies the solver model.
- The generated scene contains one root-level fixed front-three-quarter Unity
  camera and one root-level directional key light. The MuJoCo `studio_close` and
  `eye_camera` definitions remain excluded metadata and are not presentation
  objects.
- Hosted run `30591010118` and self-hosted `kawa` run `30591010149` passed on
  exact clean commit `28c582e9e23c5b61e0c8dfac0d8b6f423064ac40`, covering
  Python conversion tests, official-model conversion, Unity EditMode/PlayMode
  tests, ARM64 API-26 IL2CPP build, installed lifecycle acceptance, physical
  authoritative rendering, and artifact uploads.
- Detailed contracts and evidence are in
  `docs/architecture/AUTHORITATIVE_UNITY_RENDERING.md` and
  `docs/validation/RMA_050_UNITY_PREFAB_VALIDATION_2026-07-30.md`.

"""
replace_once(TODO, old_rma050, new_rma050)

todo_after = TODO.read_text(encoding="utf-8")
rma050_after = todo_after.split("## RMA-050", 1)[1].split("## RMA-051", 1)[0]
rma051_after = todo_after.split("## RMA-051", 1)[1].split("## RMA-052", 1)[0]
rma052_after = todo_after.split("## RMA-052", 1)[1].split("## RMA-060", 1)[0]
if rma050_after.count("- [x]") != 5 or "- [ ]" in rma050_after:
    raise SystemExit("RMA-050 must contain exactly five checked and no open boxes")
if rma051_after != rma051_before or rma052_after != rma052_before:
    raise SystemExit("RMA-051 or RMA-052 changed during RMA-050 closure")

replace_once(
    STATUS,
    """**Current implementation series:** RMA-041 machine-readable mechanical-parameter
fidelity, joint-limit provenance, source uncertainty binding, and fail-closed
calibration labeling after RMA-040 official-model import closure
""",
    """**Current implementation series:** RMA-050 deterministic Unity prefab and scene,
full-model presentation hierarchy, auditable mesh scale/basis conversion, and
fixed camera/lighting acceptance after RMA-041/RMA-042 model-integrity closure
""",
)

replace_once(
    STATUS,
    """### RMA-050 through RMA-052 — authoritative Unity rendering

The deterministic generated presentation contains all 18 non-world MuJoCo
bodies. The unnamed upstream body has the canonical identity `__body_15`; all
runtime identities are nonempty and unique.

""",
    """### RMA-050 — generated Unity prefab and scene

RMA-050 is complete. The deterministic presentation pipeline imports the exact
pinned visual assets, converts them into Unity-coordinate OBJ geometry, preserves
material RGBA, and emits a source-bound render manifest before generating the
prefab and scene. The prefab contains all 18 non-world MuJoCo bodies, 161 visual
instances, 41 referenced visual meshes, and 41 materials. The unnamed upstream
body has canonical presentation identity `__body_15`.

Mesh scale is now explicit audit data. Each manifest mesh records its source scale
and requires that scale to be baked into generated vertices; generated body and
visual transforms remain unit scale. Strict validation covers body index/parent
order, mesh and material identity, visual-to-body/mesh/material references, finite
poses/colors, source/output hashes, and exclusion of both MuJoCo cameras.

The generated scene locks one root-level fixed front-three-quarter Unity camera
and one root-level directional key light. Neither is parented under Reachy, and
the simulated `studio_close` and `eye_camera` definitions remain model metadata.
The detailed contract is in
[Authoritative Unity rendering](architecture/AUTHORITATIVE_UNITY_RENDERING.md),
with validation evidence in
[the RMA-050 validation record](validation/RMA_050_UNITY_PREFAB_VALIDATION_2026-07-30.md).

### RMA-051/RMA-052 — authoritative state mapping and invariant foundations

""",
)

replace_once(
    STATUS,
    """## Current validation evidence

""",
    """## Current validation evidence

- Hosted RMA-050 run `30591010118`: Ruff/actionlint/ShellCheck/static policy,
  focused converter failure coverage, exact pinned-model Unity visual conversion,
  native warnings/sanitizers, managed tests, and Android tests passed on
  `28c582e9e23c5b61e0c8dfac0d8b6f423064ac40`.
- Self-hosted RMA-050 run `30591010149`: generated presentation preparation,
  production ARM64 MuJoCo staging, expanded Unity prefab/scene tests, ARM64 API-26
  IL2CPP build/verification, installed lifecycle acceptance, physical
  authoritative-rendering acceptance, evidence uploads, and APK upload passed on
  the same exact commit.

""",
)

architecture_anchor = """Generated assets remain excluded from Git because they are deterministic
derivatives of the pinned upstream package. Missing, malformed, mismatched, or
escaping asset paths fail visibly. The generated presentation contains the
complete MJCF body hierarchy, converted visual geometry and materials, one
`ReachyPresentationBody` marker for every non-world body, and a fixed Unity-only
presentation camera and lighting. The MuJoCo `studio_close` and `eye_camera`
definitions remain model metadata and are never the application presentation
camera.
"""
architecture_replacement = architecture_anchor + """
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
"""
replace_once(ARCHITECTURE, architecture_anchor, architecture_replacement)

print("RMA-050 documentation closure applied and RMA-051/RMA-052 preserved.")
