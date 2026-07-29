# Reachy Mini desktop/Android reference-state comparison

**Task:** RMA-042 — Build reference-state comparison tests  
**Scenario:** `rma042_representative_motion_v1`  
**Scenario source:** `models/reachy-mini/reference-scenario.json`  
**Scenario SHA-256:** `534b92d7aa09247dffb4f101be6d000764bb28d46b533fafa0ed30490c74b776`  
**Desktop trace lock:** `models/reachy-mini/reference-trace-desktop.lock.json`  
**Desktop trace SHA-256:** `400dbe651653820f722b11de347b055a8bcf19f8904a10dc03832139531d90b7`

## Result

The pinned official Reachy Mini model produced matching desktop and Android ARM64 state traces within all documented tolerances.

The Android trace was recorded on:

- manufacturer: LGE;
- model: LG-H872;
- device codename: `lucye`;
- Android release: 8.0.0;
- API level: 26;
- ABI: `arm64-v8a`;
- serial used by the private CI runner: `LGH87250967ab9`.

The successful comparison was produced by Android MuJoCo feasibility run `30498212689`, device job `90732163460`, using commit `b06c045a04ed9b1049565db84d0bd472da979031`.

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

The exact target vectors are authoritative in `reference-scenario.json`; this document intentionally does not duplicate them as a second source of truth.

## Compared state

At every checkpoint, the comparator validates:

- simulation time;
- all 37 `qpos` values;
- all 30 `qvel` values;
- position and quaternion for all 17 named non-world bodies;
- maximum equality-constraint residual;
- MuJoCo warning count;
- scenario ID and SHA-256;
- model SHA-256;
- MuJoCo version;
- compiled dimensions.

The native Android scenario header is generated from the JSON scenario. CI rejects a stale generated header. The desktop trace is regenerated through Python MuJoCo and must match the compact trace lock before it is packaged for Android comparison.

## Coordinate conventions

The comparison uses MuJoCo-native state and transform conventions:

- positions are metres in the MuJoCo world frame;
- joint positions and velocities retain MuJoCo ordering and units;
- body quaternions use MuJoCo `w, x, y, z` ordering;
- quaternion `q` and `-q` are treated as equivalent rotations;
- no Unity coordinate conversion is applied in this model-integrity gate;
- equality residuals include only MuJoCo rows whose constraint type is `mjCNSTR_EQUALITY`; contact residuals are not misclassified as loop-closure error.

Unity/MuJoCo coordinate conversion remains a separate rendering-layer responsibility under RMA-050 through RMA-052.

## Tolerances and measured errors

These tolerances measure deterministic numerical agreement across the desktop x86-64 and Android ARM64 builds. They are not claims of physical calibration accuracy.

| Quantity | Allowed absolute error | Measured maximum error |
|---|---:|---:|
| Simulation time | `1e-12` s | `0.0` s |
| `qpos` | `2e-6` | `3.7816971776294395e-15` |
| `qvel` | `2e-5` | `2.6889601656421294e-13` |
| Body position | `2e-6` m | `5.898059818321144e-16` m |
| Quaternion component | `2e-6` | `3.219646771412954e-15` |
| Equality residual difference | `1e-6` | `1.1102230246251565e-16` |

The largest equality residual observed in either trace was:

```text
3.850418586758942e-06
```

The bounded-residual policy allows at most:

```text
0.001
```

No MuJoCo warning was present at any checkpoint.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Desktop trace | `400dbe651653820f722b11de347b055a8bcf19f8904a10dc03832139531d90b7` |
| Android trace | `4692229d2f90978bff258110e6e6eb8ff07a074d813f81db34e8322471b0793c` |
| Comparison report | available in the run `30498212689` physical-device report artifact |

The comparison report records the trace hashes, tolerances, and measured maximum errors. The full traces remain generated CI evidence rather than hand-maintained source files.

## Failure behavior

The RMA-042 tooling fails visibly when any of the following occurs:

- generated C header differs from the JSON scenario;
- model, scenario, runtime, or compiled-count identity differs;
- checkpoint count or order differs;
- any state value is missing, malformed, or non-finite;
- a MuJoCo warning appears;
- equality residual exceeds the bounded-residual policy;
- any state error exceeds its per-field tolerance;
- a body name/order differs;
- desktop trace bytes differ from the compact lock;
- Android trace runner cannot resolve every required actuator or body.

Unit tests also prove that an over-tolerance `qpos` difference fails and that quaternion sign equivalence is accepted.

## Fidelity limitation

This comparison proves deterministic cross-platform execution of the pinned upstream model and command schedule. The active actuator model remains the uncalibrated upstream `chosen_actuator` placeholder documented in `docs/model-parameter-audit.md`. Agreement between desktop and Android does not convert placeholder dynamics into measured or calibrated robot behavior.
