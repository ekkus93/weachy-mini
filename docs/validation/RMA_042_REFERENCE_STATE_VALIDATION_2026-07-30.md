# RMA-042 desktop/Android reference-state validation

**Date:** 2026-07-30  
**Scenario:** `rma042_representative_motion_v1`  
**Hardened implementation series:** `f04adda31ad50842415bb3b8bc9b18b645cddbfc` through `da6fb1fd3e13afe2b2269ee2dd85ba0a0f2826de`

## Scope

This record covers RMA-042 only: generation of a pinned desktop reference trace,
execution of the same scenario through the Android ARM64 MuJoCo build, comparison
of native state and body transforms, locked tolerances, compact fixture integrity,
loop-closure residual bounds, and coordinate-convention tests. RMA-041 and
RMA-050 remain unchanged.

## Identity contract

The comparison is bound to all of the following:

- Reachy source commit
  `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- Reachy MJCF SHA-256
  `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`;
- MuJoCo 3.9.0 and source commit
  `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`;
- exact scenario bytes and SHA-256
  `534b92d7aa09247dffb4f101be6d000764bb28d46b533fafa0ed30490c74b776`;
- exact platform labels `desktop_reference` and `android_arm64_api26`;
- compiled counts: 19 bodies including world, 16 joints, 9 actuators,
  5 equalities, 13 sites, 2 cameras, `nq=37`, and `nv=30`;
- exact ordered actuator, body, phase, and checkpoint lists.

## Scenario and compared state

The 900-step scenario settles neutral state, applies two representative bounded
command vectors to body yaw, all six Stewart actuators, and both antennas, then
returns to neutral. Ten checkpoints include the initial state, phase boundaries,
representative motion, and final state.

At every checkpoint both platforms provide:

- exact step and simulation time;
- all 37 `qpos` values;
- all 30 `qvel` values;
- MuJoCo-world position and `wxyz` quaternion for all 17 named non-world bodies;
- maximum absolute residual across every `mjCNSTR_EQUALITY` row;
- cumulative MuJoCo warning count.

The comparator requires each platform time to match `step * 0.002`, rather than
accepting two traces that share the same incorrect clock.

## Coordinate contract

RMA-042 compares MuJoCo-native data only:

- world-frame positions in metres;
- native MuJoCo `qpos`/`qvel` ordering and units;
- body quaternions in `w, x, y, z` order;
- finite normalized quaternions, with `q` and `-q` treated as equivalent;
- equality rows only for loop-closure health;
- no Unity axis, handedness, scale, or quaternion conversion.

The Unity conversion contract remains owned by RMA-050 through RMA-052.

## Tolerance contract

| Quantity | Absolute policy |
|---|---:|
| Simulation time | `1e-12` seconds |
| `qpos` | `2e-6` |
| `qvel` | `2e-5` |
| Body position | `2e-6` metres |
| Quaternion component | `2e-6` |
| Quaternion norm | `1e-6` |
| Equality residual difference | `1e-6` |
| Maximum observed equality residual | `0.001` |

These are cross-platform numerical-agreement policies, not physical calibration
tolerances. The active upstream actuator model remains explicitly uncalibrated.

## Failure-path coverage

The hardened tests reject:

- stale generated native scenario headers;
- malformed scenario phases, targets, checkpoints, counts, and tolerances;
- non-hexadecimal compact-lock digests;
- a desktop trace whose exact bytes differ from its compact lock;
- platform, model, scenario, MuJoCo, or compiled-count identity drift;
- matching desktop/Android timestamps that disagree with the scenario clock;
- missing, malformed, boolean-as-number, or non-finite state values;
- warnings, negative residuals, and excessive loop-closure residuals;
- over-tolerance `qpos`, `qvel`, position, quaternion, or residual differences;
- body count, name, or ordering changes;
- non-unit quaternions.

A separate positive test proves quaternion sign equivalence.

## Physical Android result

Android MuJoCo Feasibility run `30583271127` passed on commit
`d229235d73851f58088b7c142e469ef6cfaeaefb`. The hosted job regenerated and
byte-validated the desktop trace, cross-compiled the pinned MuJoCo runtime and
reference runner for AArch64, verified architecture/provenance, and uploaded the
exact artifact. The `kawa` device job then executed the scenario on an LGE LG-H872
(`lucye`, Android 8.0.0, API 26, `arm64-v8a`) and uploaded the comparison evidence.

Measured maxima were:

| Quantity | Measured maximum |
|---|---:|
| Cross-platform simulation time | `0.0` seconds |
| Time versus scenario schedule | `1.3322676295501878e-15` seconds |
| `qpos` | `3.784299262843405e-15` |
| `qvel` | `2.6852825518730583e-13` |
| Body position | `5.898059818321144e-16` metres |
| Quaternion component | `3.219646771412954e-15` |
| Quaternion norm error | `9.992007221626409e-16` |
| Equality residual difference | `1.1102230246251565e-16` |
| Maximum observed equality residual | `3.84603861668803e-06` |

No MuJoCo warning occurred.

Evidence identities:

- desktop trace SHA-256:
  `400dbe651653820f722b11de347b055a8bcf19f8904a10dc03832139531d90b7`;
- Android trace SHA-256:
  `9470aff1cfe0a02027d1d9dae1694b8dcc964dd8f80db786b5b6f93d30cd3598`;
- physical report artifact digest:
  `b19022aabc455236b61aa4eeda779e93ed8a4415984534704b179cac9df9c7df`.

## Hosted quality result

Hosted Quality Gates run `30583907077` passed on commit
`da6fb1fd3e13afe2b2269ee2dd85ba0a0f2826de`, including Ruff lint/format,
actionlint, ShellCheck, repository policy, native warnings/sanitizers, managed
warnings-as-errors and native lifecycle tests, Android lint/tests, official-model
compile/step, and desktop reference generation.

## Result

RMA-042 has permanent deterministic fixtures, strict identity and failure
semantics, documented coordinate conventions and floating-point tolerances, and a
successful physical Android comparison against the pinned desktop trace. It does
not claim physical calibration or close RMA-041.
