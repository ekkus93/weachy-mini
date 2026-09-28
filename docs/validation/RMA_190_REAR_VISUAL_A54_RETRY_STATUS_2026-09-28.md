# RMA-190 Rear Visual A54 Retry Status — 2026-09-28

## Scope

This note records the current-head A54 retry after the user confirmed the Samsung A54 is connected. It preserves the exact result without reclassifying runner/device visibility as a source failure.

## Retried branch and head

- Branch: `ralph/rma190-rear-visual-e2e`
- Retry commit/head: `7835c761e817124b12b185a6a8a4349bdfa06952`
- Retry run: `36436008400`
- Workflow: `A54 RMA-190 Rear Visual E2E`
- Job: `108973992025` / `RMA-190 rear visual E2E on A54`
- Result: `failure`

## Completed successfully before the failure

The retry completed the following workflow steps successfully:

1. `Check out repository`
2. `Set up Python`
3. `Validate rear-visual harness contracts`
4. `Resolve local Unity and Android installations`
5. `Prepare generated Reachy Unity presentation`
6. `Stage production MuJoCo runtime for Unity Android`
7. `Build ARM64 API-26 device APK`
8. `Verify ARM64 API-26 device APK`

## Failure boundary

The failure occurred at step 10, `Pin one physical Android device`. The rear-camera visual acceptance step was skipped and did not execute.

This retry therefore does not invalidate the earlier passing rear-visual A54 evidence at heads `3688409ef8c191e3101b36bdedefe17641819ff3` / run `36178878668` and `55036222afa6b30c6505c6646f94cdb9840aada9` / run `36179670744`; it also does not provide new passing evidence for the current retry head.

The authoritative RMA-190 and RMA-194 checkboxes remain open until passing evidence is merged and reconciled on current `master` according to exact-head policy.
