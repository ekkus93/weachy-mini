# Pluggable servo model contract

## Scope

RMA-061 defines the native data and plug-in boundary for later actuator-fidelity
work. It does **not** replace the accepted RMA-060 `upstream_baseline`, invent
motor constants, or claim a torque/current/thermal model is ready. RMA-062 and
later tasks provide implementations and evidence behind this interface.

The contract lives in `native/reachy_sim` and has no Unity dependency. Unity may
consume authoritative simulation output, but it cannot implement or own servo
physics.

## Native interface

`reachy_servo_model.hpp` defines:

- `ServoCommand`, including command sequence and sample time, mode, position and
  velocity targets, profile velocity and acceleration, feed-forward torque, and
  torque-enable state;
- `ServoObservation`, including position, velocity, applied torque, estimated
  current, supply voltage, temperature, and fault flags;
- `ServoStepResult`, the explicit output of one model step;
- `ServoModel`, a replaceable C++17 interface with parameter identity, reset, and
  deterministic step operations;
- role, mode, fault, quality, and validation enums with stable numeric values.

The interface returns no Unity objects, callbacks, transforms, or managed
containers. A concrete model owns its private state and receives the authoritative
command, observation, and timestep for each step.

## Parameter quality

Every scalar is a `QualifiedScalar` containing:

1. an optional numeric value;
2. one quality label; and
3. a non-empty evidence identifier.

The only quality labels are:

- `placeholder`: unknown, provisional, or explicitly unsuitable for physical
  fidelity;
- `manufacturer_estimate`: derived from a documented manufacturer source but not
  fitted to the represented unit;
- `calibrated`: fitted and validated against retained calibration evidence.

A null scalar is legal only for `placeholder`. A parameter set labeled
`calibrated` is rejected unless every required scalar and the fault model are
also calibrated and populated. This prevents a single calibrated label from
masking unknown current, voltage, encoder, torque-speed, or thermal behavior.

## Role-specific registry

`servo-model-parameters.json` is the authoritative machine-readable RMA-061
registry. It defines three distinct parameter sets:

- `body_yaw_upstream_placeholder`;
- `stewart_upstream_placeholder`;
- `antenna_upstream_placeholder`.

All nine official-model actuators have an explicit ordered binding. The six
Stewart actuators share the Stewart role set; body yaw and antennas cannot bind
to it. The generator rejects missing, reordered, duplicate, unknown, or
cross-role bindings.

The current values are intentionally null placeholders. RMA-041 established that
the active upstream `chosen_actuator` class inherits generic `perfect_actuator`
constants and is not manufacturer or calibration evidence. RMA-061 therefore
records the missing command timing, encoder, current, torque-speed, voltage,
temperature, and fault semantics instead of fabricating them.

## Generation and validation

`scripts/generate_reachy_servo_parameters.py` validates the JSON contract and
generates `reachy_servo_parameters.generated.hpp`. Build and CI use `--check`, so
stale generated C++ or schema drift fails before compilation.

The native registry validates identities, evidence, finite values, positivity,
voltage ordering, temperature ordering, latching-fault subsets, and calibrated
completeness. `IsTorqueModelReady` remains false for the committed placeholder
sets and becomes true only when all required numeric fields are present and the
set is otherwise valid.

## Ownership boundary for later tasks

RMA-062 may add one or more concrete `ServoModel` implementations without
changing Unity rendering or the public C simulation ABI. RMA-063 and RMA-064 may
extend private implementation state for friction, backlash, compliance, power,
and thermal behavior while preserving the command/observation/result contract.
Any future schema revision must be explicit and must not reinterpret an existing
quality label or fault bit.
