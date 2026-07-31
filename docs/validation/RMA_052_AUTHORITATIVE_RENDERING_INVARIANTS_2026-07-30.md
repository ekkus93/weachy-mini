# RMA-052 authoritative-rendering invariant validation

**Date:** 2026-07-30
**Implementation commit:** `7eca9c9c64e9e43890ec1be6a3ed1b260541d436`
**Exact validated commit:** `5d5bc2cb078ef5432c0ad6f95599890150330da6`
**Hosted quality run:** `30594656829`
**Self-hosted Unity/Android run:** `30594656835`

## Scope

RMA-052 verifies that Unity transforms remain a checked projection of
authoritative MuJoCo state, post-render writers are detected before
presentation, exact drift diagnostics are retained, prohibited writers
are rejected, and the Android artifact has no hidden kinematic fallback.

## Invariant and diagnostics

`ReachyAuthoritativeRenderer` stores expected world transforms plus
sequence, target simulation time, continuity, and finite positive
tolerances. Validation runs before the next pair and through
`Application.onBeforeRender`. Development players assert; release players
run the same fail-closed comparison without the assertion log.

`ReachyAuthoritativeInvariantReport` preserves validation status,
sequence/time/continuity, body identity, expected/actual transforms,
measured position/angular drift, and configured tolerances. Invalid
tolerances are rejected. Successful checks retain the highest normalized
drift without per-body report allocation.

Rigidbody, Rigidbody2D, Joint, Joint2D, ArticulationBody, Animator,
legacy Animation, and PlayableDirector/Timeline are rejected on mapped
bodies or visual descendants. Forced mutation tests require the
development assertion, retained fault report, and final `Faulted` state.

## Validation

Hosted run `30594656829` passed managed/native tests, sanitizers,
pinned-model conversion and reference generation, static policy, and
Android checks on exact commit
`5d5bc2cb078ef5432c0ad6f95599890150330da6`.

Self-hosted run `30594656835` passed production ARM64 MuJoCo staging,
Unity invariant tests, API-26 IL2CPP build/verification, installed
lifecycle acceptance, physical authoritative-rendering acceptance, and
artifact uploads on the same commit.

Physical evidence retained all 18 canonical body bindings, ordered
simulation state, body/head/antenna/Stewart motion, reset continuity,
renderer status `Rendering`, runtime status `Running`, and
`hidden_kinematic_fallback=false`.

## Result

RMA-052 is complete. Unity rendering is guarded at the presentation
boundary by development assertions and release fail-closed validation,
with retained diagnostics and no hidden kinematic fallback.
