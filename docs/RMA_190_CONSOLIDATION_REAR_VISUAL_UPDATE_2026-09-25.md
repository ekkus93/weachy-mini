# RMA-190 Consolidation Update — Rear Visual A54 Evidence — 2026-09-25

## New evidence

The RMA-190 rear-camera visual E2E lane now has passing physical A54 evidence.

- PR: #43 (`ralph/rma190-rear-visual-e2e`)
- Current PR head: `55036222afa6b30c6505c6646f94cdb9840aada9`
- Source/test head physically validated: `3688409ef8c191e3101b36bdedefe17641819ff3`
- Passing A54 run: `36178878668`
- Workflow: `A54 RMA-190 Rear Visual E2E`
- Result: `success`

The passing run completed source-contract validation, Unity/Android resolution, generated presentation preparation, Android runtime staging, ARM64 API-26 APK build, APK verification, physical A54 pinning, and `Run RMA-190 rear-camera visual end-to-end acceptance`.

## Repairs made while qualifying the lane

Two source issues were found and repaired before the successful run:

1. `ReachyRma190RearVisualEndToEndAcceptance.cs` needed `using ReachyMini.Interop;` so Unity Android could resolve `ReachySimAuthoritativeStateFrame` and `ReachySimBodyPoseSnapshot`.
2. `ReachyAndroidPerceptionDriver.TryBuildFrame` needed the same timestamp-correspondence fix already proven in the front-camera lane: the fail-closed identity phone-orientation sample must be stamped with `descriptor.TimestampNanoseconds`, not driver wall-clock time.

A temporary diagnostic patch also added rear-visual acceptance phase reporting, which exposed the real A54 failure before the timestamp fix:

```text
capturing transformed rear frame; last perception state=NoCameraFrame; message=Homography reprojection did not produce a frame: The camera texture and phone orientation are not timestamp-corresponding.; revision=2729.
```

## Completion boundary

This evidence supports the RMA-190 rear-camera visual question lane. It does not complete the still-open full spoken-turn, network/provider failure, model corruption/OOM, or solver controlled-reset lanes.

PR #43 remains blocked by the repository clean-mergeability gate because generic CI is still failing with zero-step/log-unavailable infrastructure failures. The physical A54 validation itself passed.
