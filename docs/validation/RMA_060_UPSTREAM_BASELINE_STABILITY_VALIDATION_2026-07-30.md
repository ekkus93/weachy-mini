# RMA-060 upstream baseline stability validation

**Status:** Complete  
**Validated commit:** `85dd886c398088946a2cc2ae61890aa94ad0294a`  
**Workflow:** `RMA-060 Baseline Stability`  
**Successful run:** `30599288952`  
**Commit status:** `RMA-060 Baseline Stability` — success

## Acceptance result

RMA-060 is accepted. The pinned official Reachy Mini model completed the same
45-cycle, 900,000-step, 1,800-simulated-second `upstream_baseline` schedule on
the hosted desktop reference runtime and on representative physical ARM64
Android hardware. Both paths used MuJoCo 3.9.0 and the exact 0.002-second physics
timestep. No timestep deviation was required.

The stability schedule covered neutral poses, the upstream sleep request, both
body-yaw limits, positive and negative boundary commands for all six Stewart
actuators, both antenna extremes, and a final neutral return. Every step checked
finite state, equality residuals, scalar joint-limit violations, active contacts,
contact penetration, total energy, and MuJoCo warning counters.

## Locked identities

| Identity | Validated value |
| --- | --- |
| Profile ID | `upstream_baseline` |
| Profile SHA-256 | `c1e0715133057e6815f0bc615107be74298b446b588abc22b0c7c0a688fb2f65` |
| Reachy source commit | `a739a6e461eb6d722901f1cfc225265ffc85c28d` |
| Model SHA-256 | `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46` |
| MuJoCo version | `3.9.0` |
| Physics timestep | `0.002` seconds / 500 Hz |
| Cycles | `45` |
| Phases per cycle | `20` |
| Steps per phase | `1,000` (`500` minimum-jerk transition + `500` hold) |
| Total steps | `900,000` |
| Simulated duration | `1,799.9999999712595` seconds |
| Timestep gate decision | `not_required` |

The small displayed difference from exactly 1,800 seconds is accumulated
binary floating-point representation of 900,000 exact 0.002-second solver
steps. The runner validates it against the profile with a fixed absolute
simulation-clock tolerance; it is not a changed timestep or skipped step.

## Hosted desktop reference result

The hosted job regenerated the native contract, passed the focused stability
unit tests, imported the exact pinned model, and ran the full 900,000-step
Python MuJoCo reference schedule before building Android artifacts.

| Desktop metric | Result |
| --- | ---: |
| Completed steps | `900,000` |
| Maximum equality residual | `0.00010839801784862102` |
| Maximum scalar joint-limit violation | `0.0` rad |
| Maximum contact penetration | `0.004506368083200589` m |
| Maximum active contact count | `6` |
| Minimum total energy | `0.9451720553210676` J |
| Maximum total energy | `1.2885171070884491` J |
| Maximum absolute total energy | `1.2885171070884491` J |
| MuJoCo warnings | `0` |
| Median measured step time | `64.65` microseconds |
| p95 measured step time | `81.562` microseconds |
| Maximum measured step time | `513.537` microseconds |

The job then cross-compiled `reachy_mujoco_stability_runner` and pinned MuJoCo
for Android ARM64, verified the runner as AArch64 ELF, staged the official model
and profile, and uploaded the complete device input artifact.

## Physical Android result

The physical gate ran on the existing representative device runner `kawa` with
this attached device:

| Device field | Value |
| --- | --- |
| Manufacturer | `LGE` |
| Model | `LG-H872` |
| Device codename | `lucye` |
| Android release | `8.0.0` |
| Android API level | `26` |
| ABI | `arm64-v8a` |

The native Android runner completed and validated the full schedule:

| Android metric | Gate | Result |
| --- | ---: | ---: |
| Completed steps | `900,000` | `900,000` |
| Simulated duration | `1,800` s | `1,799.9999999712595` s |
| Minimum solver real-time factor | `1.0` | `6.980001027847417` |
| Maximum equality residual | `0.001` | `0.00010839801784859326` |
| Maximum scalar joint-limit violation | `0.000001` rad | `0.0` rad |
| Maximum contact penetration | `0.01` m | `0.004506368083200441` m |
| Maximum absolute total energy | `100` J | `1.2885171070884491` J |
| MuJoCo warnings | `0` | `0` |

Additional Android timing evidence:

| Timing metric | Result |
| --- | ---: |
| Wall execution time | `257.879618182` s |
| Mean solver step | `281.3710051577806` microseconds |
| Median solver step | `282.552` microseconds |
| p95 solver step | `343.229` microseconds |
| Maximum solver step | `4,255.52` microseconds |
| Individual 2 ms deadline misses | `7` of `900,000` steps |

The seven isolated 2 ms solver-duration misses were visible rather than hidden.
They did not change the simulation clock, omit work, create backlog policy, or
prevent the device from sustaining the required average rate. The measured
solver real-time factor was approximately 6.98, and all state/constraint/contact/
energy monitors remained inside their locked bounds.

Desktop and Android aggregate dynamics results also agreed at practical numeric
precision. Their maximum absolute energy was identical in the serialized
reports, while maximum equality residual and maximum penetration differed only
in the final floating-point digits.

## Failure-path evidence

Before the long run, the device harness passed an invalid zero cycle count and
required a nonzero process result plus this structured failure:

```json
{"schema_version":1,"status":"failed","profile_id":"upstream_baseline","error":"cycles must be a positive 32-bit integer"}
```

This proves the device path does not convert invalid acceptance inputs into a
successful or partial stability report.

## Boundary-command correction discovered by the gate

The first full workflow run, `30598987487`, failed before dynamics execution
because one decimal target at an audited Stewart boundary rounded a few binary
floating-point units beyond MuJoCo's compiled control range. That failure was
not suppressed or reclassified as an allowed overrange.

The final profile declares a `1e-9` radian inward command-limit inset and applies
it to body-yaw and Stewart boundary probes. The inset is substantially below the
stability monitor's `1e-6` radian joint-limit threshold, keeps each probe
functionally at the audited upstream boundary, and avoids platform-dependent
one-ulp range classification. The exact upstream sleep command remains unchanged,
and its four intentional Stewart control-range exceedances remain explicitly
identified by the generated mask `102`.

The corrected commit reran the complete desktop, build, and physical-device gates
without weakening the monitoring thresholds or changing the 500 Hz timestep.

## Retained artifacts

Run `30599288952` retained two 30-day artifacts:

| Artifact | ID | ZIP SHA-256 |
| --- | ---: | --- |
| Android ARM64 runtime, model, profile, build metadata, and desktop report | `8781368497` | `83ffae7b4e3299151cac39778ac011424088cfa3f2764377b48d031f68b4796a` |
| Physical-device report, device identity, invalid-input report, and thermal captures | `8781460441` | `e2197cd77c263b0833497835c191315b69e77810b96eef321f2b381737d1d68a` |

The LG-H872 Android build does not expose `thermalservice`; the harness recorded
that absence and uploaded empty before/after thermal files. Thermal state was not
an RMA-060 acceptance criterion and was not fabricated from another source.
Power and thermal fidelity remain later Phase 7 work under RMA-064.

## Conclusion

The official generic actuator model now has a named, source-bound, generated,
and production-native `upstream_baseline` contract at exactly 500 Hz. The full
representative pose schedule is stable on desktop and physical Android hardware,
all required health monitors are active, no unexplained divergence occurred, no
MuJoCo warning was emitted, and no timestep deviation was needed. RMA-060 may be
marked complete.
