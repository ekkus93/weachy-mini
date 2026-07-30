# Reachy Mini desktop/Android reference-state comparison

**Task:** RMA-042 — Build reference-state comparison tests  
**Scenario:** `rma042_representative_motion_v1`  
**Scenario source:** `models/reachy-mini/reference-scenario.json`  
**Scenario SHA-256:** `534b92d7aa09247dffb4f101be6d000764bb28d46b533fafa0ed30490c74b776`  
**Desktop trace lock:** `models/reachy-mini/reference-trace-desktop.lock.json`  
**Desktop trace SHA-256:** `400dbe651653820f722b11de347b055a8bcf19f8904a10dc03832139531d90b7`

## Result

The pinned official Reachy Mini model produced matching desktop x86-64 and Android
ARM64 state traces within every documented tolerance. This is a deterministic
cross-platform model-integrity result, not a physical calibration claim.

The current physical comparison was produced by Android MuJoCo Feasibility run
`30583271127` on the `kawa` runner using commit
`d229235d73851f58088b7c142e469ef6cfaeaefb`.

The Android trace was recorded on:

- manufacturer: LGE;
- model: LG-H872;
- device codename: `lucye`;
- Android release: 8.0.0;
- API level: 26;
- ABI: `arm64-v8a`.

## Pinned simulation identity

| Item | Value |
|---|---|
| Reachy source commit | `a739a6e461eb6d722901f1cfc225265ffc85c28d` |
| Reachy MJCF SHA-256 | `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46` |
| MuJoCo version | `3.9.0` |
| MuJoCo source commit | `237c17e48539b6c90bf90d3161547cbdcbfaa1e0` |
| Timestep | `0.002` seconds |
| Total scenario steps | `900` |
| Checkpoints | `0, 1, 50, 100, 150, 350, 400, 600, 700, 900` |

Both traces reported the same compiled model dimensions:

- 19 bodies including world;
- 16 joints;
- 9 actuators;
- 5 equality constraints;
- 13 sites;
- 2 cameras;
- `nq=37`;
- `nv=30`.

## Command scenario

The scenario applies one ordered target vector to these actuators:

1. `yaw_body`;
2. `stewart_1` through `stewart_6`;
3. `right_antenna`;
4. `left_antenna`.

The phases are:

| Start step | Phase | Purpose |
|---:|---|---|
| 0 | `neutral_settle` | Reset/neutral baseline and initial settling. |
| 100 | `pose_a` | Positive body/Stewart/antenna representative command. |
| 350 | `pose_b` | Opposite-sign representative command. |
| 600 | `return_neutral` | Return all active targets to neutral. |

The exact target vectors are authoritative in `reference-scenario.json`; this
document intentionally does not duplicate them as a second source of truth.

## Compared state

At every checkpoint, the comparator validates:

- platform identity;
- scenario ID and exact scenario SHA-256;
- model SHA-256;
- MuJoCo version;
- compiled dimensions;
- checkpoint count, order, and exact step numbers;
- simulation time against both the other platform and `step * 0.002`;
- all 37 `qpos` values;
- all 30 `qvel` values;
- position and quaternion for all 17 named non-world bodies;
- body order and identity;
- quaternion normalization and `q`/`-q` equivalence;
- maximum equality-constraint residual;
- MuJoCo warning count.

The native Android scenario header is generated from the JSON scenario. CI rejects
a stale generated header. The desktop trace is regenerated through Python MuJoCo
and must match the compact trace lock byte-for-byte before it is packaged for the
Android comparison.

## Coordinate conventions

The comparison uses MuJoCo-native state and transform conventions:

- positions are metres in the MuJoCo world frame;
- joint positions and velocities retain MuJoCo ordering and units;
- body quaternions use MuJoCo `w, x, y, z` ordering;
- quaternions must be finite and normalized;
- quaternion `q` and `-q` are treated as equivalent rotations;
- no Unity coordinate conversion is applied in this model-integrity gate;
- equality residuals include only MuJoCo rows whose constraint type is
  `mjCNSTR_EQUALITY`; contact residuals are not misclassified as loop-closure
  error.

Every equality row is evaluated by each platform runner. The trace stores the
maximum absolute equality-row residual at each checkpoint, so the comparator both
checks the cross-platform difference and enforces an absolute bounded-residual
policy.

Unity/MuJoCo coordinate conversion remains a separate rendering-layer
responsibility under RMA-050 through RMA-052.

## Tolerances and measured errors

These tolerances measure deterministic numerical agreement across desktop x86-64
and Android ARM64 builds. They intentionally include margin for compiler,
architecture, floating-point instruction selection, and standard-library
differences while remaining much tighter than any physical-model accuracy claim.

| Quantity | Allowed absolute error | Measured maximum error |
|---|---:|---:|
| Simulation time, platform-to-platform | `1e-12` s | `0.0` s |
| Simulation time versus scenario schedule | `1e-12` s | `1.3322676295501878e-15` s |
| `qpos` | `2e-6` | `3.784299262843405e-15` |
| `qvel` | `2e-5` | `2.6852825518730583e-13` |
| Body position | `2e-6` m | `5.898059818321144e-16` m |
| Quaternion component | `2e-6` | `3.219646771412954e-15` |
| Quaternion norm error | `1e-6` | `9.992007221626409e-16` |
| Equality residual difference | `1e-6` | `1.1102230246251565e-16` |

The largest equality residual observed in either trace was
`3.84603861668803e-06`. The absolute bounded-residual policy is `0.001`.
No MuJoCo warning was present at any checkpoint.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Desktop trace | `400dbe651653820f722b11de347b055a8bcf19f8904a10dc03832139531d90b7` |
| Android trace | `9470aff1cfe0a02027d1d9dae1694b8dcc964dd8f80db786b5b6f93d30cd3598` |
| Physical report artifact | `b19022aabc455236b61aa4eeda779e93ed8a4415984534704b179cac9df9c7df` |

The comparison report records trace hashes, coordinate convention, tolerances,
and measured maxima. Full generated traces remain CI evidence rather than
hand-maintained source fixtures.

## Failure behavior

The RMA-042 tooling fails visibly when any of the following occurs:

- the generated C header differs from the JSON scenario;
- a compact lock value is not a lowercase hexadecimal SHA-256;
- desktop trace bytes differ from the compact lock;
- model, scenario, runtime, platform, or compiled-count identity differs;
- phases, targets, checkpoints, names, counts, or tolerances are malformed;
- checkpoint count, order, or step number differs;
- either platform time differs from the scenario schedule;
- any state value is missing, malformed, boolean-as-number, or non-finite;
- a MuJoCo warning appears;
- an equality residual is negative, non-finite, or exceeds the absolute bound;
- any cross-platform state error exceeds its field tolerance;
- a body name/order differs;
- a quaternion is not normalized;
- the Android trace runner cannot resolve every required actuator or body.

Unit tests explicitly prove over-tolerance state rejection, matching-but-wrong
clock rejection, platform rejection, non-finite rejection, loop-closure bound
enforcement, transform-order enforcement, quaternion normalization, quaternion
sign equivalence, strict warning typing, and strict fixture hashes.

## Fidelity limitation

This comparison proves deterministic cross-platform execution of the pinned
upstream model and command schedule. The active actuator model remains the
uncalibrated upstream `chosen_actuator` placeholder documented in
`docs/model-parameter-audit.md`. Agreement between desktop and Android does not
convert placeholder dynamics into measured or calibrated robot behavior.
