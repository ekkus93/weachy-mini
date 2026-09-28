# RMA-190 Consolidation Update — RMA-125 Network-Disabled Speech Evidence — 2026-09-25

## New evidence

The A54 now has a passing physical network-disabled speech probe that strengthens the RMA-125/RMA-190 spoken-turn prerequisite evidence.

- Evidence PR: #79 (`ralph/rma125-network-disabled-speech-evidence`)
- Evidence PR head: `bc59b100d44a4140a4816eef05ac02b58d53e872`
- Temporary validation branch: `ralph/a54-rma125-network-disabled-speech-20260925`
- Validated head: `c6493a7bef9c592df7567a2f53ffeb55a3857648`
- Passing A54 run: `36182986407`
- Workflow: `A54 RMA-125 Network-Disabled Speech Probe`
- Result: `success`

The passing run completed speech/composition contract validation, Unity/Android resolution, generated presentation preparation, Android runtime staging, ARM64 API-26 APK build, APK verification, physical A54 pinning, network-disable verification, and the shipping RMA-195 application-composition acceptance path.

## What this proves

This evidence supports these claims:

- The Samsung A54 physical device can run the shipping app build while Wi-Fi and mobile data are disabled by the validation workflow.
- The network reachability probe fails after Wi-Fi/mobile-data disable, before the speech composition probe starts.
- The shipping app still reports explicit on-device ASR availability and offline TTS start/completion through the RMA-195 acceptance path while networking is disabled.
- The validation does not inject a transcript, cloud credentials, or a local model.

## Completion boundary

This evidence does **not** close the full RMA-125 offline conversation checkbox or the full RMA-190 spoken-turn checkbox by itself. Those remain open until a real human-utterance ASR -> local LLM -> behavior -> offline TTS acceptance path is validated, or an explicitly accepted non-fabricated ASR acceptance method is specified and validated.

PR #79 remains blocked by the repository clean-mergeability gate because generic CI is still failing with zero-step/log-unavailable infrastructure failures. The A54 physical validation itself passed.
