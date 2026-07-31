# RMA-061 pluggable servo model validation

**Status:** Complete  
**Validated commit:** `68c035ab20ec20a28c8b287914d43dcaf7ad1c67`  
**Workflow:** `RMA-061 Servo Model Contract`  
**Successful run:** `30601191456`  
**Commit status:** `RMA-061 Servo Model Contract` — success

## Acceptance result

RMA-061 is accepted. The repository now has a native C++17 servo-model interface
that is independent of Unity, an authoritative machine-readable parameter
registry, deterministic generated C++ bindings, explicit parameter-quality
semantics, and ordered role-safe bindings for all nine official-model actuators.

RMA-061 intentionally defines no physical torque implementation. The committed
role sets retain null `placeholder` values for command timing, encoder
quantization, current limits, torque-speed behavior, voltage, temperature, and
fault semantics because the pinned upstream `chosen_actuator` class is not
manufacturer or calibration evidence. `IsTorqueModelReady` therefore returns
false for every committed set. RMA-062 must supply documented values and a
concrete implementation before these parameters can drive torque.

## Native interface

`native/reachy_sim/include/reachy_servo_model.hpp` defines:

- `ServoCommand` with sequence, sample time, mode, position/velocity targets,
  profile velocity/acceleration, feed-forward torque, and torque-enable state;
- `ServoObservation` with position, velocity, applied torque, estimated current,
  supply voltage, temperature, and fault flags;
- `ServoStepResult` for torque/current/temperature/fault output;
- the replaceable `ServoModel` interface with parameter identity, reset, and
  deterministic step operations;
- stable enums for modes, actuator roles, fault bits, quality, and validation
  failures.

The focused workflow explicitly rejected `UnityEngine`, `MonoBehaviour`,
`GameObject`, `Transform`, and `Unity::` ownership in the native interface and
implementation.

## Parameter registry

`models/reachy-mini/servo-model-parameters.json` identifies contract
`rma061_servo_model_v1` and binds the exact pinned Reachy source commit, model
SHA-256, RMA-041 audit contract, and active upstream actuator class.

Three distinct role sets are defined:

| Role | Parameter-set ID | Current quality |
| --- | --- | --- |
| Body yaw | `body_yaw_upstream_placeholder` | `placeholder` |
| Stewart platform | `stewart_upstream_placeholder` | `placeholder` |
| Antennas | `antenna_upstream_placeholder` | `placeholder` |

All nine actuator bindings are explicit and preserve official-model order:
`yaw_body`, `stewart_1` through `stewart_6`, `right_antenna`, and
`left_antenna`. The generator rejects missing, reordered, duplicate, unknown,
or cross-role bindings.

Every scalar stores an optional value, one of the exact labels
`placeholder`, `manufacturer_estimate`, or `calibrated`, and a non-empty
evidence identifier. Null values are accepted only as placeholders. A set cannot
be labeled calibrated unless all 15 required scalar fields and its fault model
are populated and calibrated.

## Generation and native validation

`scripts/generate_reachy_servo_parameters.py` validates the JSON contract and
produces `native/reachy_sim/src/reachy_servo_parameters.generated.hpp`. The
committed generated header was reproduced byte-for-byte during the gate.

The C++ registry validates:

- non-empty set/source/evidence identities;
- finite numeric values;
- positive or non-negative domains;
- ordered minimum/nominal/maximum voltage;
- ordered ambient/warning/shutdown temperatures;
- latching faults as a subset of supported faults;
- complete calibrated evidence before a calibrated set is accepted.

## Validation evidence

Hosted run `30601191456` used Ubuntu 24.04, Python 3.11.15, and GNU C/C++ 13.3.0.
It passed:

1. exact generated-header verification for `rma061_servo_model_v1`;
2. eight Python schema and failure-path tests;
3. the explicit Unity-dependency rejection;
4. CMake configuration with `BUILD_TESTING=ON`, strict first-party warnings,
   AddressSanitizer, and UndefinedBehaviorSanitizer;
5. compilation of `libreachy_servo_model.a` and
   `reachy_servo_model_test` without warnings;
6. the native test executable, including plug-in derivation, role-safe lookup,
   placeholder readiness rejection, voltage/temperature/fault validation,
   calibrated-completeness rejection, and fault-bit behavior.

CTest reported `1/1` passing tests and zero failures. The workflow published a
successful `RMA-061 Servo Model Contract` commit status on exact commit
`68c035ab20ec20a28c8b287914d43dcaf7ad1c67`.

## Conclusion

The native actuator-fidelity layer now has a stable, source-bound plug-in and
parameter contract without moving physics into Unity or fabricating motor data.
Body, Stewart, and antenna roles are distinct; every official actuator is bound;
all requested command, encoder, current, torque-speed, voltage, temperature, and
fault concepts are represented; and parameter quality cannot silently escalate.
RMA-061 may be marked complete. RMA-062 is the next ordered task and must provide
the first documented noncalibrated electrical/controller implementation.
