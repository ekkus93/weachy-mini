#!/usr/bin/env python3
"""Close RMA-042 after verified hosted and physical Android evidence."""

from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TODO_PATH = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS_PATH = ROOT / "docs" / "IMPLEMENTATION_STATUS.md"


def replace_once(text: str, old: str, new: str, label: str) -> str:
    """Replace one exact block and fail on stale or duplicate source text."""
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one source block, found {count}")
    return text.replace(old, new, 1)


def section(text: str, start: str, end: str) -> str:
    """Return one exact heading-delimited section."""
    start_index = text.index(start)
    end_index = text.index(end, start_index)
    return text[start_index:end_index]


def patch_todo() -> None:
    """Close exactly the seven RMA-042 checklist items."""
    original = TODO_PATH.read_text(encoding="utf-8")
    rma041_before = section(
        original,
        "## RMA-041 — Audit mechanical parameters\n",
        "## RMA-042 — Build reference-state comparison tests\n",
    )
    rma050_before = section(
        original,
        "## RMA-050 — Build the Reachy Unity prefab\n",
        "## RMA-051 — Implement state-to-render mapping\n",
    )
    old = """## RMA-042 — Build reference-state comparison tests

- [ ] Produce desktop reference traces using the pinned upstream model and MuJoCo version.
- [ ] Compare Android qpos, qvel, body transforms, and constraint residuals for reset and representative command sequences.
- [ ] Define numeric tolerances and explain platform floating-point differences.
- [ ] Store compact reference fixtures with hashes.

**Acceptance criteria — model integrity gate**

- [ ] Android and desktop reference results agree within documented tolerances.
- [ ] Loop-closure residuals stay bounded.
- [ ] Coordinate conventions are documented and covered by tests.

"""
    new = """## RMA-042 — Build reference-state comparison tests

**Status:** Complete (2026-07-30)

- [x] Produce desktop reference traces using the pinned upstream model and MuJoCo version.
- [x] Compare Android qpos, qvel, body transforms, and constraint residuals for reset and representative command sequences.
- [x] Define numeric tolerances and explain platform floating-point differences.
- [x] Store compact reference fixtures with hashes.

**Acceptance criteria — model integrity gate**

- [x] Android and desktop reference results agree within documented tolerances.
- [x] Loop-closure residuals stay bounded.
- [x] Coordinate conventions are documented and covered by tests.

**Completion evidence**

- `reference-scenario.json` pins the Reachy source/model hash, MuJoCo 3.9.0,
  0.002-second timestep, ordered actuator/body identities, four command phases,
  ten checkpoints, compiled dimensions, and per-field tolerances.
- Desktop generation and the native Android runner execute the same generated
  scenario. The committed compact lock binds the exact desktop trace bytes to the
  scenario, model, runtime, generator, serialization format, checkpoint count,
  and total step count using strict lowercase hexadecimal SHA-256 values.
- The comparator verifies exact platform/scenario/model/runtime/count identities,
  scenario-clock timing, all 37 qpos values, all 30 qvel values, all 17 named body
  positions and normalized wxyz quaternions, warning counts, and the maximum
  absolute residual across every MuJoCo equality row. Quaternion q/-q equivalence
  is accepted; malformed matching traces are rejected.
- Physical Android MuJoCo Feasibility run `30583271127` passed the AArch64 build,
  provenance checks, locked desktop regeneration, and LG-H872 API-26 comparison on
  `d229235d73851f58088b7c142e469ef6cfaeaefb`. Maximum qpos error was
  `3.784299262843405e-15`, maximum qvel error was
  `2.6852825518730583e-13`, and maximum observed equality residual was
  `3.84603861668803e-06` against the `0.001` bound, with zero warnings.
- Hosted Quality Gates run `30583907077` passed Ruff, actionlint, ShellCheck,
  static policy, native warnings/sanitizers, managed tests, Android tests, and
  official-model desktop trace generation on
  `da6fb1fd3e13afe2b2269ee2dd85ba0a0f2826de`.
- Detailed contracts and evidence are in `docs/reference-state-comparison.md` and
  `docs/validation/RMA_042_REFERENCE_STATE_VALIDATION_2026-07-30.md`.

"""
    updated = replace_once(original, old, new, "RMA-042 TODO closure")
    rma042_after = section(
        updated,
        "## RMA-042 — Build reference-state comparison tests\n",
        "# Phase 6 — Unity rendering from authoritative state\n",
    )
    if rma042_after.count("- [x]") != 7 or "- [ ]" in rma042_after:
        raise SystemExit("RMA-042 closure did not produce exactly seven checked items")
    if section(
        updated,
        "## RMA-041 — Audit mechanical parameters\n",
        "## RMA-042 — Build reference-state comparison tests\n",
    ) != rma041_before:
        raise SystemExit("RMA-041 changed during RMA-042 closure")
    if section(
        updated,
        "## RMA-050 — Build the Reachy Unity prefab\n",
        "## RMA-051 — Implement state-to-render mapping\n",
    ) != rma050_before:
        raise SystemExit("RMA-050 changed during RMA-042 closure")
    TODO_PATH.write_text(updated, encoding="utf-8", newline="\n")


def patch_status() -> None:
    """Record RMA-042 completion while leaving RMA-041 open."""
    original = STATUS_PATH.read_text(encoding="utf-8")
    updated = replace_once(
        original,
        """**Current implementation series:** RMA-040 official Reachy Mini model import,
complete topology identity, immutable provenance, and Android/runtime acceptance
after RMA-033 deterministic snapshot and reset closure
""",
        """**Current implementation series:** RMA-042 pinned desktop/Android reference-state
comparison, strict fixture identity, coordinate conventions, and physical ARM64
model-integrity acceptance after RMA-040 official-model import closure
""",
        "implementation-series status",
    )
    updated = replace_once(
        updated,
        """### RMA-041/RMA-042 — parameter audit and reference comparison foundations

The mechanical audit and cross-platform reference infrastructure already exist,
but these tasks remain formally open until their individual requirements and
acceptance criteria are audited and closed. The audit classifies generic actuator
dynamics and missing antenna hard-stop evidence as uncalibrated placeholders; no
calibrated claim is made. Desktop/Android reference traces compare qpos, qvel,
named body transforms, equality residuals, warnings, dimensions, hashes, and
MuJoCo version within locked tolerances.

""",
        """### RMA-041 — mechanical parameter audit foundation

The mechanical audit infrastructure already exists, but RMA-041 remains formally
open until its classifications, joint-limit provenance, uncertainty records, and
machine-readable fidelity exposure are individually audited and closed. Generic
actuator dynamics and missing antenna hard-stop evidence remain explicitly
uncalibrated placeholders; no calibrated claim is made.

### RMA-042 — desktop/Android reference-state comparison

RMA-042 is complete. A versioned scenario pins the exact Reachy model, MuJoCo
3.9.0 runtime, 500 Hz timestep, compiled dimensions, actuator/body order, command
phases, checkpoints, and numeric policies. Desktop Python MuJoCo generation and
the native Android ARM64 runner execute the same generated scenario, while a
compact SHA-256 lock requires byte-identical desktop fixture regeneration.

The comparator requires exact platform, scenario, model, runtime, count, step,
and body identities. It validates every qpos and qvel value, all named body poses,
normalized wxyz quaternions with q/-q equivalence, scenario-clock timing, zero
warnings, and the maximum absolute residual across every equality-constraint row.
Matching-but-malformed traces, non-finite values, wrong clocks, non-unit
quaternions, over-bound loop closures, and non-hexadecimal fixture hashes fail
visibly.

Physical LG-H872 API-26 evidence agrees with the desktop trace by orders of
magnitude inside all locked tolerances. The detailed contract is in
[Desktop/Android reference-state comparison](reference-state-comparison.md), with
validation evidence in
[the RMA-042 validation record](validation/RMA_042_REFERENCE_STATE_VALIDATION_2026-07-30.md).

""",
        "RMA-041/RMA-042 status split",
    )
    updated = replace_once(
        updated,
        """## Current validation evidence

- Hosted RMA-040 run `30567896524`: full pinned-source/topology import,
""",
        """## Current validation evidence

- Physical RMA-042 Android MuJoCo run `30583271127`: regenerated and locked the
  pinned desktop trace, cross-built the AArch64 runtime/reference runner, and
  passed the LG-H872 API-26 state/transform/equality comparison on `d229235d`.
- Hosted RMA-042 quality run `30583907077`: Ruff/actionlint/ShellCheck/static,
  native warnings/sanitizers, managed, Android, official-model, and desktop trace
  generation gates passed on `da6fb1fd`.
- Hosted RMA-040 run `30567896524`: full pinned-source/topology import,
""",
        "RMA-042 validation evidence",
    )
    STATUS_PATH.write_text(updated, encoding="utf-8", newline="\n")


def main() -> None:
    """Apply guarded closure and remove this one-shot helper."""
    patch_todo()
    patch_status()
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
