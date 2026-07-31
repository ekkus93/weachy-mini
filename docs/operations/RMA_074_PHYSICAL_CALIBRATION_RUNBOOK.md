# RMA-074 Physical Calibration Runbook

## Current blocker

The `kawa` runner is online, but the read-only probe found no reachable Reachy
Mini daemon:

- `127.0.0.1:8000` refused the connection;
- `reachy-mini.local` did not resolve;
- `REACHY_MINI_HOST` was not configured.

Do not proceed to physical motion until the read-only preflight passes.

## 1. Make the real robot reachable

Start the Reachy Mini daemon against the physical robot, not `--sim` or
`--mockup-sim`. The daemon must report:

- state `running`;
- physical backend `ready=true`;
- no backend error;
- a non-null hardware ID;
- valid current joint and head-pose state.

The daemon can be:

- local to `kawa` on port 8000;
- reachable by a LAN hostname or IP supplied as the manual workflow input
  `reachy_host`;
- configured persistently as repository variable `REACHY_MINI_HOST`.

Before using an address, verify from `kawa`:

```bash
curl --fail --silent --show-error http://REACHY_HOST:8000/api/daemon/status
curl --fail --silent --show-error http://REACHY_HOST:8000/api/daemon/hardware-id
```

Do not put a hardware serial number, token, or private address into repository
files. The workflow artifact retains only the SHA-256 of the hardware ID and
redacted candidate labels.

## 2. Run the no-motion preflight

Open the GitHub Actions workflow **RMA-074 Physical Calibration Preflight** and
run it manually. Supply `reachy_host` when mDNS or localhost is not appropriate.

The gate performs only GET requests. It must report:

- `result=physical_unit_ready`;
- zero motion commands;
- zero torque commands;
- a stable hashed unit identity;
- valid joint, antenna, body-yaw, and pose samples;
- an explicit telemetry capability inventory.

A preflight failure is not permission to bypass the gate or substitute
synthetic data.

## 3. Resolve instrumentation before motion

The normal public daemon state does not provide every RMA-074 measurement.
Before building or running the physical `ExperimentAdapter`, identify real
sources for:

- servo current or load;
- bus voltage;
- motor temperature;
- emergency-stop state;
- external pose, when head position/orientation accuracy is claimed;
- force/contact data, when contact accuracy is claimed.

Each source must have an identity, units, monotonic timestamp, clock alignment,
range limits, and failure behavior compatible with the RMA-070 dataset
contract. Do not relabel estimated load as measured current, IMU temperature as
motor temperature, or position error as contact force.

A metric whose instrument is absent must be `unsupported`; it cannot produce a
mature accuracy claim.

## 4. Review the motion plan

The physical fitting and held-out plans must be separate and must produce
separate dataset IDs, hashes, and capture-run IDs. Review:

- actuator and workspace limits;
- warm/cold and free-decay steps;
- final torque-disabled shutdown;
- current, voltage, temperature, fault, and E-stop abort limits;
- exact robot and plan hashes;
- the physical test environment and external-instrument mounting.

The held-out plan must not be used during fitting or threshold selection.

## 5. Required authorization before execution

Physical execution must receive all of the following at the time of the run:

- exact plan SHA-256;
- exact hashed unit identity;
- operator present;
- workspace clear;
- emergency stop verified and reachable;
- explicit physical-motion authorization;
- exact acknowledgement:

```text
RMA-072 PHYSICAL MOTION AUTHORIZED
```

The general instruction to work on RMA-074 is not a substitute for these
run-specific safety facts.

## 6. Capture and validate

For each physical run:

1. Verify the read-only preflight again.
2. Record the authorization and instrument inventory.
3. Execute through the reviewed `ExperimentAdapter`.
4. Abort on stale/missing safety data, communication loss, non-finite values,
   range violations, robot faults, or unavailable E-stop state.
5. Validate the resulting dataset with the RMA-070 validator.
6. Retain the dataset hash, plan hash, capture-run ID, unit hash, and artifact
   digest.

The fitting run and held-out run must be separate physical executions.

## 7. Fit and evaluate

Use RMA-073 to produce an unapproved fit candidate from only the fitting-role
dataset. Freeze the candidate before loading held-out evidence.

The RMA-074 held-out report must contain all eight entries:

- joint position;
- head position;
- head orientation;
- settling;
- overshoot;
- current;
- torque-disabled free decay;
- contact.

Every entry records status, metric, units, value, threshold, sample count,
source streams, and claim scope. Core position/orientation/settling/overshoot/
decay metrics must pass before approval. Current or contact may be unsupported
only when the limitation remains explicit and excluded from mature claims.

## 8. Approve and label

Generate a non-fixture Ed25519 approval key outside the repository. Never use
the committed RMA-073 synthetic test key for a real unit.

Create the approval with `scripts/approve_calibration_profile.py`. The approval
binds the candidate profile, physical datasets, held-out report, unit identity,
compatibility contract, claims, approver statement, canonical hash, and
signature.

Resolve the application label with `scripts/resolve_calibration_mode.py`:

- verified matching approval: `Calibrated for this unit`;
- missing, invalid, incompatible, or unit-mismatched evidence: `Uncalibrated`.

## 9. Completion evidence

RMA-074 closes only after the repository contains:

- successful read-only physical preflight evidence;
- reviewed adapter and run-specific authorization evidence;
- physical fitting and held-out datasets or redistributable derived evidence;
- unit-specific fit candidate;
- complete held-out report;
- signed unit approval using a non-fixture key;
- verified calibrated/uncalibrated selection behavior;
- permanent exact-head CI/device evidence.
