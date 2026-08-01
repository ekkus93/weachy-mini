# RMA-080 Application State Architecture Validation

**Date:** 2026-07-31  
**Status:** Complete

## Scope

RMA-080 establishes the application-level composition, lifecycle, boundary, and
health contracts required before the main Unity application shell is built. It
does not implement the RMA-081 main screen or the later camera, speech,
provider, perception, and behavior capabilities.

## Implemented contracts

The shared `ReachyMini.AppState` core defines separate interfaces for:

- simulation;
- camera;
- audio;
- providers;
- perception;
- behavior;
- persistence;
- user interface.

`ReachyApplicationComposition` requires exactly one registration for every
boundary. It rejects missing or duplicate kinds, duplicate identifiers,
self/duplicate/missing dependencies, dependency cycles, undeclared dependency
resolution, null factory results, and factory identity/kind/criticality/marker
mismatches.

`ReachyApplicationHost` constructs and initializes services in deterministic
topological order. Startup failures roll back in reverse order. Rejected factory
results are disposed before the fault propagates. Shutdown is idempotent,
reverse ordered, and exhaustive: a disposal exception is retained but does not
prevent later services from being disposed.

`ReachyApplicationServiceBase` enforces one-shot initialization and idempotent
disposal. Service and application health records are immutable, revisioned, and
published through typed events. Required unavailable/faulted services fault the
application; optional unavailable/faulted services degrade it; only an all-ready
graph is reported ready.

The Unity-facing `ReachyApplicationHostBehaviour` requires an explicit
composition provider and has no implicit fallback graph. Unity `Start` and
`OnDestroy` delegate to the same explicit start/shutdown methods used by tests.
Startup diagnostics remain visible through `Fault`.

## Managed contract validation

The permanent hosted workflow runs the shared core with .NET 8 analyzers and
warnings as errors, while rejecting any `UnityEngine` dependency under the
shared application core.

The managed suite covers:

- complete graph construction and dependency order;
- missing, duplicate, and cyclic graphs;
- undeclared dependency requests;
- factory contract mismatch and rejected-service disposal;
- initialization failure and full rollback;
- reverse, idempotent, exhaustive disposal with a synthetic disposal fault;
- optional degradation and required-service fault aggregation;
- immutable historical health snapshots and monotonic revisions;
- one-shot service initialization and post-disposal rejection.

Workflow run `30677857245`, job `91308651796`, passed on exact commit
`0c0f04350363b9bc2f9b3e2cc9e5a2fdd1bee5b6`.

## Unity and Android regression validation

Self-hosted workflow run `30677109292`, job `91306592627`, passed on exact
implementation commit `da0418c95bd1278976b5cacc4683775a78f1a395`.

The run proved:

- 71 of 71 Unity edit-mode tests passed;
- the two RMA-080 Unity bridge tests passed;
- one of one Unity play-mode tests passed;
- the production ARM64 API-26 IL2CPP APK built and was verified;
- installed LG-phone native lifecycle acceptance passed;
- installed LG-phone authoritative-rendering acceptance passed;
- all expected evidence and APK artifacts uploaded successfully.

Artifacts from that run:

- Unity tests: artifact `8810922935`, ZIP digest
  `6adb71e4bd1dec889a8019e8eadcd3c3f8af8fe8dad1e9e967d32ef7ef3a865a`;
- lifecycle report: artifact `8810956859`, ZIP digest
  `3f550780c41c4bdfdfe5a0bc30afb5c54ec424109e87570656de8121cdb08ff7`;
- authoritative-rendering report: artifact `8810963025`, ZIP digest
  `1d2e0d9a17dcaeca024b55db7865f4897516189ee40127f5a88e2aa84a7e8c30`;
- device APK: artifact `8810987054`, ZIP digest
  `d6979022481ac775afb244fc699a06b199c90bc8ab02e3eb472d63d6f898a0c7`.

## Defects found and corrected during validation

1. The initial namespace `ReachyMini.Application` shadowed
   `UnityEngine.Application` in existing Unity namespaces. The shared contract
   was renamed to `ReachyMini.AppState`; no unrelated Unity production call was
   qualified or weakened to hide the collision.
2. Initial edit-mode tests attempted to invoke Unity callbacks through
   `SendMessage` and relied on `DestroyImmediate` for callback timing. The
   bridge now exposes explicit lifecycle methods used by both Unity callbacks
   and deterministic tests.
3. Managed analyzer findings were resolved by tightening private collection
   types rather than suppressing analyzers.

## Acceptance decision

RMA-080 is accepted. The repository now has explicit application service
boundaries, validated dependency construction, deterministic initialization and
disposal, and a top-level health/status model. RMA-081 may build the production
main screen and first production composition on these contracts.
