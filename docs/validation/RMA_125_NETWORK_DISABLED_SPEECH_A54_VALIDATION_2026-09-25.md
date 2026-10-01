# RMA-125 Network-Disabled Speech A54 Validation — 2026-09-25

## Scope

This note records a physical Samsung A54 validation run that strengthens the RMA-125 offline-speech evidence by proving the shipping Android app can expose on-device ASR readiness and complete offline TTS while the device network interfaces are disabled.

This is **not** a full RMA-125 closure claim: the authoritative TODO still requires a real user conversation using Android on-device ASR and offline TTS with networking disabled. This run does not inject or fabricate a human ASR transcript and therefore leaves that conversation checkbox open.

## Validated run

- Repository: `ekkus93/weachy-mini`
- Temporary validation branch: `ralph/a54-rma125-network-disabled-speech-20260925`
- Validated head: `c6493a7bef9c592df7567a2f53ffeb55a3857648`
- Workflow run: `36182986407`
- Workflow: `A54 RMA-125 Network-Disabled Speech Probe`
- Job: `RMA-125 network-disabled speech probe on A54`
- Result: `success`

## Passing steps

The run completed these steps successfully:

1. `Validate speech and composition contracts`
2. `Resolve local Unity and Android installations`
3. `Prepare generated Reachy Unity presentation`
4. `Stage production MuJoCo runtime for Unity Android`
5. `Build ARM64 API-26 device APK`
6. `Verify ARM64 API-26 device APK`
7. `Pin one physical Android device`
8. `Run RMA-125 network-disabled speech probe`

The physical probe step disables Wi-Fi and mobile data on the pinned A54, verifies a network reachability probe fails, then runs the existing shipping RMA-195 application-composition acceptance path. Cleanup restores Wi-Fi and mobile data.

## Evidence claims

This run supports these claims:

- A physical Samsung A54 can run the shipping app build used by the acceptance path.
- The device can be pinned as an arm64 API-31+ physical Android target for offline speech validation.
- The workflow disables Wi-Fi/mobile data before the speech probe and verifies that an external network ping no longer succeeds.
- The shipping composition acceptance still passes while networking is disabled.
- The underlying RMA-195 acceptance validates explicit on-device ASR availability and offline TTS start/completion without injecting a transcript, cloud credentials, or a local model.

## Evidence boundaries

This run does **not** prove the full RMA-125 conversation criterion by itself because it does not capture a real user utterance through ASR and route that recognized transcript through the complete conversation turn. It also does not prove RMA-190's full spoken-turn scenario by itself.

The authoritative TODO items for full network-disabled conversation and RMA-190 spoken-turn closure must remain open until a human-utterance or otherwise explicitly accepted non-fabricated ASR acceptance method is validated.
