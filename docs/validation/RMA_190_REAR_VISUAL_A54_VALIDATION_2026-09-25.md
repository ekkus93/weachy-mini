# RMA-190 Rear Visual A54 Validation — 2026-09-25

## Scope

This note records physical Android validation for the RMA-190 rear-camera visual end-to-end lane on the Samsung A54.

## Validated branch and source

- Repository: `ekkus93/weachy-mini`
- Branch: `ralph/rma190-rear-visual-e2e`
- Validated head: `3688409ef8c191e3101b36bdedefe17641819ff3`
- Workflow run: `36178878668`
- Workflow: `A54 RMA-190 Rear Visual E2E`
- Job: `RMA-190 rear visual E2E on A54`
- Result: `success`

## Passing steps

The exact-head run completed these steps successfully:

1. `Validate rear-visual harness contracts`
2. `Resolve local Unity and Android installations`
3. `Prepare generated Reachy Unity presentation`
4. `Stage production MuJoCo runtime for Unity Android`
5. `Build ARM64 API-26 device APK`
6. `Verify ARM64 API-26 device APK`
7. `Pin one physical Android device`
8. `Run RMA-190 rear-camera visual end-to-end acceptance`

The device pinned by the acceptance workflow is the available physical Samsung A54 path used by the repository's A54 workflows.

## Fixes proven by this run

This run validates the branch after two source repairs:

- `ReachyRma190RearVisualEndToEndAcceptance.cs` imports `ReachyMini.Interop`, resolving the Unity Android build failure for `ReachySimAuthoritativeStateFrame` and `ReachySimBodyPoseSnapshot`.
- `ReachyAndroidPerceptionDriver.TryBuildFrame` stamps the fail-closed identity phone-orientation sample with `descriptor.TimestampNanoseconds`, preserving timestamp correspondence between the camera texture and phone-orientation sample. This fixes the prior rear-visual failure: `Homography reprojection did not produce a frame: The camera texture and phone orientation are not timestamp-corresponding.`

## Evidence boundary

This is physical-device evidence for the rear-camera transformed-frame path and optional VLM branch behavior as exercised by `scripts/run_rma190_rear_visual_end_to_end_android.sh` at the validated head. The workflow succeeds only after it builds the exact branch APK, verifies the APK exists, pins a physical arm64 API-26+ Android device, launches the shipping app acceptance path, validates the produced JSON report, and exits successfully.

The run does not close unrelated RMA-190 lanes such as full spoken-turn ASR → local LLM → behavior → offline TTS, network/provider failure modes, model corruption/OOM, or solver controlled-reset behavior.
