# RMA-184 A54 profile-summary status — 2026-09-28

## Scope

This note records bounded Ralph-visible evidence from the A54 RMA-184 profile-summary extraction lane. It is intentionally narrower than the redacted profile-capture record and does not close the authoritative RMA-184 or RMA-194 performance-report checkboxes by itself.

## Evidence inspected

- Repository: `ekkus93/weachy-mini`
- Workflow branch: `ralph/a54-rma184-profile-summary-extract-20260925`
- Workflow head: `4eabd6995aa713686526eeec8f6b2422d0e043745`
- Workflow: `A54 RMA-184 Profile Summary Extract`
- Run ID: `36192045766`
- Job ID: `108259212316`
- Job: `RMA-184 profile summary extraction on A54`
- Step with machine-readable summary: `Run acceptance and emit Ralph-readable summary`

The run intentionally ended with a failure marker so that Ralph would surface the generated profile summary through failure evidence. The acceptance summary visible through Ralph reported:

```json
{"android_api_level":null,"device_model":null,"profile_count":2,"schema_version":1,"source":"rma180-performance-acceptance","status":"passed"}
```

## Interpretation

This is physical A54-path evidence that the underlying RMA-180 performance acceptance completed successfully and produced two profile entries for the RMA-184 default-profile capture workflow.

The job conclusion is not treated as a source failure because the workflow deliberately prints the summary as an error marker and exits non-zero after the acceptance pass so Ralph can collect the bounded JSON summary.

## Boundary

The detailed profile rows emitted around the summary are redacted by Ralph as URL-bearing records in the current inspection output, and the original `docs/validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md` patch is also redacted. Therefore this consolidation branch records only the bounded visible summary and does not reconstruct or fabricate:

- per-profile target FPS rows;
- NativePhysics, UnityRendering, CameraAcquisition, CameraWarp, LightweightTracking, LocalLlm, Audio, or Network timing values;
- resource sample counts; or
- device model/API fields beyond what is visible in Ralph output.

RMA-184's measured default-profile publication item and RMA-194's representative-device performance-report item must remain open until the detailed metric-level report is safely readable, committed, or otherwise accepted through an explicit release criterion.
