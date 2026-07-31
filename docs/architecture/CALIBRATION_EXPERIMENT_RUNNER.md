# RMA-072 Calibration Experiment Runner

## Scope

RMA-072 adds a versioned, deterministic experiment-plan validator, compiler,
dry-run CLI, and physical-adapter execution boundary. It covers the experiment
families required before fitting can begin:

- unloaded actuator sweeps;
- gravity-loaded static poses;
- step response;
- sinusoidal frequency response;
- backlash direction reversals;
- torque-disabled free decay;
- simultaneous multi-actuator commands;
- warm/cold thermal cycling.

The committed smoke plan and CI outputs are synthetic orchestration evidence.
They are not physical Reachy Mini measurements and are not a calibrated
profile. RMA-074 remains responsible for operating a physical unit and
collecting admissible calibration data.

## Versioned plan contract

`calibration/schemas/calibration-experiment-plan-v1.schema.json` describes the
logical JSON representation. `scripts/calibration_experiment.py` is the
authoritative bounded validator.

Every plan binds:

- the exact version-1 schema hash;
- an expected robot identity, hardware revision, and firmware constraint;
- a primary monotonic clock and command sampling rate;
- plan-wide duration, action-count, voltage, current, temperature, and
  simultaneous-actuator limits;
- explicit soft position and profile-velocity limits for every permitted
  actuator;
- an ordered list of typed experiments;
- a canonical SHA-256 over the complete plan except its self-referential
  digest.

Unknown members, duplicate JSON keys, non-finite numbers, malformed
identifiers, undeclared actuators, out-of-range positions, unsafe sampling
frequencies, duplicate experiment identifiers, hash drift, and resource-limit
violations fail closed.

## Deterministic schedule compiler

The compiler expands a validated plan into immutable timestamped actions:

- `marker` actions retain experiment and phase boundaries;
- `torque` actions make enable/disable transitions explicit;
- `command` actions carry position mode, torque state, target position, and
  the declared profile-velocity ceiling.

Simultaneous commands are ordered by actuator identifier without changing
their timestamp. Linear sweeps and sine waves use integer nanosecond schedules
derived from the declared command rate. The final schedule always emits an
explicit safe-shutdown marker, disables torque for every actuator used by the
plan, and then emits `run_complete`.

The schedule receives a canonical SHA-256. Recompiling the same plan must
produce byte-identical actions and the same digest.

## Dry-run CLI and RMA-070 bridge

`scripts/run_calibration_experiment.py` validates and compiles a plan without
moving hardware. It writes:

- a versioned run manifest;
- the complete action schedule;
- RMA-070-shaped command JSONL.

The command JSONL can be passed to
`scripts/capture_reachy_calibration.py`, preserving plan timestamps,
actuator identity, command mode, torque state, target position, and profile
velocity in a normal RMA-070 command stream.

Example:

```bash
python3 scripts/run_calibration_experiment.py \
  --plan calibration/experiments/rma072-smoke-plan.json \
  --manifest-output build/rma072/run-manifest.json \
  --schedule-output build/rma072/schedule.json \
  --command-jsonl-output build/rma072/commands.jsonl
```

## Physical adapter boundary

Physical execution is library-driven rather than exposed as an unsafe generic
CLI switch. A hardware integration implements `ExperimentAdapter` and supplies
robot identity, live safety state, command/torque operations, event markers,
emergency stop, and run-finalization behavior.

`execute_schedule` refuses to begin unless all of these are true:

- the authorization names the exact canonical plan SHA-256;
- the authorized, expected, and connected robot identities match;
- the operator is present;
- the workspace is confirmed clear;
- the emergency stop has been verified;
- the exact acknowledgement text is supplied;
- physical motion is explicitly enabled.

Before every action, the runner checks finite live voltage, temperature, and
current values; emergency-stop availability; and robot fault state. Any
violation, unsupported action, adapter failure, or timing-path exception
invokes the adapter emergency stop and records an aborted outcome. Exceptions
are never swallowed.

RMA-072 defines this adapter contract and validates it with deterministic fake
hardware. A production Reachy transport adapter and real data acquisition
belong to RMA-074.

## Experiment semantics

### Unloaded sweep

Moves one actuator between declared endpoints with deterministic linearly
sampled outbound and return sweeps.

### Gravity-loaded static pose

Commands one or more actuators simultaneously and holds the pose while gravity
remains active. The plan must explicitly set `gravity_loaded=true`.

### Step response

Commands an initial pose, retains a pre-step dwell, applies the target step,
and retains a post-step observation interval.

### Frequency response

Commands a sine wave around a declared center for an ordered list of
frequencies. Frequencies must increase strictly and remain at or below one
quarter of the command sampling rate.

### Backlash reversal

Alternates around a center position with explicit dwell time to preserve
direction-reversal events.

### Free decay

Commands and settles an initial pose, emits a release marker, explicitly
disables torque, and observes the unpowered decay interval.

### Multi-actuator

Commands at least two actuators at one timestamp and holds the resulting pose.
The count cannot exceed the plan-wide concurrency limit.

### Thermal cycle

Records cold-baseline, warm-sequence, and cooldown markers around repeated
bounded position reversals. Temperature acceptance is based on live safety
telemetry, never on an assumed open-loop temperature model.
