# RMA-184 A54 default-profile capture evidence — 2026-09-25

## Scope

This note records a successful physical A54 performance-profile capture run used to advance RMA-184 representative-device work.

It is evidence that the current `master` application source can build, install, and complete the existing RMA-180 performance acceptance on the attached Samsung A54 while collecting both 30 FPS and 60 FPS profile data.

This note is **not** a publication of measured default profile values. The workflow intentionally printed a compact `RMA184_PROFILE_SUMMARY` to the successful job log, but the current Ralph Bridge CI report surface exposes only synthesized success/failure metadata for successful jobs, not successful-step log text. Therefore no measured values are copied here, and the RMA-184 `Publish measured default profiles` checkbox must remain open until the metric summary is captured into accessible repository evidence.

## Physical run

- temporary branch: `ralph/a54-rma184-default-profiles-20260925`
- workflow head: `7ee2e211d1405e4e9aa294a1dde4041cf8bcee3e`
- source baseline: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `A54 RMA-184 Default Profiles`
- run id: `36187273962`
- job id: `108243676196`
- job: `RMA-184 default profiles on A54`
- job URL: `https://github.com/ekkus93/weachy-mini/actions/runs/36187273962/job/108243676196`
- result: `success`

The run passed:

1. RMA-184/RMA-180 source-contract validation.
2. Local Unity and Android installation resolution.
3. Generated Reachy Unity presentation preparation.
4. Production MuJoCo Android runtime staging.
5. ARM64 API-26 device APK build.
6. APK existence verification.
7. Physical A54 pinning.
8. `scripts/run_rma180_performance_acceptance_android.sh` with full 300-second 30 FPS and 60 FPS profiles.

## Closure boundary

This run strengthens RMA-184 and RMA-194 release-readiness evidence because the A54 completed the full performance acceptance path on current `master` source.

Do **not** mark RMA-184 complete from this note alone. The remaining closure gap is preserving and publishing the actual measured default-profile values in an accessible document or machine-readable report.
