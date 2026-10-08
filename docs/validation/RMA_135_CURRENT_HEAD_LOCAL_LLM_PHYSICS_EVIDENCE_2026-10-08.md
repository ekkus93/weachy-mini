# RMA-135 Current-Head Local LLM Physics Evidence — 2026-10-08

## Scope

This note records exact-head physical Android evidence for the RMA-135 local-LLM acceptance criteria that overlap with the RMA-134 physical provider harness. It does **not** close the representative-device long-run, thermal-degradation, or release-performance gates owned by RMA-184 and RMA-194.

## Exact head

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Commit: `33332e81498dc174181cc00fce916f68bb14f9a3`
- Triggering source workflow: `Local Unity Android Validation` run `37729982975`
- Physical RMA-134 workflow: `RMA-134 local LLM Android acceptance` run `37731000395`
- Physical RMA-134 job: `113159851173` (`Validate RMA-134 on physical Android`)
- Uploaded artifact: `11530685642` (`rma134-local-llm-android-33332e81498dc174181cc00fce916f68bb14f9a3`)

## What the physical RMA-134 workflow proved

The exact-head physical RMA-134 workflow completed successfully. Its job steps pinned an approved representative physical Android device, downloaded the exact-SHA Unity Android artifact from the successful Local Unity Android validation run, ran the managed-provider physical acceptance harness, recorded workflow provenance, uploaded the physical evidence artifact, and published a success status.

The acceptance runner `scripts/run_rma134_local_llm_acceptance_android.sh` verifies the selected RMA-133 Qwen3 0.6B Q4_K_M artifact before use:

- selected model id: `qwen3-0.6b`
- selected model size: `396704416` bytes
- selected model SHA-256: `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`
- model suffix contract: `/no_think`

It then installs the exact validated APK, verifies the installed APK SHA-256 against the host artifact, clears app data, pushes the exact model to app-private storage, verifies the model SHA-256 and size on-device, launches the Unity app with `reachy_rma134_acceptance=true`, and waits for the shipping acceptance report.

The runner requires the device report to satisfy all of the following before it accepts the result as passed:

- `status == passed`
- `reachy_llama_abi == 2`
- `model_id == qwen3-0.6b`
- `artifact_sha256 == b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`
- `artifact_bytes == 396704416`
- `initial_generation_status == Succeeded`
- `cancellation_status == Cancelled`
- `reset_status == Superseded`
- `reload_status == Reloaded`
- `post_reload_generation_status == Succeeded`
- at least one streamed text event
- at least 250 physics steps
- physics p95 budget exactly `2000.0` microseconds
- finite physics p95 value at or below `2000.0` microseconds
- `adaptive_resource_changes_applied == false`
- `network_fallback_used == false`
- `json_repair_used == false`

## RMA-135 interpretation

This exact-head evidence supports the narrow RMA-135 local-LLM acceptance claims that the selected benchmark-backed model can load and produce a validated intent offline on an approved representative Android device, and that physics p95 remained within the defined 2 ms budget during that generation acceptance run.

It does **not** prove the broader RMA-184/RMA-194 representative-device release-performance criteria. In particular, it does not replace measured default profiles, long-running memory/state-lag evidence, sustained thermal-priority degradation evidence, or supported-device target reports.

## Boundaries that remain open

The following evidence gates remain intentionally open until their own required physical evidence exists:

- RMA-002 independent two-environment Unity/Android reproducibility.
- RMA-125 real network-disabled spoken ASR → behavior → offline TTS conversation.
- RMA-184 measured default profiles and long-running/thermal/device-target acceptance.
- RMA-190 unchecked end-to-end scenarios.
- RMA-194 representative-device performance report and final release-gate reconciliation.
