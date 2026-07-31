# RMA-063 mechanical effects baseline

## Status and scope

RMA-063 adds a native, Unity-independent mechanical-effects decorator around the
RMA-061 `ServoModel` contract. The decorator can wrap the RMA-062
`ElectricalServoModel` without changing the public simulation C ABI or selecting
it as the active MuJoCo actuator path.

The model covers reduced-order joint-space friction, stiction, backlash, and
torsional compliance. It does not claim calibrated physical fidelity. Collision,
hard stops, shared-supply behavior, and thermal dynamics remain owned by later
tasks.

## Composition boundary

`MechanicalServoModel` owns no motor controller. It receives another
`ServoModel` by reference, applies backlash to position commands before invoking
the wrapped model, and applies compliance plus passive friction/stiction to the
wrapped model's requested torque afterward.

This ordering is intentional:

1. target backlash represents lost motion in the command-to-output path;
2. the wrapped electrical controller computes drive torque and current;
3. compliance transmits a bounded, stateful torque;
4. stiction or kinetic friction modifies the torque delivered to the joint;
5. current and controller fault reporting remain those of the wrapped model.

With all four effects disabled, the input command and returned
`ServoStepResult` are preserved exactly.

## Friction model

Kinetic friction is the sum of a Coulomb term and a viscous term. Its sign
opposes measured joint velocity. Inside the low-speed stiction entry band, the
model uses drive-torque direction and limits the friction magnitude so it cannot
reverse the requested direction merely because measured velocity is quantized to
zero.

The committed coefficients are role-specific engineering estimates scaled from
the RMA-062 motor-role stall torque and no-load speed. They are not manufacturer
friction specifications and are not copied as one identical parameter vector
across body, Stewart, and antenna roles.

## Stiction and breakaway

Stiction is a state machine with separate entry and exit speed thresholds:

- enter the stuck state only when speed is at or below the entry threshold and
  transmitted torque is at or below breakaway torque;
- while stuck, cancel transmitted torque so net joint torque is zero;
- leave the stuck state only when torque exceeds breakaway or speed reaches the
  higher exit threshold.

The threshold hysteresis prevents repeated stuck/free transitions caused by
small velocity sign changes around zero.

## Backlash and direction reversal

Position targets pass through a one-dimensional play operator. The operator
retains the last transmitted target while the input remains inside a symmetric
half-width band. After a direction reversal, output does not move until the
input crosses the opposite edge of that band.

Backlash is applied only to position targets in this baseline. Velocity and
direct-torque modes pass through unchanged. Gear impact, tooth elasticity, and
hard-stop collision are not part of this model.

## Compliance

The reduced-order compliance state is a bounded first-order torsional torque
transmission model. Stiffness and maximum elastic deflection define the maximum
transmitted torque. Damping divided by stiffness defines the transmission time
constant.

This is deliberately simpler than a two-inertia motor/gear/load model because
the current simulation contract does not expose motor-side position or rotor
inertia. The state still provides deterministic torque lag, bounded elastic
deflection, and a parameter surface suitable for later identification.

## Independent effect switches

`MechanicalEffectConfiguration` exposes independent runtime switches for:

- Coulomb and viscous friction;
- stiction and breakaway;
- backlash;
- compliance.

Changing the configuration clears transient stiction/compliance/reversal state
and anchors backlash to the last observed joint position. This prevents stale
state from contaminating A/B experiments.

## Identification hooks

The physics thread does not invoke an external callback. Instead, every step
updates:

- one `MechanicalIdentificationSample` containing measured state, electrical
  torque, compliance torque, friction torque, output torque, transmitted target,
  elastic deflection, and stuck state;
- one bounded `MechanicalIdentificationAccumulator` containing sample count,
  reversal count, stuck count, absolute-value sums, and maximum deflection.

Callers can copy these structures at their own cadence. Future calibration work
can replace engineering estimates without changing the high-frequency model
interface.

## Parameter provenance

`models/reachy-mini/mechanical-effects-baseline.json` is the authoritative
`rma063_mechanical_effects_v1` contract. It binds the pinned Reachy model, the
RMA-041 parameter audit, and the validated RMA-062 electrical contract.

Three explicit role sets exist:

| Role | Parameter set |
| --- | --- |
| Body yaw | `body_yaw_xc330_m288_pg_mechanical_estimate` |
| Stewart platform | `stewart_xl330_m288_mechanical_estimate` |
| Antennas | `antenna_xl330_m077_mechanical_estimate` |

The generator rejects missing evidence, calibrated claims, invalid breakaway or
stiction ordering, cross-role bindings, unit drift, and an identical complete
parameter fingerprint copied between different roles.

## Known limitations

- No physical robot friction, breakaway, backlash, or compliance dataset has
  been collected yet.
- Stewart compliance is represented in actuator joint space, not as a Cartesian
  six-degree-of-freedom head stiffness matrix.
- Antenna cable drag, shell contact, and hard stops are absent.
- Body values use the same RMA-062 XC330-M288-T performance proxy limitations as
  the custom XC330-M288-PG electrical baseline.
- The model is not selected in the production MuJoCo actuator path by RMA-063.
