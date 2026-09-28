# RMA-154 A54 Visual Servo Validation — 2026-09-25

## Scope

This note records a physical Android validation run for the RMA-154 visual-servo acceptance path on a Samsung A54-class device.

The validation was run from a temporary branch-scoped workflow whose app source was equivalent to current `master` at:

- `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Temporary workflow branch head: `e6825fdb72539efb8bcd7b487e4b4a80ac71d306`

The temporary workflow file itself is not intended to merge. This documentation branch preserves the evidence without adding the one-off workflow to `master`.

## Physical validation evidence

- Workflow: `A54 RMA-154 Visual Servo`
- Run ID: `36148919716`
- Job: `RMA-154 visual servo on A54`
- Result: `success`
- Device class: physical Android ARM64 device pinned by the workflow.

Successful steps reported by Ralph Bridge:

1. Check out repository.
2. Set up Python.
3. Validate visual-servo harness contracts.
4. Resolve local Unity and Android installations.
5. Prepare generated Reachy Unity presentation.
6. Stage production MuJoCo runtime for Unity Android.
7. Build ARM64 API-26 device APK.
8. Verify ARM64 API-26 device APK.
9. Pin one physical Android device.
10. Run RMA-154 visual-servo acceptance.

## Interpretation

This validates that the RMA-154 visual-servo Android acceptance harness can build the master-equivalent app, install/run against the available physical A54 device, and complete the visual-servo acceptance successfully.

This evidence supports later RMA-190/RMA-194 release reconciliation work involving camera/perception-driven behavior and representative-device Android qualification, but it does not by itself close the full RMA-190 rear-camera visual-question or full spoken-turn scenarios.

## Follow-up

- Keep the temporary workflow branch out of `master`.
- Use this run as physical evidence during TODO reconciliation once merge-gate CI is healthy.
- Do not mark broader end-to-end items complete until their scenario-specific acceptance evidence exists and is merged/reconciled.
