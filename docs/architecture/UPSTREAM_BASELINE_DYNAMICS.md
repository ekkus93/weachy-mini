# Upstream baseline dynamics

## Scope

RMA-060 defines the first named dynamics mode for the Reachy Mini digital twin:
`upstream_baseline`. It is a source-faithful comparison baseline, not a claim of
mechanical or servo fidelity. The mode uses the official pinned Reachy Mini MJCF,
MuJoCo 3.9.0, and the active upstream generic position-actuator defaults audited
by RMA-041.

The authoritative machine-readable contract is
`models/reachy-mini/upstream-baseline-stability.json`. The native Android runner
uses only the generated
`native/reachy_sim/feasibility/reachy_stability_profile.generated.h`; the build
fails if that header is not the exact rendering of the JSON profile bytes.

## Fixed timing contract

The baseline timestep is exactly 0.002 seconds, or 500 Hz. Model loading fails if
the compiled MJCF uses another timestep. The Android acceptance schedule contains
45 cycles. Each cycle contains 20 phases, and each phase contains 500
minimum-jerk transition steps followed by 500 hold steps. The complete gate is
therefore:

- 900,000 solver steps;
- 1,800 simulated seconds;
- 30 simulated minutes;
- no timestep deviation;
- an average solver real-time factor of at least 1.0 on the representative phone.

A future timestep change requires a new named profile and a measured gate
decision. It must not silently alter `upstream_baseline`.

## Motion coverage

The ordered schedule begins and ends in neutral and covers:

- the upstream sleep command;
- both body-yaw limits;
- positive and negative limits for all six Stewart actuators;
- both antenna extremes and mirrored antenna extremes.

Body-yaw and Stewart boundary commands use the profile-declared 1e-9 radian
inward inset. This keeps the commands inside MuJoCo's compiled actuator ranges
when decimal-to-binary conversion differs by an ulp, while remaining effectively
at the audited upstream limits. The inset is not an allowed overrange exception.

The upstream sleep command requests four Stewart targets outside their encoded
actuator control ranges. This is retained as explicit source evidence, not hidden
or normalized. The profile names the four affected actuators, the generated
header stores the corresponding bit mask, and both desktop and Android runners
reject any mismatch between the declaration and the compiled model.

## Monitoring

Every Android solver step checks all available authoritative numeric state needed
by this gate:

- `qpos`, `qvel`, and `qacc`;
- actuator commands and actuator forces;
- body positions and quaternions;
- active equality residuals;
- scalar hinge/slide joint-limit excursions;
- active contact count and maximum penetration;
- potential plus kinetic energy;
- MuJoCo warning counters.

The run fails on the first non-finite value, MuJoCo warning, or threshold breach.
The locked thresholds are:

| Monitor | Maximum |
| --- | ---: |
| Equality residual | 0.001 |
| Scalar joint-limit violation | 0.000001 rad |
| Contact penetration | 0.01 m |
| Absolute total energy | 100 J |

The runner also records per-step timing, median and p95 step duration, maximum
step duration, 2 ms solver-deadline misses, wall execution time, and solver
real-time factor. Timing data is evidence; it never changes the simulation
clock or skips solver steps.

## Desktop and Android roles

`scripts/run_reachy_upstream_baseline_stability.py` is the desktop reference
implementation. It validates the profile, source identity, topology, actuator
order, command-range declarations, schedule, monitoring, and deterministic
report structure through the pinned Python MuJoCo package.

`reachy_mujoco_stability_runner` is the production-native Android evidence path.
It is cross-compiled with the same pinned NDK and MuJoCo source used by the
production simulation library. `scripts/run_reachy_stability_android.sh` selects
one physical ARM64 Android device, records device and thermal metadata, verifies
a structured invalid-cycle failure, executes the complete gate, and validates
the report against the exact staged profile bytes.

The GitHub Actions workflow `.github/workflows/rma060-baseline-stability.yml`
runs the full desktop schedule, builds the ARM64 runner, and then executes the
long-duration gate on the labeled physical-device runner. Successful reports are
uploaded as retained workflow artifacts.

## Fidelity statement

`upstream_baseline` remains uncalibrated. Its active actuator class is a
placeholder inherited from the upstream model. Passing RMA-060 establishes a
stable and measurable baseline for later servo-model work; it does not justify
calling the virtual robot mechanically exact, manufacturer-validated, measured,
or calibrated.
