# RMA-190 Rear Visual Current-Head A54 Validation — 2026-09-25

## Scope

This note records that the RMA-190 rear-camera visual end-to-end lane passed on the current PR head, not only on the earlier source/test head recorded in the first validation note.

## Validated branch and source

- Repository: `ekkus93/weachy-mini`
- Branch: `ralph/rma190-rear-visual-e2e`
- Validated head: `55036222afa6b30c6505c6646f94cdb9840aada9`
- Workflow run: `36179670744`
- Workflow: `A54 RMA-190 Rear Visual E2E`
- Job ID: `108218682971`
- Job: `RMA-190 rear visual E2E on A54`
- Result: `success`

## Passing steps

The exact current-head run completed these steps successfully:

1. `Validate rear-visual harness contracts`
2. `Resolve local Unity and Android installations`
3. `Prepare generated Reachy Unity presentation`
4. `Stage production MuJoCo runtime for Unity Android`
5. `Build ARM64 API-26 device APK`
6. `Verify ARM64 API-26 device APK`
7. `Pin one physical Android device`
8. `Run RMA-190 rear-camera visual end-to-end acceptance`

The run used the branch-scoped A54 workflow and validated the exact PR head that also contains the documentation record.

## Evidence boundary

This is physical-device evidence for the RMA-190 rear-camera visual lane at current PR head. It supports the rear-camera transformed-frame path and optional VLM branch behavior as exercised by `scripts/run_rma190_rear_visual_end_to_end_android.sh`.

It does not close unrelated RMA-190 lanes such as full spoken-turn ASR -> local LLM -> behavior -> offline TTS, provider/network failure modes, model corruption/OOM, permission denial/revocation, or solver controlled-reset behavior.

The PR remains merge-gated by generic CI infrastructure failures where jobs start with no steps and logs are unavailable. That generic failure is not classified as a rear-visual source failure.
