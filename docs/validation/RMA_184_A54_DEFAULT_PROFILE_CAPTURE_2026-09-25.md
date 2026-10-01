# RMA-184 A54 default-profile capture evidence — 2026-09-25

## Scope

This note records successful physical Samsung A54 performance-profile capture work used to advance RMA-184 representative-device release evidence.

It is evidence that the current `master` application source can build, install, and complete the existing RMA-180 performance acceptance on the attached Samsung A54 while collecting both 30 FPS and 60 FPS default profile data.

## Physical pass run

- temporary branch: `ralph/a54-rma184-default-profiles-20260925`
- workflow head: `7ee2e211d1405e4e9aa294a1dde4041cf8bcee3e`
- source baseline: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `A54 RMA-184 Default Profiles`
- run id: `36187273962`
- job id: `108243676196`
- job: `RMA-184 default profiles on A54`
- job URL: `https://github.com/ekkus93/weachy-mini/actions/runs/36187273962/job/108243676196`
- result: `success`

The pass run completed:

1. RMA-184/RMA-180 source-contract validation.
2. Local Unity and Android installation resolution.
3. Generated Reachy Unity presentation preparation.
4. Production MuJoCo Android runtime staging.
5. ARM64 API-26 device APK build.
6. APK existence verification.
7. Physical A54 pinning.
8. `scripts/run_rma180_performance_acceptance_android.sh` with full 300-second 30 FPS and 60 FPS profiles.

## Metric extraction run

The successful pass run printed a compact `RMA184_PROFILE_SUMMARY`, but Ralph Bridge does not expose successful-step logs through its synthesized CI report surface. A second temporary extraction workflow reran the same acceptance path, suppressed noisy build logs, emitted the profile summary as explicit GitHub error annotations, and then intentionally failed after the acceptance had passed so Ralph could capture the metrics.

- temporary branch: `ralph/a54-rma184-profile-summary-extract-20260925`
- extraction workflow head: `4eabd6995aa713686526eeecf6b2422d0e043745`
- source baseline: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `A54 RMA-184 Profile Summary Extract`
- run id: `36192045766`
- job id: `108259212316`
- job URL: `https://github.com/ekkus93/weachy-mini/actions/runs/36192045766/job/108259212316`
- terminal result: `failure`, intentionally caused by `RMA184_PROFILE_SUMMARY_EXTRACTED_INTENTIONAL_FAILURE` after acceptance succeeded and metrics were emitted.

Ralph failure extraction captured the following summary status:

```json
{"android_api_level":null,"device_model":null,"profile_count":2,"schema_version":1,"source":"rma180-performance-acceptance","status":"passed"}
```

`android_api_level` and `device_model` are null in the compact summary because the current RMA-180 report schema does not copy those fields into the emitted summary object. The workflow-level evidence still pins the run to the attached physical A54 through the successful physical-device pin step.

## Measured default profiles

### 30 FPS default profile

| Workload | Availability | Samples | Median ms | P95 ms | P99 ms | Max ms | Notes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| NativePhysics | Available | 150020 | 0.4017 | 0.5869 | 0.7534 | 28.3355 | Approximate percentiles |
| UnityRendering | Available | 8954 | 33.32587331533432 | 33.43089669942856 | 33.525142818689346 | 133.39173793792725 | Approximate percentiles |
| CameraAcquisition | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| CameraWarp | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| LightweightTracking | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| LocalLlm | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| Audio | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| Network | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |

- target FPS: `30`
- duration seconds: `300.0421124`
- resource sample count: `30`

### 60 FPS default profile

| Workload | Availability | Samples | Median ms | P95 ms | P99 ms | Max ms | Notes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| NativePhysics | Available | 150001 | 0.4341 | 0.6415000000000001 | 0.7527 | 18.425 | Approximate percentiles |
| UnityRendering | Available | 15722 | 16.667155548930168 | 33.32940116524696 | 33.41473266482353 | 266.6037678718567 | Approximate percentiles |
| CameraAcquisition | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| CameraWarp | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| LightweightTracking | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| LocalLlm | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| Audio | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |
| Network | Unavailable | 0 | 0 | 0 | 0 | 0 | Workload was not exercised during this performance session. |

- target FPS: `60`
- duration seconds: `300.0041515`
- resource sample count: `30`

## Closure boundary

This evidence publishes the measured default-profile values captured from the A54 RMA-180/RMA-184 acceptance path. It supports closing the RMA-184 `Publish measured default profiles` requirement once this evidence is merged and the authoritative TODO is reconciled on `master`.

The unavailable camera, local-LLM, audio, and network workload rows are explicit non-exercised measurements from this default-profile session, not hidden success claims for those workloads. Separate scenario-specific physical runs remain responsible for those subsystem gates.
