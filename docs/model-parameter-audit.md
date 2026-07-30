# Reachy Mini model parameter audit

**Audit scope:** pinned upstream MJCF baseline  
**Source repository:** `https://github.com/pollen-robotics/reachy_mini.git`  
**Source commit:** `a739a6e461eb6d722901f1cfc225265ffc85c28d`  
**Model:** `src/reachy_mini/descriptions/reachy_mini/mjcf/reachy_mini.xml`  
**Machine-readable audit:** [`models/reachy-mini/model-parameter-audit.json`](../models/reachy-mini/model-parameter-audit.json)

## Fidelity conclusion

The current model is an **uncalibrated upstream geometric baseline**.

It may be described as:

- CAD-derived geometry and body inertial data;
- an upstream closed-loop kinematic/dynamic baseline;
- a model that compiles and runs on desktop and Android MuJoCo.

It must not be described as:

- a calibrated digital twin;
- a measured servo model;
- a validated hard-stop or collision model;
- a validated electrical, thermal, backlash, compliance, or fault model.

All nine active position actuators use the upstream `chosen_actuator` class. That class inherits the generic `perfect_actuator` defaults. These active dynamics are therefore classified as **placeholder**, even though the model is useful as a geometric and solver-integration baseline.

## Classification vocabulary

| Classification | Meaning in this audit |
|---|---|
| `cad_derived` | Generated from the recorded CAD source or its exported geometry/inertial data; not necessarily checked against a physical unit. |
| `upstream_approximation` | Supplied by the pinned upstream model without evidence sufficient to call it measured, fitted, or a manufacturer specification. |
| `manufacturer_specification` | Traceable to a named manufacturer document and part/revision. None are claimed in this baseline. |
| `measured` | Directly measured from a physical Reachy Mini with recorded method and conditions. None are claimed in this baseline. |
| `fitted` | Estimated from a recorded dataset with a fitting method and held-out validation. None are claimed in this baseline. |
| `placeholder` | Generic, intentionally incomplete, explicitly uncertain, or missing physical behavior. |

## Machine-readable diagnostics contract

The audit schema is version `2` and identifies the contract as
`rma041_parameter_fidelity_v2`. Its `diagnostics` object is intentionally small
and display-ready: it carries the profile ID, status, warning severity, title,
fidelity classification, calibration flag, summary, source-model SHA-256, and
the classifications that block a calibrated claim. CI requires that payload to
be an exact projection of the authoritative `fidelity` and `source` fields, so a
future diagnostics screen cannot silently contradict the audit.

The current payload reports `uncalibrated_upstream_baseline` with warning
severity and `calibrated=false`.

## Structured provenance and uncertainty binding

`joint_limit_provenance` binds every range decision to the pinned MJCF commit,
model SHA-256, `joint.range` attribute, and radian units. It separately lists
explicit hinge ranges, unrestricted passive ball joints, and antenna joints that
lack encoded hard stops. Each joint points back to the applicable policy group
through `range_provenance_id`. No manufacturer specification or measurement
report is claimed.

`source_uncertainties` binds each upstream cautionary comment to its exact scope:
the three retained actuator-default classes and the collision-mesh selection.
For actuator classes, full source validation requires the comment to remain
inside the matching `<default class=...>` block; moving the same text elsewhere
no longer satisfies the audit.

## Source-derived geometry and inertias

The MJCF states that it was generated using `onshape-to-robot` and records the originating Onshape document. Body hierarchy, transforms, visual meshes, collision meshes, masses, centers of mass, and full inertia tensors are therefore classified as `cad_derived`.

This classification does not prove that the exported mass properties match a specific assembled robot. The current baseline does not include a physical weighing, center-of-mass measurement, inertia identification, or assembly-tolerance study.

The source also states that the selected collision meshes are coarse and that finer models exist. Collision fidelity is therefore not yet validated for internal interference, hard stops, or contact-force prediction.

## Joint-limit audit

The exact values below are copied from the pinned MJCF. Explicit ranges are classified as `upstream_approximation` because the source does not link them to a manufacturer specification or physical measurement report. Missing ranges are not silently interpreted as physical freedom.

| Joint | Type | Actuated | MJCF range in radians | Classification | Audit note |
|---|---:|---:|---:|---|---|
| `yaw_body` | hinge | yes | `[-2.792526803190975, 2.792526803190879]` | `upstream_approximation` | Explicit upstream range; not established as a measured hard stop. |
| `stewart_1` | hinge | yes | `[-0.8377580409572196, 1.3962634015955222]` | `upstream_approximation` | Explicit upstream range. |
| `stewart_2` | hinge | yes | `[-1.396263401595614, 1.2217304763958803]` | `upstream_approximation` | Explicit upstream range. |
| `stewart_3` | hinge | yes | `[-0.8377580409572173, 1.3962634015955244]` | `upstream_approximation` | Explicit upstream range. |
| `stewart_4` | hinge | yes | `[-1.3962634015953894, 0.8377580409573525]` | `upstream_approximation` | Explicit upstream range. |
| `stewart_5` | hinge | yes | `[-1.2217304763962082, 1.396263401595286]` | `upstream_approximation` | Explicit upstream range. |
| `stewart_6` | hinge | yes | `[-1.3962634015954123, 0.8377580409573296]` | `upstream_approximation` | Explicit upstream range. |
| `passive_1`–`passive_7` | ball | no | none | `upstream_approximation` | No passive-joint cone, compliance, or hard-stop limit is encoded. |
| `right_antenna` | hinge | yes | none | `placeholder` | Unbounded in MJCF; physical antenna stops are absent. |
| `left_antenna` | hinge | yes | none | `placeholder` | Unbounded in MJCF; physical antenna stops are absent. |

A command limit in a future behavior planner is not a substitute for a physical hard-stop model.

## Actuator-model audit

### Active model: `chosen_actuator`

`chosen_actuator` inherits the upstream `perfect_actuator` default:

| Parameter | Value |
|---|---:|
| joint damping | `0.15` |
| joint friction loss | `0.1` |
| armature | `0.001` |
| position `kp` | `10.0` |
| position `kv` | `0.1` |
| force range | `[-20.0, 20.0]` |

All nine actuators—body yaw, six Stewart actuators, and two antennas—use this class. The model does not encode command latency, sample timing, encoder quantization, voltage dependence, torque-speed behavior, current limiting, backlash, compliance, thermal state, or faults. The active actuator model is therefore `placeholder`.

### Inactive candidate classes retained upstream

| Class | Source statement | Classification | Reason |
|---|---|---|---|
| `xc330m288t` | “probably wrong, would need to re-identify” | `placeholder` | The source explicitly rejects confidence in the values. |
| `sts3215_345` | “Confident that this is realistic (mini duck walks with these values)” | `upstream_approximation` | Behavioral confidence is not a measurement or traceable specification. |
| `sts3215_147` | “Estimation based on the gear ratio difference” | `upstream_approximation` | The source explicitly labels the values as estimated. |

None of these candidate classes is active in the pinned Reachy Mini model.

## Loop-closure solver parameters

The five Stewart-platform loop closures share:

- `solref="0.002 1"`
- `solimp="0.99 0.999 0.0005 0.5 2"`

These values are classified as `upstream_approximation`. They are numerical equality-constraint settings, not measured linkage stiffness, damping, or compliance.

## Calibration-label rule

The machine-readable audit sets:

```json
{
  "calibrated": false,
  "may_be_labeled_calibrated": false,
  "diagnostics_status": "uncalibrated_upstream_baseline"
}
```

CI rejects a calibrated label while any parameter remains classified as `placeholder`. A future calibrated profile must identify its robot, model hash, simulator version, dataset hashes, measurement conditions, fitted parameters, and held-out validation evidence.

## Validation policy

The validator now fails closed when a required classification or provenance ID
is missing, when a parameter claims manufacturer, measured, or fitted evidence
that the audit explicitly records as absent, when a source comment is reassigned
to another model, when the diagnostics payload drifts from fidelity data, or when
the pinned source moves an actuator uncertainty comment outside its class block.

## Required follow-on evidence

RMA-060 through RMA-074 must provide the evidence needed to improve the classification:

1. manufacturer documents for the exact motor and gear variants;
2. measured command timing, encoder behavior, voltage, current, temperature, and faults;
3. fitted friction, backlash, compliance, controller, and torque-speed parameters;
4. physical joint-limit and hard-stop measurements;
5. collision/contact validation;
6. held-out trajectory, settling, current, and load comparisons.

Until those gates pass, diagnostics and UI must present this model as an uncalibrated upstream baseline.
