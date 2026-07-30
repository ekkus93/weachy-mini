#!/usr/bin/env python3
"""Create guarded RMA-041 completion documentation without touching adjacent tasks."""

from __future__ import annotations

import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TODO_PATH = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS_PATH = ROOT / "docs" / "IMPLEMENTATION_STATUS.md"
VALIDATION_PATH = (
    ROOT / "docs" / "validation" / "RMA_041_MODEL_PARAMETER_AUDIT_VALIDATION_2026-07-30.md"
)

RMA041_HEADING = "## RMA-041 — Audit mechanical parameters\n"
RMA042_HEADING = "## RMA-042 — Build reference-state comparison tests\n"
RMA050_HEADING = "## RMA-050 — Build the Reachy Unity prefab\n"
RMA051_HEADING = "## RMA-051 — Implement state-to-render mapping\n"

EXPECTED_RMA041 = """## RMA-041 — Audit mechanical parameters

- [ ] Add `docs/model-parameter-audit.md` or an equivalent generated report.
- [ ] Classify each parameter as CAD-derived, upstream approximation, manufacturer specification, measured, fitted, or placeholder.
- [ ] Explicitly flag the generic/perfect actuator parameters as baseline approximations.
- [ ] Record joint-limit provenance.
- [ ] Record any source comments indicating uncertain values.

**Acceptance criteria**

- [ ] No placeholder is included in a profile labeled calibrated.
- [ ] The diagnostics screen can eventually display the model fidelity classification from machine-readable data.

"""

COMPLETED_RMA041 = """## RMA-041 — Audit mechanical parameters

**Status:** Complete (2026-07-30)

- [x] Add `docs/model-parameter-audit.md` or an equivalent generated report.
- [x] Classify each parameter as CAD-derived, upstream approximation, manufacturer specification, measured, fitted, or placeholder.
- [x] Explicitly flag the generic/perfect actuator parameters as baseline approximations.
- [x] Record joint-limit provenance.
- [x] Record any source comments indicating uncertain values.

**Acceptance criteria**

- [x] No placeholder is included in a profile labeled calibrated.
- [x] The diagnostics screen can eventually display the model fidelity classification from machine-readable data.

**Completion evidence**

- `docs/model-parameter-audit.md` defines the human-readable fidelity vocabulary,
  source-derived geometry/inertia scope, joint-limit inventory, actuator-default
  audit, equality-solver classification, calibration-label rule, and required
  follow-on evidence.
- `models/reachy-mini/model-parameter-audit.json` schema version `2` identifies
  contract `rma041_parameter_fidelity_v2`. Every parameter group, joint range,
  actuator, retained actuator-default class, and equality setting has an explicit
  classification. Manufacturer, measured, fitted, and calibrated evidence remain
  explicitly absent rather than inferred.
- The active `chosen_actuator` class inherits the upstream `perfect_actuator`
  defaults and remains a calibration-blocking `placeholder`. The retained
  `xc330m288t` class is also a placeholder; the two retained STS3215 defaults are
  upstream approximations. No profile may be labeled calibrated while any
  placeholder remains.
- `joint_limit_provenance` binds all ranges to the exact pinned MJCF commit and
  SHA-256, the `joint.range` attribute, and radian units. Each joint points to its
  applicable range policy. The two antenna hinges are explicitly recorded as
  lacking encoded hard stops, and the seven passive ball joints remain
  unrestricted upstream approximations.
- `source_uncertainties` binds each audited upstream comment to its exact actuator
  class or collision-mesh scope. Full-source validation requires actuator comments
  to remain inside their matching `<default class=...>` block; moving identical
  text elsewhere does not satisfy the contract.
- The display-ready `diagnostics` object is an exact validator-enforced projection
  of authoritative source and fidelity fields. It reports warning severity,
  `uncalibrated_upstream_baseline`, `calibrated=false`, the source-model hash, and
  the classifications that block calibration.
- Regression tests reject missing/wrong classifications, false measured or
  calibrated claims, joint-provenance drift, new unrecorded ranges, reassigned or
  relocated uncertainty comments, and diagnostics/fidelity disagreement.
- Focused validation run `30587841758` passed Ruff, all 11 parameter-audit tests,
  and static audit validation before publishing artifact
  `rma041-validated-patch-b2f116049df652307af45d7dd90f23bf7473fb8f` with digest
  `0bca4d4a13903d76bb513670be8bd15c80789fcf8fda90c4bc9e7bb95357859f`.
- The official-model job in hosted run `30588235631` passed the exact pinned
  upstream source/topology/parameter audit, Unity visual conversion, MuJoCo
  compile/step, and reference generation with the permanent contract. The run's
  unrelated static job saw a temporary patch helper; that helper and its workflow
  were removed in cleanup commit `a44d1f883e94515c24338b1a7ecb2fcb55430c4e`.
- Detailed validation evidence is in
  `docs/validation/RMA_041_MODEL_PARAMETER_AUDIT_VALIDATION_2026-07-30.md`.

"""

STATUS_HEADER_OLD = """**Current implementation series:** RMA-042 pinned desktop/Android reference-state
comparison, strict fixture identity, coordinate conventions, and physical ARM64
model-integrity acceptance after RMA-040 official-model import closure
"""
STATUS_HEADER_NEW = """**Current implementation series:** RMA-041 machine-readable mechanical-parameter
fidelity, joint-limit provenance, source uncertainty binding, and fail-closed
calibration labeling after RMA-040 official-model import closure
"""

STATUS_RMA041_OLD = """### RMA-041 — mechanical parameter audit foundation

The mechanical audit infrastructure already exists, but RMA-041 remains formally
open until its classifications, joint-limit provenance, uncertainty records, and
machine-readable fidelity exposure are individually audited and closed. Generic
actuator dynamics and missing antenna hard-stop evidence remain explicitly
uncalibrated placeholders; no calibrated claim is made.

"""

STATUS_RMA041_NEW = """### RMA-041 — mechanical parameter audit

RMA-041 is complete. The version-2 machine-readable audit binds the fidelity
profile to the exact pinned Reachy source commit and MJCF SHA-256, classifies every
parameter group, joint range, actuator/default class, and equality-solver setting,
and explicitly records which manufacturer, measured, fitted, and calibrated
evidence is absent.

Active `chosen_actuator` dynamics inherit the upstream `perfect_actuator` defaults
and remain a calibration-blocking placeholder. Antenna hinges remain placeholders
because the source encodes no hard-stop ranges; passive ball-joint and explicit
hinge limits remain upstream approximations rather than physical measurements.
The validator rejects any calibrated label while placeholders remain.

Joint-limit provenance is structured and per-joint. Upstream uncertainty comments
are bound to exact actuator classes or collision-mesh scope, including a source
location check that rejects identical text moved outside the applicable default
block. A display-ready diagnostics projection is checked against authoritative
fidelity/source fields so future UI cannot silently overstate model fidelity.

The detailed contract is in [Model parameter audit](model-parameter-audit.md), with
validation evidence in
[the RMA-041 validation record](validation/RMA_041_MODEL_PARAMETER_AUDIT_VALIDATION_2026-07-30.md).

"""

STATUS_EVIDENCE_MARKER = "## Current validation evidence\n\n"
STATUS_EVIDENCE_INSERT = """## Current validation evidence

- Focused RMA-041 audit run `30587841758`: Ruff, 11 positive/failure-path tests,
  static evidence validation, and deterministic patch artifact publication passed
  before the exact validated bytes were applied to `master`.
- Hosted RMA-041 source gate in run `30588235631`: the official-model job passed
  the exact pinned Reachy source/topology/parameter audit, Unity visual conversion,
  MuJoCo compile/step, and desktop reference generation with the permanent v2
  contract. Temporary patch scaffolding observed only by that run's static job was
  subsequently removed at `a44d1f88`.
"""

VALIDATION_DOCUMENT = """# RMA-041 model parameter audit validation

**Date:** 2026-07-30  
**Contract:** `rma041_parameter_fidelity_v2`  
**Pinned Reachy commit:** `a739a6e461eb6d722901f1cfc225265ffc85c28d`  
**Pinned MJCF SHA-256:** `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`

## Scope

This record covers RMA-041 only: mechanical-parameter classification, source and
joint-limit provenance, upstream uncertainty comments, fail-closed calibration
labeling, and a stable machine-readable diagnostics projection. It does not claim
physical calibration and does not close the later actuator-identification,
collision, thermal, electrical, or release-fidelity tasks.

## Fidelity result

The current profile is `upstream_geometric_baseline` with diagnostics status
`uncalibrated_upstream_baseline`, warning severity, and `calibrated=false`.

The audit permits these parameter evidence classes:

- `cad_derived`;
- `upstream_approximation`;
- `manufacturer_specification`;
- `measured`;
- `fitted`;
- `placeholder`.

The current model uses only `cad_derived`, `upstream_approximation`, and
`placeholder`. Manufacturer-specification, measured, fitted, and calibrated
profiles are explicitly empty. The validator rejects any use of those evidence
classes while their evidence categories remain absent.

## Audited parameter groups

| Group | Classification | Current conclusion |
|---|---|---|
| Body geometry, transforms, meshes, mass and inertia | `cad_derived` | Generated from the recorded Onshape source; not verified against a physical unit. |
| Explicit yaw and Stewart hinge ranges | `upstream_approximation` | Exact pinned MJCF values; not established as measured mechanical hard stops. |
| Antenna hinge ranges | `placeholder` | The pinned source supplies no `range` attribute. |
| Passive ball-joint limits | `upstream_approximation` | The source leaves seven ball joints unrestricted. |
| Active position-actuator dynamics | `placeholder` | All nine active actuators use `chosen_actuator`, inheriting generic `perfect_actuator` defaults. |
| Loop-closure equality settings | `upstream_approximation` | Numerical MuJoCo solver settings, not measured linkage compliance. |

## Joint-limit provenance

The `joint_limit_provenance` object binds the inventory to the pinned source
commit, model SHA-256, `joint.range` attribute, and radian units. It separates:

- seven explicit ranges: `yaw_body` and `stewart_1` through `stewart_6`;
- seven unrestricted passive ball joints;
- two antenna hinges with missing encoded hard stops.

Every joint has a `range_provenance_id` that points back to the applicable audit
group. Manufacturer specification and measurement-report fields are explicitly
null.

## Actuator defaults and uncertainty binding

The active `chosen_actuator` class remains a placeholder because it inherits
`perfect_actuator`. The retained inactive classes are classified independently:

- `xc330m288t`: placeholder, with the source warning that it is probably wrong and
  needs re-identification;
- `sts3215_345`: upstream approximation, with behavioral confidence but no
  measurement or manufacturer evidence;
- `sts3215_147`: upstream approximation, explicitly estimated from gear ratio.

`source_uncertainties` binds these comments to their exact actuator model IDs and
binds the coarse-collision-mesh comment to collision selection. Full pinned-source
validation additionally requires each actuator comment to remain inside its exact
`<default class=...>` block.

## Diagnostics contract

The `diagnostics` object is an exact projection of authoritative source/fidelity
data. It contains:

- schema version and profile ID;
- status and warning severity;
- display title and fidelity classification;
- `calibrated=false`;
- summary and source-model SHA-256;
- `placeholder` as the calibration-blocking classification.

The validator compares this object for exact equality with the source and fidelity
fields. A future diagnostics screen can consume the compact object without
reinterpreting the full audit, while CI prevents it from silently diverging.

## Failure-path coverage

The focused suite rejects:

- a placeholder-containing profile labeled calibrated;
- missing or changed parameter-group classifications;
- a measured claim while measured evidence is explicitly absent;
- joint-limit source/provenance drift;
- a missing per-joint provenance identifier;
- a newly added source range that the audit does not record;
- uncertainty comments reassigned between model scopes;
- an actuator comment moved outside its matching source class;
- diagnostics fields that contradict fidelity data.

The positive fixture validates all source defaults, joint ranges, actuator
mappings, equality settings, comments, and hashes together.

## Validation evidence

Focused workflow run `30587841758` passed Ruff, all 11 audit tests, and static
validation. It published the exact four-file patch artifact with SHA-256 digest
`0bca4d4a13903d76bb513670be8bd15c80789fcf8fda90c4bc9e7bb95357859f`.

The official-model job in hosted run `30588235631` then checked the permanent
contract against the clean pinned upstream checkout. Exact source identity,
topology, parameter audit, Unity visual conversion, MuJoCo compile/step, and
desktop reference generation all passed. That workflow's static job inspected a
temporary patch helper and failed Ruff; the helper and temporary workflow were
subsequently deleted, ending at cleanup commit
`a44d1f883e94515c24338b1a7ecb2fcb55430c4e`.

## Result

RMA-041 has a complete human-readable audit, a strict machine-readable fidelity
contract, source-bound classifications and uncertainty evidence, explicit
joint-limit provenance, a display-ready diagnostics payload, and fail-closed
validation preventing unsupported calibration claims. The result remains an
uncalibrated upstream baseline, not a calibrated digital twin.
"""


def section(text: str, start: str, end: str) -> str:
    """Return one exact section delimited by headings."""
    start_index = text.index(start)
    end_index = text.index(end, start_index)
    return text[start_index:end_index]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    """Replace exactly one guarded fragment."""
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def sha256(text: str) -> str:
    """Return a UTF-8 SHA-256 for invariant checks."""
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def close_todo() -> None:
    """Close exactly the seven RMA-041 checklist items."""
    before = TODO_PATH.read_text(encoding="utf-8")
    before_042 = section(before, RMA042_HEADING, RMA050_HEADING)
    before_050 = section(before, RMA050_HEADING, RMA051_HEADING)
    if section(before, RMA041_HEADING, RMA042_HEADING) != EXPECTED_RMA041:
        raise RuntimeError("RMA-041 TODO block differs from guarded source")
    if EXPECTED_RMA041.count("- [ ]") != 7 or COMPLETED_RMA041.count("- [x]") != 7:
        raise RuntimeError("RMA-041 closure must contain exactly seven checklist transitions")

    after = replace_once(before, EXPECTED_RMA041, COMPLETED_RMA041, "RMA-041 TODO")
    if sha256(section(after, RMA042_HEADING, RMA050_HEADING)) != sha256(before_042):
        raise RuntimeError("RMA-042 changed during RMA-041 closure")
    if sha256(section(after, RMA050_HEADING, RMA051_HEADING)) != sha256(before_050):
        raise RuntimeError("RMA-050 changed during RMA-041 closure")
    TODO_PATH.write_text(after, encoding="utf-8", newline="\n")


def close_status() -> None:
    """Replace the open-foundation status with the completed contract."""
    text = STATUS_PATH.read_text(encoding="utf-8")
    text = replace_once(text, STATUS_HEADER_OLD, STATUS_HEADER_NEW, "status header")
    text = replace_once(text, STATUS_RMA041_OLD, STATUS_RMA041_NEW, "status RMA-041")
    text = replace_once(
        text,
        STATUS_EVIDENCE_MARKER,
        STATUS_EVIDENCE_INSERT,
        "status validation evidence",
    )
    STATUS_PATH.write_text(text, encoding="utf-8", newline="\n")


def main() -> None:
    """Generate all guarded closure files."""
    close_todo()
    close_status()
    if VALIDATION_PATH.exists():
        raise RuntimeError(f"Validation record already exists: {VALIDATION_PATH}")
    VALIDATION_PATH.parent.mkdir(parents=True, exist_ok=True)
    VALIDATION_PATH.write_text(VALIDATION_DOCUMENT, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
