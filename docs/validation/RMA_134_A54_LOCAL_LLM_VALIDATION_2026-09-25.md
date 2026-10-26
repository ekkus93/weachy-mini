# RMA-134 A54 Local LLM Validation — 2026-09-25

## Scope

This note preserves physical Android evidence for the local LLM acceptance path on the connected Samsung A54 while keeping the temporary workflow branch out of `master`.

The validation supports the RMA-134 local LLM provider gate and contributes evidence for the RMA-190 spoken-turn path and RMA-194 representative-device release acceptance. It does not by itself close the full RMA-190 spoken-turn scenario because that scenario still also requires on-device ASR, behavior execution, and offline TTS in one end-to-end production flow.

## Exact source under validation

- Repository: `ekkus93/weachy-mini`
- App source base: current `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Temporary validation branch: `ralph/a54-rma134-local-llm-20260925`
- Successful validation head: `7ff718020b303add6f7143743bac7d295bb73c47`
- Temporary workflow-only changes: `.github/workflows/a54-rma134-local-llm.yml`

The temporary branch only added a branch-scoped validation workflow. It did not change production app source.

## Physical device

- Device family: Samsung A54
- Reported model in earlier A54 dumps: `SM-A546E`
- Physical-device serial selected by runner variable: `R5CW31AX4FL`
- Validation runner requirement: `self-hosted`, `linux`, `x64`, `weachy-mini-android-device`

## Passing run

- Workflow: `A54 RMA-134 Local LLM`
- Run: `36140591155`
- Job: `RMA-134 local LLM on A54`
- Result: `success`
- Head: `7ff718020b303add6f7143743bac7d295bb73c47`

Successful steps reported by Ralph Bridge:

1. Set up job
2. Check out repository
3. Set up Python
4. Validate local LLM harness contracts
5. Resolve local Unity and Android installations
6. Prepare generated Reachy Unity presentation
7. Stage production MuJoCo runtime for Unity Android
8. Build ARM64 API-26 device APK
9. Verify ARM64 API-26 device APK
10. Pin one physical Android device
11. Install exact APK on A54
12. Run RMA-134 local LLM acceptance
13. Post-job cleanup

## Earlier failures and remediation

Two earlier temporary-workflow attempts failed before this successful run:

- Run `36138257509` reached the A54 and failed in the local LLM acceptance step, but the workflow did not produce a useful device-side report.
- Run `36138949367` added diagnostics, but still did not install the exact APK before invoking the RMA-134 acceptance harness.
- Run `36139640452` added installation, but the A54 package-manager path rejected `adb install --no-stream` with `Unknown option --no-stream`.

The successful run `36140591155` used a portable install path: normal `adb install -r -d` first, with a device-side `pm install` fallback if needed.

## Interpretation

The passing run proves that the current master-equivalent app source can build, install, and pass the RMA-134 local LLM acceptance harness on the connected A54.

This is not a full RMA-190 spoken-turn completion claim. The spoken-turn RMA-190 item still needs one combined production acceptance covering:

- on-device ASR,
- local LLM,
- behavior execution,
- offline TTS,
- explicit offline/no-network posture,
- and evidence captured in one end-to-end run.

## Merge guidance

This documentation-only branch is the persistent evidence record. Do not merge the temporary workflow branch `ralph/a54-rma134-local-llm-20260925`; it exists only to run the physical validation.
