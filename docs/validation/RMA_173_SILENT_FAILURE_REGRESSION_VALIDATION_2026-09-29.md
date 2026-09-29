# RMA-173 Silent-Failure Regression Validation — 2026-09-29

## Scope

This note records the current RMA-173 implementation and qualification status without closing the roadmap checkboxes prematurely.

RMA-173 requires permanent regression coverage proving that failures remain visible and do not activate hidden provider, privacy, calibration, stale-frame, solver, command-buffer, or malformed-output fallbacks.

## Implemented permanent gate

The permanent source-level regression suite is committed at:

- `scripts/tests/test_rma173_silent_failure_regressions.py`

It is automatically included in the default static validation path because `scripts/ci.sh --static-only` runs:

```bash
python3 -m unittest discover -s scripts/tests -v
```

The suite covers each RMA-173 bullet:

| RMA-173 requirement | Permanent coverage |
| --- | --- |
| On-device ASR failure does not activate cloud ASR | `test_on_device_asr_failure_does_not_activate_cloud_or_system_asr` verifies the live turn orchestrator creates only `ReachyAndroidOnDeviceAsrProviderFactory`, never `ReachyAndroidSystemAsrProviderFactory` or `OpenAi`, and returns a visible null/no-transcript outcome instead of a fallback. |
| Offline TTS failure does not activate a network voice | `test_offline_tts_failure_does_not_activate_network_voice` verifies the live TTS path creates only `ReachyAndroidOfflineTtsProviderFactory`, never the system/network TTS provider or OpenAI, and routes selection through `ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice`. |
| Local LLM failure does not activate OpenAI | `test_local_llm_failure_does_not_activate_openai_or_cloud_llm` verifies the `OnDevice` branch calls `RunLocalLlmAsync` and does not call `RunCloudLlmAsync`; failure diagnostics stay local and visible. |
| Invalid camera pixels do not reuse stale content | `test_invalid_camera_pixels_do_not_reuse_stale_content` verifies invalid coverage produces `FrameBuildResult.Failure` and rejects stale-frame caches such as `lastSuccessfulFrame` or `cachedTrackingFrame`. |
| Solver failure does not switch to Unity animation | `test_solver_failure_does_not_switch_to_unity_animation` verifies production simulation uses `ReachyProductionAuthoritativeRuntime`, fails visibly, and does not introduce `Animator` or `AnimationClip` fallback paths. |
| Calibration mismatch does not load placeholders as calibrated | `test_calibration_mismatch_does_not_load_placeholders_as_calibrated` verifies exact calibration selection and `NoCalibration` state, with no placeholder/fallback calibration path. |
| Dropped command/state buffers produce visible errors | `test_dropped_command_and_state_buffers_produce_visible_errors` binds managed and native contract evidence for zero-step, unknown-reset, post-close, stale-handle, invalid-copy, and busy-copy outcomes. |
| Malformed provider response does not produce a fake successful intent | `test_malformed_provider_response_does_not_fake_successful_intent` verifies cloud LLM validation must produce a real successful `ReachyBehaviorIntentValidationResult`; missing or malformed validation fails closed without constructing a replacement intent. |

Related application-composition regression coverage also exists in `scripts/tests/test_rma195_application_composition.py`, including checks that live ASR/TTS/LLM composition does not cross provider boundaries on failure.

## Exact-head evidence currently available

Current reviewed implementation head:

- `96ea3835edd0449bfab79392e12a3a4483ab5f49`

A physical A54 exact-head validation passed on that implementation head:

- Workflow: `Local Unity Android Device Validation No Artifacts`
- Run: `36604695975`
- Job: `109530619622`
- Result: success
- Covered stages: Unity tests, ARM64 API-26 APK build/verification, physical-device pinning, RMA-190 core end-to-end acceptance, RMA-090/091/092 camera acceptance, RMA-111 tracking acceptance, RMA-154 visual-servo acceptance, RMA-022 lifecycle acceptance, RMA-195 application-composition acceptance, and authoritative rendering acceptance.

That physical workflow is useful release evidence, but it is not the hosted static CI gate that runs `scripts/tests/test_rma173_silent_failure_regressions.py`.

## Hosted CI status

The exact-head push CI run for `96ea3835edd0449bfab79392e12a3a4483ab5f49` did not provide source-failure evidence:

- Workflow: `CI`
- Run: `36604696043`
- Result: failure
- Observed behavior: all five jobs (`static`, `managed`, `native`, `android`, `reachy-model`) completed in roughly 2–3 seconds with empty step lists.
- Ralph failure evidence: job logs were unavailable with `JOB_NOT_FOUND` for the failed jobs.

This is classified as a hosted CI/bootstrap infrastructure failure, not as an RMA-173 source regression. The RMA-173 TODO checkboxes should remain open until hosted CI, a configured Ralph validation profile, or an equivalent exact-head static validation run executes the static unittest discovery path successfully on the current source head.

## Roadmap reconciliation rule

Do not mark RMA-173 complete solely because this source gate exists. Close the RMA-173 checkboxes only after an exact-head validation result confirms the static unittest suite ran successfully after this validation note is present, or after an equivalent source-qualified static validation is recorded with its run/SHA evidence.
