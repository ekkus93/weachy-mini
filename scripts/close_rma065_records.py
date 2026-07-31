#!/usr/bin/env python3
"""Close RMA-065 tracking records after accepted hosted/device validation."""

from __future__ import annotations

import re
from pathlib import Path


TODO_PATH = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
STATUS_PATH = Path("docs/IMPLEMENTATION_STATUS.md")


COMPLETED_TODO = """## RMA-065 — Add collision and hard-stop model

**Status:** Complete (2026-07-31)

- [x] Audit existing collision geometry.
- [x] Add coarse validated collision shapes for motor arms, rods, moving platform, head shell, body shell, and antennas where required.
- [x] Add mechanical hard stops distinct from soft command limits.
- [x] Expose contact pairs, impulses/forces, and overload events.
- [x] Benchmark contact cost on Android.

**Acceptance criteria — dynamics baseline gate**

- [x] Representative internal and external contacts are stable.
- [x] Invalid commands cannot pass through hard stops without a reported fault.
- [x] Collision complexity remains within the measured device budget.

**Completion evidence**

- The immutable pinned source model now generates 17 named coarse collision
  primitives plus the retained source shell colliders, producing 25 active
  collision geoms across 17 bodies and 9 explicit limited joints.
- Shell, moving, and external masks are explicit. Topology exclusions are
  source-bound and validated exactly; neutral simulation remains contact-free.
- Soft actuator command ranges are inset from separate hard joint ranges.
  Yaw and antenna outward-motion trials observe their limit constraints and
  remain inside the hard range with zero MuJoCo warnings.
- State-format-v2 diagnostics expose contact geom/body pairs, position,
  normal, penetration, normal/tangent force, impulse, contact classification,
  overload flags, hard-stop observations, events, and health flags while the
  state-format-v1 ABI remains compatible.
- Permanent run `30654822714` passed schema/failure tests, strict native
  compilation, ASan/UBSan, real MuJoCo contact and hard-stop validation,
  state-v2 telemetry, Android API-26 ARM64 build, AArch64 verification, and
  the physical LG-H872 benchmark on exact implementation commit
  `08bf637a12dbe77591d3827412a752d3d4e28fba`.
- The phone completed 50,000 source and 50,000 enhanced steps with zero
  warnings and zero penetration. Realtime factors were `9.180208968594009`
  and `9.97500021157112`; enhanced p95 was `222.2909824922681` us and the
  p95 overhead ratio was `-0.06812249472499832`, below the `0.35` budget.
- Collision shapes, thresholds, and antenna ranges remain explicit
  engineering estimates and are not labeled calibrated.
- Detailed evidence is in
  `docs/validation/RMA_065_COLLISION_HARD_STOP_VALIDATION_2026-07-31.md`.
"""


STATUS_SECTION = """### RMA-065 — collision and hard-stop baseline

RMA-065 is complete. The pinned Reachy source now deterministically generates
coarse collision coverage for the base/body shells, six motor arms, six rods,
moving platform, head shell, and both antennas. Explicit mask roles and 18
source-bound topology exclusions prevent unvalidated self-collision pairs while
retaining representative moving-to-shell and external contact behavior.

Mechanical hard stops are distinct from inset actuator soft ranges. Source yaw
and Stewart ranges remain pinned-source values; antenna ranges remain explicit
engineering estimates. State-format-v2 extends diagnostics with contact
geom/body pairs, forces, impulses, penetration, classification and overload
flags, plus hard-stop observations/events and health flags, without breaking
the existing state-format-v1 contract.

Permanent run `30654822714` passed 25 focused Python regressions, deterministic
model generation, strict native compilation, ASan/UBSan, fake and real MuJoCo
ABI/state tests, 5,000-step neutral/contact/hard-stop validation, Android API-26
ARM64 cross-compilation, AArch64 verification, and the physical LG-H872 device
gate on exact implementation commit
`08bf637a12dbe77591d3827412a752d3d4e28fba`.

The phone completed 50,000 source and 50,000 enhanced steps with zero warnings
and zero penetration. Source/enhanced realtime factors were
`9.180208968594009` and `9.97500021157112`; p95 step times were
`238.5409898124635` us and `222.2909824922681` us, for overhead ratio
`-0.06812249472499832` against the `0.35` ceiling. Collision and antenna-stop
parameters remain visibly uncalibrated engineering estimates. Detailed evidence
is in
[the RMA-065 validation record](validation/RMA_065_COLLISION_HARD_STOP_VALIDATION_2026-07-31.md).

"""


OLD_OPEN_BULLET = """- The committed bus and thermal constants remain explicit engineering
  estimates pending physical capture; RMA-065 still owns collisions and
  mechanical hard stops, and production MuJoCo profile selection remains
  explicit future work.
"""


NEW_EVIDENCE_BULLETS = """- RMA-065 run `30654822714`: permanent hosted schema, report, strict
  native, ASan/UBSan, real state-v2 telemetry, Android ARM64 build, and
  physical LG-H872 50,000-step source/enhanced benchmarks passed on
  `08bf637a12dbe77591d3827412a752d3d4e28fba`.
- RMA-065 collision primitives, thresholds, and antenna stop ranges remain
  explicit engineering estimates pending physical identification; production
  fidelity-profile selection remains future work.
"""


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label} replacement count={count}")
    return text.replace(old, new, 1)


def update_todo() -> None:
    text = TODO_PATH.read_text(encoding="utf-8")
    start_marker = "## RMA-065 — Add collision and hard-stop model\n"
    end_marker = "\n---\n\n# Phase 8"
    if text.count(start_marker) != 1:
        raise RuntimeError(f"TODO RMA-065 marker count={text.count(start_marker)}")
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    text = text[:start] + COMPLETED_TODO + text[end:]
    TODO_PATH.write_text(text, encoding="utf-8", newline="\n")


def update_status() -> None:
    text = STATUS_PATH.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "**Updated:** 2026-07-30",
        "**Updated:** 2026-07-31",
        "status date",
    )
    text, count = re.subn(
        r"\*\*Current implementation series:\*\*.*?\n\n## Repository rules in force",
        "**Current implementation series:** RMA-065 collision geometry, "
        "mechanical hard stops, contact/overload telemetry, and physical "
        "Android complexity validation\n\n## Repository rules in force",
        text,
        count=1,
        flags=re.DOTALL,
    )
    if count != 1:
        raise RuntimeError(f"implementation-series replacement count={count}")
    marker = "## Current validation evidence\n"
    text = replace_once(text, marker, STATUS_SECTION + marker, "RMA-065 status section")
    text = replace_once(
        text,
        OLD_OPEN_BULLET,
        NEW_EVIDENCE_BULLETS,
        "RMA-065 open evidence bullet",
    )
    STATUS_PATH.write_text(text, encoding="utf-8", newline="\n")


def main() -> int:
    update_todo()
    update_status()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
