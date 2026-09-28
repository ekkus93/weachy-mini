# RMA-190 Consolidation Update: RMA-134 Local LLM A54 Evidence — 2026-09-25

## Purpose

This supplement records that the local LLM physical-device prerequisite for the RMA-190 spoken-turn lane has now passed on the connected A54.

It does not close the full RMA-190 spoken-turn scenario by itself. The spoken-turn scenario still needs one combined production acceptance covering on-device ASR, local LLM, behavior execution, offline TTS, and explicit offline/no-network posture.

## Evidence

- Evidence PR: #76, `ralph/rma134-a54-local-llm-evidence`
- Evidence branch head: `419ad3e5a441d8929e588f26f0bb1f7e0b734d2d`
- Temporary validation branch: `ralph/a54-rma134-local-llm-20260925`
- Passing validation head: `7ff718020b303add6f7143743bac7d295bb73c47`
- Passing workflow run: `36140591155`
- Workflow: `A54 RMA-134 Local LLM`
- Result: success
- App source base: current `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`

## Passing path

The passing run completed:

1. local LLM harness contract validation,
2. Unity/Android installation resolution,
3. generated Reachy Unity presentation preparation,
4. production MuJoCo runtime staging,
5. ARM64 API-26 APK build and verification,
6. physical A54 pinning,
7. exact APK installation on the A54,
8. RMA-134 local LLM acceptance.

## Prior temporary-workflow failures

Earlier attempts failed because the temporary workflow did not yet match the RMA-134 harness assumptions:

- `36138257509`: reached acceptance but produced no useful report.
- `36138949367`: added diagnostics but still did not install the exact APK before acceptance.
- `36139640452`: installed the APK but used `adb install --no-stream`, which the A54 package-manager path rejected.

The passing run removed `--no-stream` and used a portable install path.

## Current consolidation impact

RMA-190 spoken-turn should now treat local LLM on A54 as physically validated, but not the complete scenario. Remaining spoken-turn work is to bind ASR, local LLM, behavior execution, offline TTS, and offline network posture into one end-to-end production acceptance run.
