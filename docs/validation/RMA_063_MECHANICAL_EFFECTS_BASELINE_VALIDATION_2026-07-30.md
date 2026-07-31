# RMA-063 mechanical effects baseline validation

**Status:** Complete  
**Validated commit:** `a15a1154e62a95999b482ed6b2e6f62f51379929`  
**Workflow:** `RMA-063 Mechanical Effects Baseline`  
**Successful run:** `30606712074`  
**Commit status:** `RMA-063 Mechanical Effects Baseline` — success

## Acceptance result

RMA-063 is accepted. The repository now contains a native, Unity-independent
mechanical-effects decorator for the RMA-061 `ServoModel` contract. It can wrap
the RMA-062 electrical/controller baseline and adds role-specific Coulomb and
viscous friction, stiction and breakaway hysteresis, backlash play, reduced-order
torsional compliance, independent effect switches, and bounded
parameter-identification state.

The committed parameters remain engineering hypotheses. They are not physical
robot measurements, manufacturer friction specifications, or calibrated
profiles. RMA-063 does not silently select this model in the production MuJoCo
actuator path.

## Composition and ownership

`MechanicalServoModel` receives another `ServoModel` by reference. It does not
own an electrical controller and does not change the public simulation C ABI.
The step order is:

1. apply backlash to position targets;
2. invoke the wrapped electrical/controller model;
3. transmit its requested torque through bounded compliance state;
4. apply stiction or kinetic friction to the transmitted torque;
5. retain the wrapped model's current estimate, temperature, and fault flags.

With friction, stiction, backlash, and compliance all disabled, the original
command and complete `ServoStepResult` are preserved exactly.

## Friction and stiction

Kinetic friction combines a Coulomb term with a velocity-proportional viscous
term and opposes measured joint motion. Near zero velocity, drive-torque
direction is used so quantized velocity cannot create a friction torque that
reverses the requested direction.

Stiction uses distinct entry and exit velocity thresholds. The model enters the
stuck state only when speed is inside the entry band and transmitted torque does
not exceed breakaway torque. While stuck it cancels transmitted torque. It exits
only when torque exceeds breakaway or speed reaches the higher exit threshold.
This hysteresis prevents numerical chatter around zero velocity.

## Backlash and compliance

Position targets pass through a symmetric one-dimensional play operator. After
a direction reversal, the transmitted target remains fixed until the input
crosses the opposite side of the configured half-width dead zone. Velocity and
direct-torque modes are unchanged by backlash.

Compliance is a bounded first-order torsional transmission model. Stiffness and
maximum elastic deflection define the maximum transmissible torque, while the
ratio of damping to stiffness defines the state time constant. The result is
deterministic torque lag and bounded elastic deflection without claiming a
full motor/gear/load two-inertia model.

## Independent experiment controls

`MechanicalEffectConfiguration` exposes separate switches for:

- Coulomb and viscous friction;
- stiction and breakaway;
- backlash;
- compliance.

Changing the configuration clears transient stiction, compliance, and reversal
state and anchors backlash to the last observed joint position. This prevents
state from one A/B experiment contaminating the next.

## Identification hooks

The high-frequency step path invokes no external callback. Each step updates a
copyable `MechanicalIdentificationSample` containing command identity, measured
position and velocity, electrical torque, compliance torque, friction torque,
output torque, transmitted target, elastic deflection, and stuck state.

A bounded `MechanicalIdentificationAccumulator` records sample count, reversal
count, stuck count, absolute-value sums, and maximum elastic deflection. Callers
can copy these records at a lower cadence, and later physical identification can
replace the engineering values without changing the servo-model interface.

## Parameter registry and role isolation

`models/reachy-mini/mechanical-effects-baseline.json` is the authoritative
`rma063_mechanical_effects_v1` registry. It binds the pinned Reachy source and
model, the RMA-041 parameter audit, and the RMA-062 electrical baseline.

| Role | Parameter-set identity |
| --- | --- |
| Body yaw | `body_yaw_xc330_m288_pg_mechanical_estimate` |
| Stewart platform | `stewart_xl330_m288_mechanical_estimate` |
| Antennas | `antenna_xl330_m077_mechanical_estimate` |

All nine official actuators retain ordered role-safe bindings. The deterministic
generator rejects missing evidence, calibrated claims, invalid breakaway or
stiction ordering, unit-contract drift, missing or cross-role bindings, and an
identical complete parameter fingerprint copied between dissimilar roles.

## Validation evidence

Hosted run `30606712074` used Ubuntu 24.04, Python 3.11.15, and GNU C/C++
13.3.0. It passed:

1. byte-exact generated-header verification for
   `rma063_mechanical_effects_v1`;
2. eight Python schema and failure-path tests;
3. explicit Unity-dependency rejection;
4. explicit calibrated-claim rejection;
5. CMake configuration with strict first-party warnings, AddressSanitizer, and
   UndefinedBehaviorSanitizer;
6. compilation of the integrated servo-model library and mechanical test
   executable without warnings;
7. the native behavior suite covering zero/kinetic friction direction,
   stiction entry, breakaway and hysteresis, reversal dead-zone behavior,
   bounded compliance, each independent effect switch, exact all-disabled
   passthrough, role-safe lookup, parameter validation, and identification
   counters/state.

CTest reported `1/1` passing tests and zero failures. The workflow published a
successful `RMA-063 Mechanical Effects Baseline` commit status on exact commit
`a15a1154e62a95999b482ed6b2e6f62f51379929`.

## Known limitations

- No physical friction, breakaway, backlash, or compliance dataset has been
  collected from a Reachy Mini unit.
- Stewart compliance is actuator-joint-space compliance, not a Cartesian
  six-degree-of-freedom head stiffness matrix.
- Antenna cable drag, shell contact, and hard stops remain absent.
- Body-yaw values inherit the RMA-062 XC330-M288-T proxy limitation for the
  custom plastic-geared XC330-M288-PG.
- Shared supply, thermal evolution, and model selection in the production
  MuJoCo path remain later tasks.

## Conclusion

RMA-063 satisfies its implementation and acceptance requirements. Direction
reversal produces the expected backlash dead zone, disabling each effect
restores the prior baseline behavior, and the contract prevents parameters from
being silently copied between dissimilar motor roles. The task may be marked
complete. RMA-064 is the next ordered task.
