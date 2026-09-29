# RMA-190 Front-Camera E2E Boundary Static Validation — 2026-09-29

## Scope

This document records a source-level regression gate for the still-open RMA-190 scenario:

- `Front camera -> head rotation -> transformed Reachy-eye frame.`

The gate is intentionally conservative. It does not close the scenario and does not claim new physical camera evidence. It prevents the repository from accidentally treating the already-passing RMA-190 core harness or RMA-154 synthetic visual-servo harness as proof of the real front-camera transformed-frame scenario.

## Added permanent gate

Test file:

- `scripts/tests/test_rma190_front_camera_e2e_boundary.py`

The test is included by the existing Python unittest discovery path used by static CI:

```bash
python3 -m unittest discover -s scripts/tests -v
```

## Assertions

The gate requires all of the following:

1. `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` keeps the front-camera RMA-190 checkbox open until dedicated evidence exists.
2. `docs/validation/RMA_190_CORE_END_TO_END_VALIDATION_2026-09-29.md` states that the front-camera scenario is not closed, requires a dedicated live transformed-frame E2E assertion, and that RMA-154 synthetic optics cannot be represented as real front-camera evidence.
3. `Assets/ReachyMini/Runtime/Application/ReachyRma154VisualServoAcceptance*.cs` remains identifiable as synthetic-optics supporting evidence rather than a real front-camera harness.
4. `Assets/ReachyMini/Runtime/Application/ReachyAndroidPerceptionDriver.cs` retains the reusable `CaptureFrameForAcceptanceAsync` hook and the shipping `TryBuildFrame` path that uses authoritative camera rotation, homography warp, and owned transformed tracking-frame construction.

## Evidence boundary

This gate preserves truthfulness while the dedicated RMA-190 front-camera harness is still absent. It does not exercise a live camera frame and must not be used to close the RMA-190 front-camera checkbox.

The next implementation slice should add an Android physical acceptance harness, using the existing launch-extra/result-file convention, that starts the real front camera, exercises the shipping `CaptureFrameForAcceptanceAsync`/`TryBuildFrame` path, moves the authoritative head/camera state, and proves a new transformed Reachy-eye frame reflects that actual MuJoCo head rotation.

## Validation status

At commit creation time, hosted CI was still exhibiting the separate zero-step/bootstrap failure pattern and Ralph's configured local `static` validation profile errored before returning a result. Those infrastructure conditions are not source-failure evidence for this gate. The file is committed so the next functioning static run can qualify it exactly.
