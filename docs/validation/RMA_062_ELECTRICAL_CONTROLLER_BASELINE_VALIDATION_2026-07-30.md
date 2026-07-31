# RMA-062 electrical/controller baseline validation

**Status:** Complete  
**Validated commit:** `699c7b0adcc56263b307b76cc24b4f642dbe5f04`  
**Workflow:** `RMA-062 Electrical Controller Baseline`  
**Successful run:** `30605184722`  
**Commit status:** `RMA-062 Electrical Controller Baseline` — success

## Acceptance result

RMA-062 is accepted. The repository now contains the first concrete native
`ServoModel` implementation for sampled-command timing, configurable latency,
encoder and command quantization, bounded position/velocity/torque control,
voltage-sensitive torque-speed saturation, current limiting, torque disable,
and explicit fault transitions.

The implementation is intentionally a noncalibrated manufacturer/engineering
baseline. It does not claim unit-to-unit physical fidelity and does not replace
the named RMA-060 `upstream_baseline` until a later task explicitly connects the
model to MuJoCo actuator callbacks and selects a fidelity profile.

## Source and evidence identity

The machine-readable contract
`models/reachy-mini/electrical-controller-baseline.json` is identified as
`rma062_electrical_controller_v1` and binds:

- pinned Reachy Mini source commit
  `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- the pinned hardware-page blob and hardware-configuration blob;
- the RMA-061 servo contract and RMA-041 parameter audit;
- the ROBOTIS XC330-M288, XL330-M288, and XL330-M077 e-manual evidence used by
  the role-specific baselines.

The upstream hardware mapping is preserved:

| Actuator role | Hardware mapping | Baseline identity |
| --- | --- | --- |
| Body yaw | custom XC330-M288-PG, using XC330-M288-T performance as a proxy | `body_yaw_xc330_m288_pg_estimate` |
| Stewart platform | six XL330-M288-T motors | `stewart_xl330_m288_estimate` |
| Antennas | two XL330-M077-T motors | `antenna_xl330_m077_estimate` |

The custom plastic-geared body motor remains an explicit proxy rather than a
manufacturer-equivalent or calibrated claim.

## Electrical and timing contract

`ElectricalServoModel` implements the RMA-061 C++17 `ServoModel` interface and
contains no Unity ownership. Its contract includes:

- a 0.002 s native simulation step;
- a 0.010 s command sample period derived from the pinned SDK's 100 Hz motion
  playback default;
- a configurable baseline latency of 0.010 s, explicitly labeled an engineering
  estimate;
- a fixed-capacity 16-command pending queue;
- monotonic command sequences, idempotent equal sequences, and latching
  communication faults for sequence regression or queue overflow;
- activation only when authoritative observation time reaches the serialized
  sample slot plus configured latency.

No simulation step is skipped or shifted to model command timing.

## Quantization and unit consistency

Position encoding and targets use the documented 4096-pulse revolution:

`2*pi/4096 = 0.0015339807878856412 rad`.

Velocity encoding and targets use the documented 0.229 rpm unit:

`0.229*2*pi/60 = 0.023980823922402087 rad/s`.

Quantization uses nearest-increment `std::round` semantics, including half-step
rounding away from zero for both signs. Optional velocity and acceleration
profiles are bounded in SI units. Zero profile limits mean immediate target
application rather than an unreported unlimited profile.

The generator locks the complete runtime unit map to seconds, radians,
radians/second, radians/second-squared, newton-metres, amperes, volts, and degrees
Celsius. Unit drift, conversion drift, source drift, cross-role bindings,
placeholder values, calibrated claims, and stale generated output fail visibly.

## Controller and saturation behavior

Position mode uses a bounded joint-space PD request plus feed-forward torque.
Velocity mode uses bounded velocity damping plus feed-forward torque. Torque mode
uses direct feed-forward torque. The gains are engineering estimates selected so
a ten-degree static error reaches the documented 6 V stall-torque point with a
20 ms derivative time constant; they are not raw Dynamixel PID-register values.

Output torque is bounded by both:

1. a linear torque-speed envelope between the documented 6 V stall-torque and
   no-load-speed points; and
2. a current-derived torque limit using `stall_torque/stall_current` as the
   initial torque-constant estimate.

The three role baselines use documented 6 V performance points:

| Role | Stall torque | Stall current | No-load speed |
| --- | ---: | ---: | ---: |
| Body yaw proxy | 1.10 N m | 2.15 A | 10.157816246606997 rad/s |
| Stewart | 0.60 N m | 1.74 A | 12.880529879718152 rad/s |
| Antenna | 0.228 N m | 1.74 A | 47.752208334564855 rad/s |

The documented servo domain is 3.7-6.0 V with 5.0 V nominal. Torque and no-load
speed scale linearly below 6 V and clamp at the 6 V point above it; the model does
not extrapolate undocumented performance. Voltage outside 3.7-6.0 V disables
motor torque and raises a transient voltage fault.

The robot-level 6.8-7.6 V input specification is not treated as the internal
Dynamixel bus because the pinned source does not identify that regulated rail.
The actual internal rail remains an explicit measurement/documentation gap.

## Current limiting, torque disable, and faults

Continuous current is conservatively estimated as 50 percent of documented stall
current. The documented stall-current ceiling is available for at most 0.25 s;
sustained overload latches `over_current` and returns zero torque until reset.
Both the 50 percent ratio and 0.25 s duration remain explicit engineering
assumptions.

Disabled mode or `torque_enabled=false` returns zero torque and zero estimated
current. Passive MuJoCo dynamics, including gravity, remain active, so torque
disable is not a hidden pose-hold fallback.

Under-voltage and over-voltage faults clear when voltage returns to range.
Over-current, over-temperature, encoder, communication, and model-rejected faults
latch. Non-finite command, observation, or timestep data fail closed as
`model_rejected`. Temperature is observed rather than dynamically integrated;
a reported value at or above the documented 70 C limit latches over-temperature.
Shared-supply and thermal-state evolution remain RMA-064 work.

## Validation evidence

Hosted run `30605184722` used Ubuntu 24.04, Python 3.11.15, and GNU C/C++ 13.3.0.
It passed:

1. exact generated-header verification for
   `rma062_electrical_controller_v1`;
2. eight Python schema and failure-path tests covering exact regeneration,
   pinned-source drift, calibrated claims, placeholder/null values, SI-unit
   drift, encoder conversion drift, cross-role binding, and stale output;
3. explicit rejection of Unity ownership and calibrated claims;
4. CMake configuration with `BUILD_TESTING=ON`, strict first-party warnings,
   AddressSanitizer, and UndefinedBehaviorSanitizer;
5. compilation of `libreachy_servo_model.a` with the electrical implementation
   and `reachy_electrical_servo_model_test` without warnings;
6. the complete native behavior suite covering registry/unit identity,
   quantization boundaries, delayed command application, zero-error torque,
   positive/negative saturation, voltage scaling, torque-disable gravity
   response, transient and latching fault transitions, current-duration cutoff,
   command sampling, and profile bounds.

CTest reported `1/1` passing tests and zero failures. The workflow published a
successful `RMA-062 Electrical Controller Baseline` commit status on exact commit
`699c7b0adcc56263b307b76cc24b4f642dbe5f04`.

## Defects found during integration

Two integration defects were exposed rather than suppressed:

- run `30604944996` found that the Python test fixture used local-development
  paths instead of repository paths; the fixture was corrected to
  `models/reachy-mini/...` and `native/reachy_sim/src/...`;
- run `30605032736` passed the complete source and behavior validation but the
  GitHub Actions token was correctly refused permission to create a new workflow
  file. Source integration and permanent workflow publication were separated;
  integration run `30605137219` then passed, and the connected GitHub API
  published the focused workflow in commit `699c7b0...`.

Neither issue changed the fidelity model or weakened a gate.

## Deferred scope

RMA-062 does not add friction, stiction, backlash, hysteresis, compliance,
shared-supply voltage sag, thermal state integration, or physical parameter
identification. Those remain assigned to RMA-063 through RMA-065. It also does
not silently replace RMA-060 production dynamics; selecting this model in the
MuJoCo actuator path requires a separate explicit integration and acceptance
task.

## Conclusion

RMA-062 now provides a source-bound, unit-checked, replaceable electrical and
controller timing baseline without fabricating calibration evidence. All listed
requirements and tests are implemented and validated. RMA-062 may be marked
complete; RMA-063 is the next ordered actuator-fidelity task.
