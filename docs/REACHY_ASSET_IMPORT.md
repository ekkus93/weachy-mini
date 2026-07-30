# Reachy Mini asset import

The project does not commit imported Reachy model files. It imports them from a clean checkout at the exact revision recorded in `third_party/reachy-mini-source.lock.json`.

## Prepare the source checkout

```bash
git clone https://github.com/pollen-robotics/reachy_mini.git /path/to/reachy_mini
git -C /path/to/reachy_mini checkout --detach a739a6e461eb6d722901f1cfc225265ffc85c28d
git -C /path/to/reachy_mini status --short
```

The final command must produce no output. The importer rejects modified and untracked source files rather than accepting an ambiguous source tree.

## Import

```bash
python3 scripts/import_reachy_assets.py --source /path/to/reachy_mini
```

The default output is `Assets/Generated/ReachyMini/Source/`, which is intentionally ignored by Git. The importer:

1. verifies the exact Git commit and clean worktree;
2. parses the pinned Reachy Mini MJCF;
3. copies the MJCF and every mesh referenced by its `<asset>` section;
4. copies the upstream license;
5. emits `ATTRIBUTION.md` and a deterministic `PROVENANCE.json` containing a SHA-256 digest and byte size for each imported file;
6. validates exact topology counts, every named body/joint/actuator/site/camera required by the runtime, and the complete ordered 18-body hierarchy;
7. fails on missing files, traversal paths, malformed XML, source changes, revision mismatch, or body reparenting/reordering.

The imported MJCF and source meshes remain byte-for-byte unchanged. The source cameras (`studio_close` and `eye_camera`) stay available as MuJoCo model metadata, but `prepare_reachy_unity_assets.py` marks both as excluded from the Unity presentation and creates an independent Unity-only presentation camera. Mesh conversion is a separate versioned `reachy_stl_to_unity_obj_v1` presentation transformation with source/output hashes; it does not alter the solver model, joint ranges, inertias, or collision geometry.

## Tests

```bash
python3 -m unittest discover -s scripts/tests -v
```

The fixture tests verify deterministic repeated output, dirty-checkout rejection, revision mismatch rejection, and preservation of the previous output when validation fails.
