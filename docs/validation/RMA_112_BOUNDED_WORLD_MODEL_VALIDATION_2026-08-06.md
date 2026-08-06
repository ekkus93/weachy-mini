# RMA-112 Bounded World Model Validation

**Task:** RMA-112 — Implement bounded world model  
**Date:** 2026-08-06  
**Status:** Complete

## Scope

RMA-112 adds a Unity-independent managed world model that consumes the existing
RMA-110/RMA-111 perception contracts without inventing unsupported spatial
information.

The permanent implementation and validation surfaces are:

- `Assets/ReachyMini/Runtime/Core/Perception/ReachyBoundedWorldModel.cs`
- `managed/ReachyMini.WorldModel.Tests/Program.cs`
- `managed/ReachyMini.WorldModel.Tests/ReachyMini.WorldModel.Tests.csproj`
- `.github/workflows/rma112-bounded-world-model.yml`

## Implemented contract

The world model stores and exposes:

- stable entity identity and an explicit entity generation;
- tracker-local identity, classification, confidence, and provider provenance;
- first-seen and last-seen monotonic timestamps;
- camera, source-session, source-sequence, authoritative-sequence, and
  simulation-continuity provenance;
- normalized Reachy-eye direction estimates and source bounds;
- an explicit unknown metric position for two-dimensional tracker observations;
- coverage state, valid fraction, turning-stop state, and coverage diagnostic;
- bounded observation and semantic-description histories;
- semantic confirmation count, latest confirming provider, source identity,
  and description age;
- immutable current/recent snapshots for future conversation and behavior
  consumers;
- bounded ordering cursors and visible drop/rejection diagnostics.

## Truthfulness and fail-closed rules

The production implementation does not:

- fabricate metric or three-dimensional position from two-dimensional bounds;
- refresh, expire, or change visibility from a stale or conflicting frame;
- evict ordering state for a scope whose entity is still retained;
- silently overwrite entities when entity or cursor capacity is exhausted;
- attach semantic descriptions to a reused tracker ID from another generation;
- retain unbounded entities, observations, descriptions, text, or session
  cursors;
- substitute a fallback provider, stale observation, or stale semantic result.

Cursor-capacity rejection, entity-capacity rejection, unusable coverage,
classification conflict, stale/conflicting input, description rejection, and
bounded-history drops are returned or counted explicitly.

## Ordering hardening

A post-green source audit rejected the first behaviorally green candidate. That
candidate advanced the world-model clock, expiry, and visibility before testing
per-scope frame ordering. A stale frame carrying a later timestamp could
therefore mutate retained state before returning `StaleRejected`.

The accepted implementation performs duplicate/stale checks and cursor-capacity
preflight before any world-state mutation. Cursor eviction is permitted only for
a scope with no entity retained at the incoming timestamp. If every cursor is
protecting retained state, the incoming batch returns visible
`CapacityExceeded` without changing the clock, entities, visibility, or cursors.

Non-stale frames that are rejected for unusable coverage, entity capacity, or
classification conflict still advance their scope's ordering cursor. This
prevents an older frame from replaying after a visible semantic rejection.

## Deterministic contract suite

The permanent warnings-as-errors harness covers eighteen cases:

1. stable tracker identity updates one entity;
2. continuity reset creates a new entity generation;
3. expiry occurs at the exact configured boundary;
4. unusable coverage creates no observation and does not refresh visibility;
5. degraded coverage remains attached to the observation;
6. normalized semantic duplicates are deduplicated, aged, and attributed to
   the latest confirming provider;
7. description history and text length remain bounded;
8. published snapshots are deep immutable copies;
9. entity-capacity failure is atomic and visible;
10. a long synthetic stream remains within every configured bound;
11. source-session ordering cursors remain bounded;
12. two-dimensional tracking never reports a metric position;
13. current, recent, and expired visibility are distinct;
14. stale and conflicting observations fail visibly;
15. a later-timestamp stale frame is non-mutating and cannot advance expiry;
16. retained entity scopes protect their ordering cursors from eviction;
17. semantic results cannot cross entity generations;
18. source contracts retain the no-fallback and bounded-state requirements.

## Candidate and repair history

### Initial source and harness repair

Commit `b1bd69869af6eefc4661ce141999103b7d969cc5` introduced the production
implementation, tests, and permanent workflow. The production core built with
zero warnings, but run `31084148208` exposed two deterministic test-harness
compilation defects: `TrackedTracked` instead of `TrackedObject`, and analyzer
CA2249 requiring `string.Contains` for exact source assertions.

Commit `d65a6cfe619f5b67da9fbb53411ddfc610b6ded7` corrected only those harness
defects. No production contract or analyzer policy was weakened.

### Rejected behaviorally green candidate

Commit `f9284eae67331ac7d7b6e6434d0cbd8ec3e173a6` passed the original sixteen
contracts, but the source audit found the stale-frame mutation and retained-scope
cursor-eviction defects described above. Its physical run was superseded and its
green managed result was not accepted as RMA-112 completion.

### Accepted implementation

Commit `4e5d08d9dc917b5e7a22a0dada0a34ab5ed11f7f` contains the ordering,
cursor-retention, and semantic-provenance hardening. The one-use hardening run
`31085162166`, job `92562784586`, built the managed core with zero warnings and
passed all eighteen contracts before the exact tested commit was published.

## Accepted exact-SHA validation

### Dedicated bounded-world-model gate

Run `31085246677`, job `92563054033`, passed on exact implementation SHA
`4e5d08d9dc917b5e7a22a0dada0a34ab5ed11f7f`:

- managed core build with warnings as errors and zero warnings;
- all eighteen behavioral/source contracts;
- exact-SHA report creation and source hashing;
- artifact upload and final `RMA-112 Bounded World Model` status publication.

Artifact `8961118733`,
`rma112-bounded-world-model-evidence-4e5d08d9dc917b5e7a22a0dada0a34ab5ed11f7f`,
has digest
`sha256:599b2c8918cc850a43445e719e57a43dbd004bf3dedf57d813101ec72e9134c7`.
Its report records `status: passed`, eighteen contract cases, deterministic
expiry, immutable snapshots, bounded entity/description/cursor state, fail-closed
entity capacity, no silent fallback, `unknown_from_2d_tracking` metric position,
and source SHA-256
`daada17ad9e8453c3e8eec88ae5933ef66770896583ac3c27145ed348b282ec0`.

### Hosted CI

Hosted CI run `31085246701` passed static policy, managed warnings-as-errors,
native and sanitizer tests, Android checks, and pinned Reachy-model validation on
the same exact SHA.

### Physical Unity and Android validation

Self-hosted run `31085246680`, job `92563129531`, passed the complete acceptance
sequence on an LGE LG-H872 running Android 8.0.0/API 26/arm64-v8a, serial
`LGH87250967ab9`:

- generated Reachy presentation and production MuJoCo staging;
- `129/129` Unity EditMode tests and `1/1` PlayMode test;
- ARM64/API-26 IL2CPP APK build and architecture verification;
- physical RMA-090 discovery, RMA-091 acquisition, RMA-092 GPU texture, and
  RMA-111 bundled face/person tracking;
- RMA-022 pause/resume, controlled-failure, destruction, and no-hidden-native-
  fallback lifecycle acceptance;
- authoritative MuJoCo-driven rendering with 17 moved bodies, all six Stewart
  links moved, yaw/head/both antennas moved, valid renderer structure, and no
  hidden kinematic fallback;
- every evidence upload, APK upload, and final status publication.

The Unity result artifact `8961200442` has digest
`sha256:8d4786d2c654abc29ad71d12005fd096f58b3a88b09c8819c21b87002104aeca`.
The RMA-111 artifact `8961372713` has digest
`sha256:c2bb44a2373c86aba4561498e71d95871754c74e40557fa7e55c4a49c2183206`
and records stable `face-000001` and `person-000001`, invalid-center
suppression, zero VLM invocations, and no network model download.

The lifecycle artifact `8961405069` has digest
`sha256:f43665d2d558fb22bc0507c69182c8d979869e5120e1224bc0f9cf9e0d51be78`.
The authoritative-rendering artifact `8961421065` has digest
`sha256:f96c230e33fb63f605c9a544cf0e3b8e90e9569a520b01ccc8512231e316b646`.
The APK artifact `8961492048` has digest
`sha256:1a753779aff4c1bc3f6e6956fd519791755582fd85500f5b4f482923416b34f7`.

The authoritative gate reused the exact installed candidate APK only after
verifying candidate, pre-install, and final installed SHA-256 were all
`040d95387496f35218d736d86f7e00e3f1fb67e3c6971daba525a1e1fab29f4d`.
Application data was cleared, launch status was zero, and
`installed_apk_matches_candidate=true`.

## Repository cleanup

The one-use branches `rma112-staging`, `rma112-repair1`, and
`rma112-hardening1` were deleted. Their applicators, payload chunks, and repair
workflows are absent from `master`. Only the permanent RMA-112 source, tests,
workflow, TODO closeout, and this validation record remain.
