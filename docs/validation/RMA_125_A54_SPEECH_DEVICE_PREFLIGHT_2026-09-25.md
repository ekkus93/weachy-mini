# RMA-125 A54 speech device preflight — 2026-09-25

## Source and run

- Evidence branch used for the physical run: `ralph/a54-rma125-speech-preflight-20260925`
- Evidence branch head: `b79f489ac3f1f40c6dae3673f1ae47b24682ffe2`
- Base app source for the temporary branch: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Workflow: `A54 RMA-125 Speech Device Preflight`
- Run id: `36104703145`
- Job: `Run RMA-125 speech device preflight on A54`

The temporary branch changed only a branch-scoped workflow file that invoked the existing repository script:

```text
scripts/run_rma125_speech_device_preflight.sh
```

No application source was changed for this preflight.

## Result

The physical preflight step succeeded on the attached A54:

```text
RMA-125 device preflight: eligible
Device: samsung SM-A546E, Android 16/API 36, ABI arm64-v8a
physical ARM64 API-31+ platform floor satisfied
Evidence: build/rma125-speech-device-preflight/device-preflight.json
Platform preflight passed. This does not prove the on-device ASR service, language model, offline TTS voice, network-disabled state, or end-to-end RMA-125 acceptance.
```

The workflow conclusion was `failure` only because the final artifact upload step hit GitHub Actions artifact storage quota:

```text
Failed to CreateArtifact: Artifact storage quota has been hit. Unable to upload any new artifacts. Usage is recalculated every 6-12 hours.
```

That artifact-storage failure is infrastructure/evidence-retention failure, not a device preflight failure.

## Interpretation

This evidence establishes only the platform preflight required for later offline-speech acceptance:

- the connected device was physical, not an emulator;
- the device reported ARM64 (`arm64-v8a`);
- the device reported Android API 36, satisfying the API-31+ floor required by the RMA-125 script;
- the device model was `samsung SM-A546E`.

This evidence does **not** prove any of the following:

- on-device ASR service availability;
- installed offline ASR language model;
- installed offline TTS voice for the target locale;
- network-disabled operation;
- full RMA-125 offline-speech physical acceptance;
- full RMA-190 spoken-turn acceptance.

## Follow-up required

The RMA-190 spoken-turn scenario remains open. A later physical workflow or acceptance harness still needs to prove the complete path:

```text
on-device ASR -> local LLM -> behavior -> offline TTS
```

with network disabled and without substituting system/network ASR, cloud LLM, network-capable TTS, or fabricated provider success.
