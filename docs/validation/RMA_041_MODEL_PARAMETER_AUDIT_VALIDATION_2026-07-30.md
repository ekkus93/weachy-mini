# RMA-041 model parameter audit validation

**Date:** 2026-07-30
**Contract:** `rma041_parameter_fidelity_v2`
**Pinned Reachy commit:** `a739a6e461eb6d722901f1cfc225265ffc85c28d`
**Pinned MJCF SHA-256:** `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`
**Guarded closure commit:** `6809532b3a49911b39bfdf447da0710e76945938`

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

Guarded closure workflow run `30588993044` re-ran Ruff, all focused tests, static
audit validation, exact checklist-boundary checks, and cached-diff validation
before committing the seven-item RMA-041 closure as
`6809532b3a49911b39bfdf447da0710e76945938` and removing its own temporary
script/workflow.

## Result

RMA-041 has a complete human-readable audit, a strict machine-readable fidelity
contract, source-bound classifications and uncertainty evidence, explicit
joint-limit provenance, a display-ready diagnostics payload, and fail-closed
validation preventing unsupported calibration claims. The result remains an
uncalibrated upstream baseline, not a calibrated digital twin.
