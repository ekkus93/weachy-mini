#!/usr/bin/env python3
"""Close RMA-052 documentation after exact-head validation."""

from pathlib import Path


TODO_PATH = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
STATUS_PATH = Path("docs/IMPLEMENTATION_STATUS.md")
ARCHITECTURE_PATH = Path("docs/architecture/AUTHORITATIVE_UNITY_RENDERING.md")
PENDING_PATH = Path("docs/validation/RMA_052_VALIDATION_PENDING_2026-07-30.md")
FINAL_PATH = Path(
    "docs/validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md"
)


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one {label} block in {path}, found {count}.")
    path.write_text(text.replace(old, new), encoding="utf-8")


def main() -> None:
    if not PENDING_PATH.is_file():
        raise SystemExit(f"Pending RMA-052 marker is missing: {PENDING_PATH}")
    if FINAL_PATH.exists():
        raise SystemExit(f"Final RMA-052 validation record already exists: {FINAL_PATH}")

    pending = PENDING_PATH.read_text(encoding="utf-8")
    if "5d5bc2cb078ef5432c0ad6f95599890150330da6" not in pending:
        raise SystemExit("Pending marker does not identify the validated exact head.")

    old_todo = """## RMA-052 — Add authoritative-rendering invariant checks

- [ ] Add development-build assertions comparing Unity rendered transforms to the mapped MuJoCo snapshot.
- [ ] Report drift above tolerance.
- [ ] Ensure animation, Timeline, Animator, and physics components cannot write mapped transforms.
- [ ] Disable or reject Unity Rigidbody/ArticulationBody components on authoritative robot bodies.

**Acceptance criteria — authoritative rendering gate**

- [ ] Forced transform modification is detected in tests/development builds.
- [ ] Production rendering contains no hidden kinematic fallback.
"""
    new_todo = """## RMA-052 — Add authoritative-rendering invariant checks

- [x] Add development-build assertions comparing Unity rendered transforms to the mapped MuJoCo snapshot.
- [x] Report drift above tolerance.
- [x] Ensure animation, Timeline, Animator, and physics components cannot write mapped transforms.
- [x] Disable or reject Unity Rigidbody/ArticulationBody components on authoritative robot bodies.

**Acceptance criteria — authoritative rendering gate**

- [x] Forced transform modification is detected in tests/development builds.
- [x] Production rendering contains no hidden kinematic fallback.

**Completion evidence — 2026-07-30**

- The renderer records the exact expected Unity world transform, authoritative
  sequence, interpolation target time, continuity identity, and configured
  tolerances after every mapped MuJoCo pose.
- `Application.onBeforeRender` performs a final frame-boundary comparison. Editor
  and development players emit an explicit assertion before entering the same
  fail-closed renderer fault used by release players.
- `ReachyAuthoritativeInvariantReport` preserves expected and actual transforms,
  position/rotation drift, body identity, sequence/time/continuity, and both
  tolerances. Invalid, zero, negative, NaN, and infinite tolerances are rejected.
- Focused tests force transform drift and require the development assertion,
  retained diagnostic report, renderer fault, and disabled motion authority.
  Additional descendant tests reject Rigidbody, Rigidbody2D, ArticulationBody,
  Animator, legacy Animation, and PlayableDirector/Timeline writers.
- Hosted run `30594656829` passed managed, native, official-model, static, and
  Android gates on exact commit
  `5d5bc2cb078ef5432c0ad6f95599890150330da6`.
- Self-hosted `kawa` run `30594656835` passed generated Unity tests, production
  ARM64 MuJoCo staging, API-26 IL2CPP APK build/verification, installed lifecycle
  acceptance, physical authoritative-rendering acceptance, evidence uploads, and
  APK upload on the same exact commit.
- Physical acceptance retained renderer status `Rendering`, runtime status
  `Running`, all 18 canonical body bindings, body/head/antenna/Stewart motion,
  reset continuity, and `hidden_kinematic_fallback=false`.
- Detailed evidence is recorded in
  `docs/validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md`.
"""
    replace_once(TODO_PATH, old_todo, new_todo, "RMA-052 TODO")

    old_header = """**Current implementation series:** RMA-051 allocation-free authoritative
state-to-render mapping, timestamp interpolation, discontinuity handling,
generated diagnostics, and physical Android acceptance after RMA-050 prefab
closure
"""
    new_header = """**Current implementation series:** RMA-052 pre-render authoritative
invariant assertions, exact drift diagnostics, prohibited-writer rejection, and
physical Android acceptance after RMA-051 state-to-render closure
"""
    replace_once(STATUS_PATH, old_header, new_header, "status header")

    status_anchor = """## Current validation evidence
"""
    status_section = """### RMA-052 — authoritative-rendering invariant closure

RMA-052 is complete. Every rendered pose retains the exact expected Unity world
positions and rotations derived from the mapped MuJoCo pair, together with the
newer authoritative sequence, interpolation target time, continuity identity,
and finite positive drift tolerances.

The renderer validates the previous application before the next pose and again at
`Application.onBeforeRender`. Editor and development players emit an explicit
assertion on drift; release players execute the same comparison without the
assertion log. Every build faults, disables the renderer, and propagates the
failure into the production runtime rather than overwriting the competing writer
or switching to cosmetic motion.

`ReachyAuthoritativeInvariantReport` retains body index/name, expected and actual
position/rotation, position and angular drift, sequence/time/continuity, and both
tolerances. The report remains available after the component is disabled, making
the exact violation inspectable instead of reducing it to a generic fault.

The authoritative hierarchy rejects Rigidbody, Rigidbody2D, ArticulationBody,
Joint, Joint2D, Animator, legacy Animation, and PlayableDirector/Timeline on
mapped bodies or visual descendants. Tests cover representative 3D/2D physics,
articulation, animation, and Timeline components on descendants as well as forced
post-render transform mutation and invalid tolerance configuration.

Hosted run `30594656829` and self-hosted `kawa` run `30594656835` passed on exact
commit `5d5bc2cb078ef5432c0ad6f95599890150330da6`. The device artifact retained the
production MuJoCo source, authoritative renderer health, all canonical motion and
reset checks, and `hidden_kinematic_fallback=false`. Detailed evidence is in
[the RMA-052 validation record](validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md).

## Current validation evidence
"""
    replace_once(STATUS_PATH, status_anchor, status_section, "RMA-052 status section")

    evidence_anchor = """- Self-hosted RMA-051 run `30593459413`: generated presentation preparation,
  production ARM64 MuJoCo staging, Unity EditMode/PlayMode tests including
  allocation and mapping regressions, ARM64 API-26 IL2CPP build/verification,
  installed lifecycle acceptance, physical authoritative rendering, evidence
  uploads, and APK upload passed on the same exact commit.
"""
    evidence_replacement = evidence_anchor + """- Hosted RMA-052 run `30594656829`: managed warnings-as-errors and
  native-backed tests, native warnings/sanitizers, pinned-model conversion and
  reference generation, actionlint/Ruff/ShellCheck/static policy, and Android
  lint/tests passed on exact commit
  `5d5bc2cb078ef5432c0ad6f95599890150330da6`.
- Self-hosted RMA-052 run `30594656835`: generated presentation preparation,
  production ARM64 MuJoCo staging, Unity invariant tests, ARM64 API-26 IL2CPP
  build/verification, installed lifecycle acceptance, physical authoritative
  rendering, evidence uploads, and APK upload passed on the same exact commit.
"""
    replace_once(STATUS_PATH, evidence_anchor, evidence_replacement, "validation evidence")

    open_gate = "- RMA-052 formal authoritative-rendering invariant closure;\n"
    status_text = STATUS_PATH.read_text(encoding="utf-8")
    if status_text.count(open_gate) != 1:
        raise SystemExit("Expected one RMA-052 open-gate entry.")
    STATUS_PATH.write_text(status_text.replace(open_gate, ""), encoding="utf-8")

    old_invariant = """## Invariant enforcement

After applying a pose, the renderer records the exact expected Unity world
transform for every authoritative body. On the next late-frame update it checks
for position or rotation drift before applying another snapshot. Drift faults
and disables the renderer, making Animator, script, Timeline, or physics
interference visible instead of silently overwriting it.

The authoritative hierarchy rejects these component classes on a mapped body or
its visual descendants:

- `Rigidbody` and `Rigidbody2D`;
- `Joint` and `Joint2D`;
- `ArticulationBody`;
- `Animator` and legacy `Animation`;
- `PlayableDirector`.

The renderer executes late in the frame and performs no successful-path
collection or array allocation. Body bindings and expected-pose arrays are
created during configuration.
"""
    new_invariant = """## Invariant enforcement

After applying a pose, the renderer records the exact expected Unity world
transform for every authoritative body plus the authoritative sequence,
interpolation target time, continuity identity, and configured position/rotation
tolerances. It validates that application before the next pose and again through
`Application.onBeforeRender`, so a later `LateUpdate` writer is caught before the
frame is presented.

Editor and development players emit an explicit assertion containing body,
sequence, simulation time, continuity, measured drift, and tolerance. Release
players execute the same comparison without the assertion log. In every build,
drift creates a retained `ReachyAuthoritativeInvariantReport`, faults and disables
the renderer, and propagates into the production runtime. The renderer never
silently restores the pose, accepts the competing authority, or selects cosmetic
fallback motion.

The report contains expected and actual position/rotation, position and angular
drift, body identity, sequence/time/continuity, and both thresholds. Tolerances
must be finite and positive; malformed serialized or runtime configuration fails
before rendering.

The authoritative hierarchy rejects these component classes on a mapped body or
its visual descendants:

- `Rigidbody` and `Rigidbody2D`;
- `Joint` and `Joint2D`;
- `ArticulationBody`;
- `Animator` and legacy `Animation`;
- `PlayableDirector`.

The renderer executes late in the frame and performs no successful-path
collection or array allocation. Body bindings, reusable source frames,
expected-pose arrays, and invariant storage are created during configuration.
"""
    replace_once(
        ARCHITECTURE_PATH,
        old_invariant,
        new_invariant,
        "architecture invariant enforcement",
    )

    FINAL_PATH.write_text(
        """# RMA-052 authoritative-rendering invariant validation

**Date:** 2026-07-30  
**Implementation commit:** `7eca9c9c64e9e43890ec1be6a3ed1b260541d436`  
**Exact validated commit:** `5d5bc2cb078ef5432c0ad6f95599890150330da6`  
**Hosted quality run:** `30594656829`  
**Self-hosted Unity/Android run:** `30594656835`

## Scope

This record closes RMA-052 only. It verifies that rendered Unity transforms remain a checked projection of the mapped authoritative MuJoCo state, that post-render competing writers are detected before presentation, that drift diagnostics retain the exact expected and actual values, that prohibited animation/Timeline/physics components are rejected, and that the production Android artifact contains no hidden kinematic fallback.

## Frame-boundary invariant

`ReachyAuthoritativeRenderer` stores the expected Unity world position and rotation for all canonical bodies after each timestamp-mapped pose. It also stores the authoritative newer sequence, interpolation target simulation time, continuity identifier, and finite positive position and angular tolerances.

The invariant runs at two boundaries:

1. before applying the next authoritative pair, preserving the existing fail-closed next-update check;
2. through `Application.onBeforeRender`, detecting writers that execute after the renderer's high-order `LateUpdate` before the frame is shown.

Editor and development players call `Debug.Assert` with the exact invariant message. Release players omit that assertion log but execute the same comparison. Any out-of-tolerance position or rotation drift creates a retained report, faults and disables the renderer, and is propagated by `ReachyProductionAuthoritativeRuntime`. The mutated transform is not silently overwritten and no alternate motion source is enabled.

## Diagnostic contract

`ReachyAuthoritativeInvariantReport` preserves:

- whether validation occurred and whether it passed;
- authoritative sequence, interpolation target simulation time, and continuity;
- body index and canonical body name;
- expected and actual Unity position and rotation;
- measured position drift in metres and angular drift in degrees;
- configured position and angular tolerances.

Zero, negative, NaN, and infinite tolerances are rejected. A successful check records the highest normalized drift observed, making near-threshold behavior inspectable without allocating a per-body report collection.

## Competing-writer rejection

The renderer continues to reject Rigidbody, Rigidbody2D, Joint, Joint2D, ArticulationBody, Animator, legacy Animation, and PlayableDirector/Timeline on mapped bodies or any visual descendant. Focused tests add representative descendants for 3D physics, 2D physics, articulation, Animator, legacy Animation, and Timeline and require a visible renderer fault for every case.

A forced transform mutation test requires both the development assertion and the production-style retained fault. It verifies body identity, sequence `2`, target simulation time `0.001`, continuity `3`, measured drift, expected thresholds, and final `Faulted` status.

## Hosted validation

Hosted run `30594656829` passed on exact commit `5d5bc2cb078ef5432c0ad6f95599890150330da6`:

- managed warnings-as-errors and native-backed lifecycle/state tests;
- native warnings-as-errors plus sanitizer suites;
- pinned Reachy source/topology validation, visual conversion, MuJoCo compile/step, and desktop reference generation;
- actionlint, Ruff, formatting, ShellCheck, and static repository policy;
- Android lint, Java warnings, and tests.

## Unity and physical Android validation

Self-hosted run `30594656835` passed on the same exact commit:

- deterministic generated presentation preparation;
- production ARM64 MuJoCo staging;
- Unity EditMode/PlayMode tests, including the new invariant report, assertion, tolerance, and prohibited-writer regressions;
- ARM64 API-26 IL2CPP APK build and architecture verification;
- installed HOME/resume lifecycle acceptance;
- physical authoritative-rendering acceptance;
- Unity evidence, device evidence, and APK uploads.

Physical acceptance retained all 18 canonical body bindings, nonzero model identity, ordered simulation sequence/time, body yaw, head motion, both antennas, all six Stewart links, reset continuity and discontinuity snapping, renderer status `Rendering`, runtime status `Running`, and `hidden_kinematic_fallback=false`.

## Result

RMA-052 is complete. Unity rendering is now guarded at the presentation boundary by development assertions and release fail-closed validation, with exact retained diagnostics and comprehensive rejection of competing animation, Timeline, articulation, and physics writers. The physical production artifact remains driven exclusively by authoritative MuJoCo state with no hidden kinematic fallback.
""",
        encoding="utf-8",
    )
    PENDING_PATH.unlink()


if __name__ == "__main__":
    main()
