# Physical Calibration and Profile Approval

## Scope

RMA-074 is the first task allowed to turn an RMA-073 fit candidate into a
unit-specific calibrated profile. That transition is evidence-driven. A
reachable daemon, a synthetic dataset, or a passing fitting test is not enough.

The workflow is split into four gates:

1. read-only physical-unit discovery;
2. explicitly authorized physical capture;
3. frozen fitting followed by independent held-out evaluation;
4. calibrated-profile approval and publication.

No gate may infer a missing measurement, weaken an RMA-070 range check, reuse a
fitting dataset as held-out evidence, or label a profile calibrated merely
because some fitted parameters passed.

## Read-only discovery

`scripts/probe_reachy_physical_unit.py` uses only HTTP GET requests against the
pinned Reachy Mini daemon API. It queries:

- `/api/daemon/status`;
- `/api/daemon/hardware-id`;
- `/api/state/full` with current joints, body yaw, antennas, and a pose matrix.

The probe rejects MuJoCo and mock backends, a non-running daemon, an unready or
faulted backend, a missing hardware identifier, malformed/non-finite state, and
incorrect state dimensions. It records a SHA-256 of the hardware identifier,
never the raw identifier or network host.

The probe issues zero motion and zero torque commands. Its result only proves
that a real unit is reachable and stable enough for the next gate.

## Telemetry capability boundary

The pinned public daemon state exposes joint positions, body yaw, antenna
positions, head pose, and—on Wireless units—IMU data. It does not expose
servo-bus current, bus voltage, per-motor temperature, force/torque, or contact
measurements through the normal state transport.

RMA-074 therefore fails closed:

- parameters and held-out metrics may be accepted only from streams actually
  captured by an identified source;
- IMU temperature is not relabeled as motor temperature;
- estimated load is not relabeled as measured current;
- contact is not inferred from position error alone;
- an approved profile cannot claim mature accuracy for a metric whose required
  source was unavailable.

A later physical capture adapter may use an exclusive low-level motor-controller
session or declared external instruments, but it must preserve source identity,
clock alignment, register configuration, units, and RMA-070 integrity.

## Motion authorization

Physical motion remains behind the RMA-072 `ExperimentAdapter` contract. A run
requires the exact plan hash, connected/authorized robot identity equality,
operator presence, workspace clearance, emergency-stop verification, the exact
acknowledgement string, and explicit physical-motion authorization.

The read-only preflight workflow cannot be converted into a motion workflow by
an environment variable or fallback. Motion capture must use a separately
reviewed workflow and explicit dispatch inputs.

## Fitting and held-out policy

Training and held-out captures must be separate physical runs with different
dataset IDs and hashes. Fitting output is frozen before held-out data is loaded.

The RMA-074 held-out report must cover, where supported by measured data:

- joint position error;
- head position and orientation error;
- settling time;
- overshoot;
- current prediction error;
- torque-disabled decay;
- contact or force response.

Every metric has an explicit threshold, source stream, sample count, and
pass/fail result. A failed or unsupported metric remains visible and prevents a
mature claim for that metric.

## Profile approval

RMA-073 candidates remain `calibrated=false` and
`approval_state=unapproved_fit_candidate`. RMA-074 will define a separate
approval envelope rather than weakening the RMA-073 validator.

An approved profile must bind:

- the verified RMA-073 candidate profile hash and signature;
- exact physical fitting and held-out dataset hashes;
- a hashed unit identity;
- model, MuJoCo, simulator ABI, and servo-contract compatibility;
- held-out metric results;
- approval policy version and approver statement;
- a new canonical hash and production signing key.

The synthetic repository key is never a production approval key.

## UI labeling contract

Until an approved RMA-074 envelope verifies, the only valid user-facing mode is
`Uncalibrated`. A verified, compatible, unit-matching approval envelope permits
`Calibrated for this unit`. Missing, invalid, incompatible, unsupported, or
unit-mismatched evidence must resolve to `Uncalibrated` with a diagnostic
reason.

Actual settings-screen integration remains part of the Phase 9 UI work, but the
profile-selection service and its tests must enforce this labeling contract
before RMA-074 can close.
