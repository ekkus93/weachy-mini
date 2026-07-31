# Electrical and controller timing baseline

## Scope

RMA-062 supplies the first concrete `ServoModel` implementation below the Unity
presentation layer. `ElectricalServoModel` is a deterministic native C++17 model
for command timing, position/velocity quantization, bounded position control,
voltage-sensitive torque-speed saturation, current limiting, torque disable, and
fault transitions.

This is an explicitly **noncalibrated manufacturer/engineering baseline**. It is
not a claim that the digital twin reproduces an individual Reachy Mini. Friction,
backlash, compliance, shared-supply sag, thermal dynamics, and physical parameter
identification remain assigned to RMA-063 through RMA-065.

## Evidence boundary

The machine-readable contract is
`models/reachy-mini/electrical-controller-baseline.json`. It is bound to the
pinned Reachy Mini source commit and records hashes for the upstream hardware
page and hardware configuration.

The pinned hardware page identifies:

- body yaw: custom XC330-M288-PG, described as an XC330-M288-T with plastic gear;
- Stewart platform: six XL330-M288-T motors;
- antennas: two XL330-M077-T motors.

The pinned hardware configuration records 1 Mbaud TTL communication, zero
Dynamixel return delay, position operating mode, raw proportional gains, and the
configured shutdown bit mask. The pinned SDK's move playback default is 100 Hz.
ROBOTIS e-manual values provide 12-bit encoder resolution, velocity units,
3.7-6.0 V motor input ranges, 6 V stall torque/current and no-load speed, and a
70 C temperature limit.

The Reachy hardware page separately states a 6.8-7.6 V robot input. It does not
identify the regulated Dynamixel rail. The baseline therefore does **not** treat
robot input voltage as servo-bus voltage. Servo observations use the motor
manuals' 3.7-6.0 V domain until the internal rail is measured or documented.

## Role-specific baselines

The three sets remain distinct:

| Role | Baseline | Hardware evidence |
| --- | --- | --- |
| Body yaw | `body_yaw_xc330_m288_pg_estimate` | Custom XC330-M288-PG; XC330-M288-T performance proxy |
| Stewart | `stewart_xl330_m288_estimate` | XL330-M288-T |
| Antenna | `antenna_xl330_m077_estimate` | XL330-M077-T |

The body proxy is intentionally called out: plastic gearing can change strength,
wear, and efficiency, so XC330-M288-T performance numbers are not calibrated
values for the custom PG motor.

## Timing model

Commands have a source sample time and monotonic sequence. New sequences enter a
fixed-capacity queue. Equal sequences are idempotent; a decreasing sequence or
queue overflow produces a latching communication fault.

The baseline uses:

- native physics step: 0.002 s;
- command sample period: 0.010 s, from the pinned 100 Hz SDK default;
- command latency: one 0.010 s sample, an explicit engineering estimate.

Commands arriving faster than 100 Hz are serialized into consecutive sample
slots. They are activated only when authoritative observation time reaches the
sample slot plus latency. No physics step is skipped or shifted.

## Quantization and profiling

Encoder position and commanded position use `2*pi/4096` radians per pulse.
Encoder velocity and commanded velocity use the documented `0.229 rpm` unit,
converted to radians per second. Quantization is nearest-increment with half
steps away from zero, matching C++ `round` semantics; tests lock both positive
and negative boundaries.

Position commands optionally use bounded profile velocity and acceleration.
Zero profile limits mean immediate target application, not an unlimited hidden
profile. Velocity mode uses the same velocity and acceleration bounds.

## Controller and saturation

Position mode computes a joint-space PD request plus feed-forward torque.
Velocity mode computes damping torque plus feed-forward torque. Torque mode uses
feed-forward torque directly. The initial gains are engineering estimates chosen
so a ten-degree static position error requests the documented 6 V stall torque,
with a 20 ms derivative time constant. They are not the raw Dynamixel PID values
and are not calibrated.

The output is bounded by both:

1. a linear torque-speed envelope from stall torque at zero speed to zero torque
   at no-load speed; and
2. a current-derived torque bound using `stall_torque / stall_current` as the
   initial torque-constant estimate.

Documented performance exists at 6 V. Below 6 V, torque and no-load speed scale
linearly with observed servo voltage. Above 6 V, performance is clamped to the
6 V point rather than extrapolated. Voltage outside the documented 3.7-6.0 V
motor domain disables torque and raises a transient voltage fault.

The continuous-current value is conservatively estimated as half the documented
stall current. Current above that estimate may use the documented stall-current
ceiling for at most 0.25 s. Sustained overload latches `over_current` and disables
torque until reset. The 50 percent ratio and 0.25 s window are explicit
engineering assumptions awaiting physical validation.

## Torque disable and faults

Disabled mode or `torque_enabled=false` always returns zero motor torque and zero
estimated current. Gravity and passive MuJoCo dynamics remain active; torque
disable is not a pose-hold fallback.

The model recognizes all RMA-061 fault bits. Under/over-voltage faults clear when
voltage returns to range. Over-current, over-temperature, encoder,
communication, and model-rejected faults latch. Reset clears internal latches and
then imports any latching fault already present in the authoritative observation.
A non-finite command, observation, or timestep fails closed with
`model_rejected`.

Temperature is currently observed, not dynamically integrated. A reported value
at or above the 70 C limit latches over-temperature. Power-network and thermal
state evolution remain RMA-064 work.

## Unit contract

All runtime values use SI units:

- seconds;
- radians and radians per second;
- newton-metres;
- amperes;
- volts;
- degrees Celsius.

The generator validates the exact unit map and the encoder/rpm conversions before
emitting `reachy_electrical_baseline.generated.hpp`. Unit drift, source drift,
role-crossing, placeholder values, calibrated claims, and stale generated output
are fatal.

## Integration boundary

`ElectricalServoModel` implements the RMA-061 `ServoModel` interface and contains
no Unity type or ownership. RMA-062 establishes the actuator-level behavior and
tests it independently. Connecting the model to MuJoCo actuator callbacks and
selecting fidelity profiles are separate integration tasks; the existing
`upstream_baseline` remains unchanged until that switch is explicit and tested.
