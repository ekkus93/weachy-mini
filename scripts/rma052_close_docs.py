#!/usr/bin/env python3
"""Close RMA-052 documentation after exact-head validation."""

from pathlib import Path
from textwrap import dedent

TODO = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
STATUS = Path("docs/IMPLEMENTATION_STATUS.md")
ARCH = Path("docs/architecture/AUTHORITATIVE_UNITY_RENDERING.md")
PENDING = Path("docs/validation/RMA_052_VALIDATION_PENDING_2026-07-30.md")
FINAL = Path("docs/validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md")
VALIDATED = "5d5bc2cb078ef5432c0ad6f95599890150330da6"


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one {label} in {path}, found {count}.")
    path.write_text(text.replace(old, new), encoding="utf-8")


def replace_section(path: Path, start: str, end: str, replacement: str) -> None:
    text = path.read_text(encoding="utf-8")
    if text.count(start) != 1 or text.count(end) != 1:
        raise SystemExit(f"Expected unique section markers in {path}.")
    prefix, remainder = text.split(start, 1)
    _, suffix = remainder.split(end, 1)
    path.write_text(prefix + replacement + end + suffix, encoding="utf-8")


def close_todo() -> None:
    section = dedent(
        """\
        ## RMA-052 — Add authoritative-rendering invariant checks

        **Status:** Complete (2026-07-30)

        - [x] Add development-build assertions comparing Unity rendered
          transforms to the mapped MuJoCo snapshot.
        - [x] Report drift above tolerance.
        - [x] Ensure animation, Timeline, Animator, and physics components cannot
          write mapped transforms.
        - [x] Disable or reject Unity Rigidbody/ArticulationBody components on
          authoritative robot bodies.

        **Acceptance criteria — authoritative rendering gate**

        - [x] Forced transform modification is detected in tests/development builds.
        - [x] Production rendering contains no hidden kinematic fallback.

        **Completion evidence — 2026-07-30**

        - The renderer records expected Unity world transforms, authoritative
          sequence, interpolation target time, continuity identity, and configured
          tolerances after every mapped MuJoCo pose.
        - `Application.onBeforeRender` performs the final frame-boundary comparison.
          Editor and development players emit an assertion before entering the same
          fail-closed renderer fault used by release players.
        - `ReachyAuthoritativeInvariantReport` preserves expected/actual transforms,
          drift, body identity, sequence/time/continuity, and both tolerances.
          Invalid, zero, negative, NaN, and infinite tolerances are rejected.
        - Tests force transform drift and require the assertion, retained report,
          renderer fault, and disabled motion authority. Descendant tests reject
          Rigidbody, Rigidbody2D, ArticulationBody, Animator, legacy Animation,
          and PlayableDirector/Timeline writers.
        - Hosted run `30594656829` passed managed, native, official-model, static,
          and Android gates on exact commit
          `5d5bc2cb078ef5432c0ad6f95599890150330da6`.
        - Self-hosted `kawa` run `30594656835` passed Unity tests, production ARM64
          MuJoCo staging, API-26 IL2CPP build/verification, installed lifecycle and
          physical authoritative-rendering acceptance, and artifact uploads on the
          same exact commit.
        - Physical evidence retained renderer status `Rendering`, runtime status
          `Running`, all 18 canonical body bindings, canonical motion/reset checks,
          and `hidden_kinematic_fallback=false`.
        - Detailed evidence is in
          `docs/validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md`.

        """
    )
    replace_section(
        TODO,
        "## RMA-052 — Add authoritative-rendering invariant checks\n",
        "---\n\n# Phase 7 — Dynamics baseline and actuator fidelity",
        section,
    )


def close_status() -> None:
    replace_once(
        STATUS,
        dedent(
            """\
            **Current implementation series:** RMA-051 allocation-free authoritative
            state-to-render mapping, timestamp interpolation, discontinuity handling,
            generated diagnostics, and physical Android acceptance after RMA-050 prefab
            closure
            """
        ),
        dedent(
            """\
            **Current implementation series:** RMA-052 pre-render authoritative
            invariant assertions, exact drift diagnostics, prohibited-writer rejection,
            and physical Android acceptance after RMA-051 state-to-render closure
            """
        ),
        "implementation-series header",
    )
    section = dedent(
        """\
        ### RMA-052 — authoritative-rendering invariant closure

        RMA-052 is complete. Every rendered pose retains the expected Unity world
        transforms derived from the mapped MuJoCo pair, plus authoritative sequence,
        interpolation target time, continuity identity, and finite positive drift
        tolerances.

        The renderer validates before the next pose and at
        `Application.onBeforeRender`. Development players assert on drift; release
        players execute the same comparison without the assertion log. Every build
        faults, disables the renderer, and propagates failure into the production
        runtime rather than overwriting a competing writer or using cosmetic motion.

        `ReachyAuthoritativeInvariantReport` retains body identity, expected/actual
        transforms, measured drift, sequence/time/continuity, and both tolerances.
        The authoritative hierarchy rejects physics, articulation, Animator, legacy
        Animation, and PlayableDirector/Timeline writers on mapped descendants.

        Hosted run `30594656829` and self-hosted `kawa` run `30594656835` passed on
        exact commit `5d5bc2cb078ef5432c0ad6f95599890150330da6`. Device evidence
        retained the production MuJoCo source, renderer health, canonical motion and
        reset checks, and `hidden_kinematic_fallback=false`. Detailed evidence is in
        the [RMA-052 validation record](validation/RMA_052_AUTHORITATIVE_RENDERING_INVARIANTS_2026-07-30.md).

        """
    )
    replace_once(
        STATUS,
        "## Current validation evidence\n",
        section + "## Current validation evidence\n",
        "validation heading",
    )
    evidence = dedent(
        """\
        - Hosted RMA-052 run `30594656829`: managed/native tests, sanitizers,
          pinned-model conversion/reference generation, static policy, and Android
          checks passed on exact commit
          `5d5bc2cb078ef5432c0ad6f95599890150330da6`.
        - Self-hosted RMA-052 run `30594656835`: generated presentation preparation,
          production ARM64 MuJoCo staging, Unity invariant tests, ARM64 API-26
          IL2CPP build/verification, installed lifecycle acceptance, physical
          authoritative rendering, evidence uploads, and APK upload passed on the
          same exact commit.
        """
    )
    replace_once(
        STATUS,
        "## Current validation evidence\n\n",
        "## Current validation evidence\n\n" + evidence,
        "evidence insertion point",
    )
    replace_once(
        STATUS,
        "- RMA-052 formal authoritative-rendering invariant closure;\n",
        "",
        "RMA-052 open gate",
    )


def close_architecture() -> None:
    section = dedent(
        """\
        ## Invariant enforcement

        After applying a pose, the renderer records expected Unity world transforms
        plus authoritative sequence, interpolation target time, continuity identity,
        and configured position/rotation tolerances. It validates before the next
        pose and through `Application.onBeforeRender`, catching later `LateUpdate`
        writers before presentation.

        Editor and development players assert with body, sequence, simulation time,
        continuity, measured drift, and tolerance. Release players execute the same
        comparison without the assertion log. In every build, drift creates a
        retained `ReachyAuthoritativeInvariantReport`, faults and disables the
        renderer, and propagates into the production runtime. It never restores the
        pose silently, accepts competing authority, or selects cosmetic fallback.

        The report contains expected/actual position and rotation, measured drift,
        body identity, sequence/time/continuity, and thresholds. Tolerances must be
        finite and positive.

        The authoritative hierarchy rejects these component classes on mapped
        bodies or visual descendants:

        - `Rigidbody` and `Rigidbody2D`;
        - `Joint` and `Joint2D`;
        - `ArticulationBody`;
        - `Animator` and legacy `Animation`;
        - `PlayableDirector`.

        The successful render path performs no collection or array allocation.
        Bindings, reusable source frames, expected-pose arrays, and invariant storage
        are created during configuration.

        """
    )
    replace_section(ARCH, "## Invariant enforcement\n", "## Optional diagnostics", section)


def write_record() -> None:
    FINAL.write_text(
        dedent(
            """\
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
            """
        ),
        encoding="utf-8",
    )


def main() -> None:
    if not PENDING.is_file() or VALIDATED not in PENDING.read_text(encoding="utf-8"):
        raise SystemExit("Pending marker does not identify the validated exact head.")
    if FINAL.exists():
        raise SystemExit(f"Validation record already exists: {FINAL}")
    close_todo()
    close_status()
    close_architecture()
    write_record()
    PENDING.unlink()


if __name__ == "__main__":
    main()
