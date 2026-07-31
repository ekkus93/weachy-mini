#!/usr/bin/env python3
"""Close the validated RMA-051 documentation checklists."""

from pathlib import Path


TODO_PATH = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
STATUS_PATH = Path("docs/IMPLEMENTATION_STATUS.md")
VALIDATION_PATH = Path(
    "docs/validation/RMA_051_STATE_TO_RENDER_MAPPING_VALIDATION_2026-07-30.md"
)


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one guarded block in {path}, found {count}.")
    path.write_text(text.replace(old, new), encoding="utf-8")


def main() -> None:
    if not VALIDATION_PATH.is_file():
        raise SystemExit(f"Validation record is missing: {VALIDATION_PATH}")

    old_todo = """## RMA-051 — Implement state-to-render mapping

- [ ] Read the latest two authoritative state snapshots.
- [ ] Interpolate render transforms by simulation timestamps without feeding results back to physics.
- [ ] Handle reset/discontinuity without interpolating through impossible poses.
- [ ] Eliminate per-frame allocations.
- [ ] Add an optional debug overlay of body axes and joint names.

**Acceptance criteria**

- [ ] Rendering at 30 and 60 FPS displays the same underlying trajectory.
- [ ] A test detects any script that attempts to authoritatively drive simulated transforms.
- [ ] Sleep/wake and antenna motion align with MuJoCo bodies.
"""
    new_todo = """## RMA-051 — Implement state-to-render mapping

- [x] Read the latest two authoritative state snapshots.
- [x] Interpolate render transforms by simulation timestamps without feeding results back to physics.
- [x] Handle reset/discontinuity without interpolating through impossible poses.
- [x] Eliminate per-frame allocations.
- [x] Add an optional debug overlay of body axes and joint names.

**Acceptance criteria**

- [x] Rendering at 30 and 60 FPS displays the same underlying trajectory.
- [x] A test detects any script that attempts to authoritatively drive simulated transforms.
- [x] Sleep/wake and antenna motion align with MuJoCo bodies.

**Completion evidence — 2026-07-30**

- The production pose source rotates three preallocated authoritative-state
  frames and copies the latest ordered pair into two renderer-owned reusable
  pose frames. The immutable snapshot API remains available for diagnostics,
  but the production `LateUpdate` path no longer constructs pose arrays or
  snapshots.
- Timestamp interpolation, render-cadence independence, ordered publication,
  and reset/discontinuity snapping are covered by focused managed and Unity
  tests. The 30/60 FPS criterion is represented by rendering the same target
  simulation timestamp through different render-call cadences and requiring
  identical transforms.
- Allocation regressions require zero managed bytes across 128 production
  source-pair copies and 128 generated-prefab render iterations after warmup.
- The generated optional diagnostics overlay starts disabled and retains all
  18 body-axis bindings and all 16 joint labels without writing transforms.
- External transform writes fault instead of being overwritten, while
  Rigidbody, ArticulationBody, Animator, Animation, Timeline, and joint writers
  remain prohibited on the authoritative hierarchy.
- Hosted quality run `30593459422` passed managed, native, official-model,
  static, and Android gates on exact commit
  `09d5f6d3cf48a5b167f09629de520112ae60d5a6`.
- Self-hosted `kawa` run `30593459413` passed generated Unity tests, ARM64
  API-26 IL2CPP build/verification, installed HOME/resume lifecycle acceptance,
  and physical authoritative-rendering acceptance on the same exact commit.
  Device evidence verifies body/head motion, both antennas, all six Stewart
  links, ordered simulation time, reset continuity, renderer health, and no
  hidden kinematic fallback.
- The official pinned model provides no named sleep/rest keyframe. `SleepRest`
  therefore remains typed `UNSUPPORTED` rather than inventing a pose; real
  device sleep/wake lifecycle behavior and neutral/reset mapping are verified
  without fabricating unsupported model state.
- Detailed evidence is recorded in
  `docs/validation/RMA_051_STATE_TO_RENDER_MAPPING_VALIDATION_2026-07-30.md`.
"""
    replace_once(TODO_PATH, old_todo, new_todo)

    old_header = """**Current implementation series:** RMA-050 deterministic Unity prefab and scene,
full-model presentation hierarchy, auditable mesh scale/basis conversion, and
fixed camera/lighting acceptance after RMA-041/RMA-042 model-integrity closure
"""
    new_header = """**Current implementation series:** RMA-051 allocation-free authoritative
state-to-render mapping, timestamp interpolation, discontinuity handling,
generated diagnostics, and physical Android acceptance after RMA-050 prefab
closure
"""
    replace_once(STATUS_PATH, old_header, new_header)

    old_status = """### RMA-051/RMA-052 — authoritative state mapping and invariant foundations

The production state-format-v1 envelope publishes model identity, sequence,
simulation time, continuity, qpos, qvel, actuator observations, canonical body
poses, calibration identity, warnings, constraint counts, and residuals. The
managed parser validates all offsets, counts, identities, ordering, finiteness,
and quaternions, then publishes immutable pose pairs.

Unity interpolates by simulation timestamps, snaps across discontinuities, and
never feeds presentation transforms back into MuJoCo. Rigidbody, articulation,
Animator, Timeline, and other competing writers are rejected or detected.
Physical acceptance verifies body yaw, head, both antennas, all six Stewart
links, reset continuity, renderer health, and absence of a hidden kinematic
fallback.

The physical acceptance scripts now share one deterministic device contract:
wake, unlock, collapse overlays, acknowledge immersive confirmation, keep awake,
launch the exact Unity activity, verify focused-window ownership, capture
structured evidence, and restore device power policy.
"""
    new_status = """### RMA-051 — authoritative state-to-render mapping

RMA-051 is complete. The production state-format-v1 envelope publishes model
identity, sequence, simulation time, continuity, qpos, qvel, actuator
observations, canonical body poses, calibration identity, warnings, constraint
counts, and residuals. The managed parser validates all offsets, counts,
identities, ordering, finiteness, and quaternions.

The production pose source retains previous, latest, and capture state frames
and rotates them when the worker publishes a new state. The renderer creates two
caller-owned reusable pose frames at bind time and copies the latest ordered pair
into them. Immutable snapshots remain available for diagnostics and legacy
callers, but the production render loop performs no pose-array or snapshot
allocation.

Unity interpolates by simulation timestamps, gives identical transforms for the
same target time regardless of 30/60 FPS render cadence, snaps across reset or
reload discontinuities, and never feeds presentation transforms back into
MuJoCo. Focused regressions require zero managed bytes in both the production
source-copy loop and the generated-prefab steady-state render loop.

The generated optional diagnostics overlay starts disabled and maps all 18 body
axes plus all 16 joint names. External transform writes fault visibly;
Rigidbody, articulation, Animator, Timeline, and other competing writers are
rejected or detected. These mechanisms provide foundations for RMA-052, but the
separate formal RMA-052 invariant-closure task remains open.

Physical acceptance verifies body yaw, head, both antennas, all six Stewart
links, reset continuity, renderer health, and absence of a hidden kinematic
fallback. Installed HOME/resume acceptance verifies real Android sleep/wake
lifecycle behavior without suspended-time catch-up. The official model has no
sleep/rest keyframe, so `SleepRest` remains typed `UNSUPPORTED` rather than
fabricating a pose.

The physical acceptance scripts share one deterministic device contract: wake,
unlock, collapse overlays, acknowledge immersive confirmation, keep awake,
launch the exact Unity activity, verify focused-window ownership, capture
structured evidence, and restore device power policy. The detailed contract is
in [Authoritative Unity rendering](architecture/AUTHORITATIVE_UNITY_RENDERING.md),
with validation evidence in
[the RMA-051 validation record](validation/RMA_051_STATE_TO_RENDER_MAPPING_VALIDATION_2026-07-30.md).
"""
    replace_once(STATUS_PATH, old_status, new_status)

    old_validation = """- Self-hosted RMA-050 run `30591010149`: generated presentation preparation,
  production ARM64 MuJoCo staging, expanded Unity prefab/scene tests, ARM64 API-26
  IL2CPP build/verification, installed lifecycle acceptance, physical
  authoritative-rendering acceptance, evidence uploads, and APK upload passed on
  the same exact commit.

- Focused RMA-041 audit run `30587841758`: Ruff, 11 positive/failure-path tests,
"""
    new_validation = """- Self-hosted RMA-050 run `30591010149`: generated presentation preparation,
  production ARM64 MuJoCo staging, expanded Unity prefab/scene tests, ARM64 API-26
  IL2CPP build/verification, installed lifecycle acceptance, physical
  authoritative-rendering acceptance, evidence uploads, and APK upload passed on
  the same exact commit.
- Hosted RMA-051 run `30593459422`: managed warnings-as-errors and native-backed
  lifecycle/state tests, native warnings/sanitizers, exact pinned-model
  conversion and reference generation, Ruff/actionlint/ShellCheck/static policy,
  and Android lint/tests passed on
  `09d5f6d3cf48a5b167f09629de520112ae60d5a6`.
- Self-hosted RMA-051 run `30593459413`: generated presentation preparation,
  production ARM64 MuJoCo staging, Unity EditMode/PlayMode tests including
  allocation and mapping regressions, ARM64 API-26 IL2CPP build/verification,
  installed lifecycle acceptance, physical authoritative rendering, evidence
  uploads, and APK upload passed on the same exact commit.

- Focused RMA-041 audit run `30587841758`: Ruff, 11 positive/failure-path tests,
"""
    replace_once(STATUS_PATH, old_validation, new_validation)

    old_gates = """## Open hard gates

- RMA-060 long-duration official-model baseline dynamics and monitoring;
"""
    new_gates = """## Open hard gates

- RMA-052 formal authoritative-rendering invariant closure;
- RMA-060 long-duration official-model baseline dynamics and monitoring;
"""
    replace_once(STATUS_PATH, old_gates, new_gates)


if __name__ == "__main__":
    main()
