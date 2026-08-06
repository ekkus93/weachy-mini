# RMA-112 Bounded World Model Contract Harness

This executable managed test project validates the Unity-independent RMA-112
bounded world-model implementation in
`Assets/ReachyMini/Runtime/Core/Perception/ReachyBoundedWorldModel.cs`.

Run the exact warnings-as-errors validation from the repository root:

```bash
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj \
  --configuration Release \
  --warnaserror

dotnet run \
  --project managed/ReachyMini.WorldModel.Tests/ReachyMini.WorldModel.Tests.csproj \
  --configuration Release
```

The harness covers eighteen behavioral and source-contract cases, including:

- deterministic entity generation and exact-boundary expiry;
- immutable current/recent snapshots;
- bounded entities, observations, descriptions, text, and ordering cursors;
- explicit unknown metric position for two-dimensional tracking;
- coverage provenance and unusable-coverage rejection;
- semantic description deduplication and generation isolation;
- non-mutating stale/conflicting-frame rejection;
- retained-scope cursor protection and visible capacity failure;
- no stale-result or provider fallback path.

The permanent GitHub Actions entry point is
`.github/workflows/rma112-bounded-world-model.yml`. The implementation history,
physical Android evidence, artifact digests, and rejected-candidate analysis are
recorded in
`docs/validation/RMA_112_BOUNDED_WORLD_MODEL_VALIDATION_2026-08-06.md`.
