# RMA-180 A54 Performance Validation — 2026-09-25

## Scope

This record preserves a successful representative Android-device performance acceptance run executed while the Samsung A54 was connected to the self-hosted Android runner.

This is validation evidence only. It does not close the authoritative roadmap by itself because `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` must be reconciled only after the relevant evidence branch is merged and any release checklist language is updated safely.

## Exact run evidence

- Repository: `ekkus93/weachy-mini`
- Current master source SHA under test: `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Temporary validation branch: `ralph/a54-rma180-performance-20260925`
- Temporary validation branch head: `f238b9abdbc0eb636e2c5785e84f63f51952e262`
- Workflow: `A54 RMA-180 Performance`
- Run id: `36106746123`
- Job: `RMA-180 performance on A54`
- Result: `success`
- Started: `2026-09-25T07:15:42Z`
- Completed: `2026-09-25T07:29:57Z`

The temporary validation branch changed only `.github/workflows/a54-rma180-performance.yml`; application source remained current `master`.

## Successful steps

The successful run passed all of the following steps:

1. Checkout.
2. Python setup.
3. Performance harness source-contract validation.
4. Local Unity and Android installation resolution.
5. Generated Reachy Unity presentation preparation.
6. Production MuJoCo runtime staging for Unity Android.
7. ARM64 API-26 APK build.
8. APK existence verification.
9. Physical Android device pin.
10. `scripts/run_rma180_performance_acceptance_android.sh` performance acceptance.

## Evidence limitations

The workflow intentionally omitted artifact upload because GitHub Actions artifact storage quota was exhausted at the time of the run. Ralph Bridge can synthesize the successful run/job/step report but cannot retrieve the printed metric JSON from successful-step logs. Therefore this record proves the acceptance harness passed on the connected A54, but it does not preserve the metric-level JSON payload in repository history.

A follow-up run should capture the full metric-level report once artifact storage or successful-log extraction is available. Until that report is preserved, this should be treated as strong representative-device acceptance evidence but not a complete archived performance-report package.

## Roadmap interpretation

This run directly supports the RMA-194 release checklist item `Representative-device performance report exists`, but it should not be marked complete on `master` until the validation note is merged and the metric-level report capture limitation is resolved or explicitly accepted in the release criteria.
