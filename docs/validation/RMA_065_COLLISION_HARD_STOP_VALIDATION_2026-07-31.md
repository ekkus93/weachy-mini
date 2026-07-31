# RMA-065 Collision and Hard-Stop Validation

**Date:** 2026-07-31  
**Repository:** `ekkus93/weachy-mini`  
**Accepted implementation commit:** `08bf637a12dbe77591d3827412a752d3d4e28fba`  
**Permanent workflow run:** `30654822714`  
**Decision:** Accepted

## Scope and fidelity boundary

RMA-065 adds an explicit collision and mechanical hard-stop baseline to the
pinned Reachy Mini MuJoCo model. The collision primitives, contact thresholds,
and antenna hard-stop ranges remain labeled `engineering_estimate`; this
validation does not relabel them as measured or calibrated physical parameters.

The immutable source inputs were:

- Reachy Mini commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- source MJCF SHA-256
  `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`;
- MuJoCo 3.9.0 commit
  `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`;
- generated enhanced MJCF SHA-256
  `743ef3763d04b2d07e179b5b33ac78ffb47b81aef5de9c3c5762ed31a559aa4a`.

## Permanent hosted gate

The permanent workflow rebuilt from the pinned sources and passed:

- 19 generator, fixture, schema, and runtime regression tests;
- 6 fail-closed report-verifier tests;
- deterministic model generation and stale-output detection;
- strict C/C++ compilation;
- ASan/UBSan-backed fake-MuJoCo backend and ABI tests;
- the 5,000-step enhanced neutral audit;
- controlled internal and external contact fixtures;
- yaw and right-antenna hard-stop trials;
- real-MuJoCo state-format-v2 contact and hard-stop telemetry;
- Android API-26 ARM64 cross-compilation and AArch64 ELF verification.

The enhanced neutral audit compiled 186 geoms, including 25 active collision
geoms over 17 bodies, and reported 9 limited joints. Across 5,000 steps it had
zero warnings, zero contacts, zero penetration, finite qpos/qvel, p95 step time
`62.327` microseconds, and realtime factor `43.07129555373134`.

The desktop source/enhanced neutral comparison reported source p95
`61.375` microseconds and enhanced p95 `54.743` microseconds, for hosted p95
overhead ratio `-0.10805702647657833`.

## Representative contacts

The isolated internal contact fixture reported:

- one contact;
- maximum normal force `63.79679083729198` N;
- maximum impulse `0.12759358167458396` N s;
- maximum penetration `0.0005000000000000074` m;
- zero warnings and finite state.

The isolated external contact fixture reported:

- one contact;
- maximum normal force `17.098584223842007` N;
- maximum impulse `0.034197168447684015` N s;
- maximum penetration `0.0005000000000000178` m;
- zero warnings and finite state.

Both penetrations remained below the committed `0.008` m ceiling. The native
state-format-v2 runner exposed one classified contact, one overload event,
health flags value `4`, nine hard-stop observations, maximum normal force
`63.796790837291979` N, and maximum impulse
`0.12759358167458396` N s. The neutral state exposed zero contacts and zero
contact-overload flags.

## Hard stops and invalid commands

The yaw trial observed the upper-limit constraint and contained the maximum
position at `2.792026803190879` rad below the hard upper limit
`2.792526803190879` rad. The right-antenna trial observed the upper-limit
constraint and contained the maximum position at `3.1195` rad below the hard
upper limit `3.12` rad. Both trials reported zero warnings.

The native telemetry runner additionally submitted a deliberately invalid
out-of-range actuator command and required the public ABI to return the typed
`COMMAND_FORMAT_ERROR` result before simulation continued.

## Physical Android benchmark

The physical job ran on an LGE LG-H872 with Android 8.0.0, API 26, and
`arm64-v8a`. The device serial is intentionally omitted from this public
record.

| Metric | Source model | Enhanced model | Requirement |
| --- | ---: | ---: | ---: |
| Steps | 50,000 | 50,000 | 50,000 each |
| Simulated time | 100 s | 100 s | complete |
| Realtime factor | 9.180208968594009 | 9.97500021157112 | >= 1.0 |
| Median step | 213.4899841621518 us | 189.4269953481853 us | recorded |
| p95 step | 238.5409898124635 us | 222.2909824922681 us | overhead <= 35% |
| Maximum step | 1083.384995581582 us | 20973.54200668633 us | recorded |
| Warnings | 0 | 0 | 0 |
| Maximum penetration | 0 m | 0 m | <= 0.008 m |

The measured p95 overhead ratio was `-0.06812249472499832`, comfortably below
the `0.35` ceiling. The one maximum-step outlier did not reduce enhanced
realtime factor below budget and did not produce a MuJoCo warning, contact, or
penetration; p95 is the committed complexity acceptance metric.

## Artifacts and integrity

Run `30654822714` published:

- hosted Android artifact ID `8802848639`, ZIP digest
  `sha256:1333466684399f702f85223576ab2536388453a610838eb3e9f7fdbe325bd4a9`;
- physical-device report artifact ID `8803021506`, ZIP digest
  `sha256:e92f7d47cc2152690dd96e1a83c7f80f886c5c1b073486f23e211755e7bd86fa`.

Selected staged-file SHA-256 values:

- `reachy_mujoco_collision_benchmark_runner`:
  `ee23f464d36e58f0ae192adb4c4892927f42ab1942dcf087123941142c9c1646`;
- `libmujoco.so`:
  `c18b1bb6bf1d80ac10118b75627551c0b12e907283ae1c56c1a0d40ce8515f6d`;
- collision/hard-stop validation JSON:
  `d644b8a3c3e1fa403526d8ef45f5518c5471dd1e720e5befea13f57677907303`;
- native contact-state JSON:
  `d2646dc78ececf95ff4ca7e095449dbac5308d881262ce71b88204695074fe98`.

## Acceptance conclusion

All RMA-065 dynamics-baseline criteria passed: representative internal and
external contacts were stable, invalid commands and hard-stop constraints were
reported rather than silently crossed, and collision complexity remained
within the measured physical-device budget. RMA-065 is complete while its
engineering-estimate and uncalibrated fidelity labels remain in force.
