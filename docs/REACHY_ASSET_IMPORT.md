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
6. fails on missing files, traversal paths, malformed XML, source changes, or revision mismatch.

It does not convert or modify mesh content yet. Any future conversion must be represented as a versioned source transformation with tests and updated provenance.

## Tests

```bash
python3 -m unittest discover -s scripts/tests -v
```

The fixture tests verify deterministic repeated output, dirty-checkout rejection, revision mismatch rejection, and preservation of the previous output when validation fails.
