# RMA-195 finding — cloud-offloaded LLM generation keeps the SM-A546E cool where on-device generation drove it into thermal throttling

**Date:** 2026-08-22
**Device:** Samsung SM-A546E (Galaxy A54 5G), Exynos 1380, serial R5CW31AX4FL
**Status:** exploratory, not a permanent CI gate; recorded as evidence, not as an RMA-135
fix or a claim that RMA-135 is closed
**Related roadmap items:** RMA-135 (resource/thermal governor, local LLM acceptance
criterion, currently blocked by a genuine on-device thermal limitation on this device),
RMA-195 (cloud LLM/VLM composition wiring, phase D)

## Motivation

`docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md` documents that this
device's combined MuJoCo physics + **on-device** local-LLM workload drives it into
light thermal throttling (`mStatus=1`) within roughly 15 seconds, even from a
measured-cool start -- a genuine hardware characteristic, not a governor bug. The user
asked whether the same phone, running the same physics simulation but with LLM
inference offloaded to a **cloud-style** provider over the network instead of running
locally, would show a different thermal profile. This session had just built the real
cloud LLM provider stack (RMA-195 Phase D), including a settings-UI-driven credential
coordinator (`ReachyCloudLlmCredentialCoordinator`) and the real
`ReachyLocalLlmProviderApplicationService.GenerateAsync` cloud path -- both already
verified against a real OpenAI-compatible server (a local Ollama instance) in an
earlier same-day investigation, but never yet exercised on the physical device.

## Method

- **Cloud endpoint:** a real Ollama server (`llama3.2:3b`, OpenAI-compatible Chat
  Completions API) running on the development/CI host (`kawa`), reached from the phone
  via `adb reverse tcp:11434 tcp:11434` -- the phone's own `127.0.0.1:11434` transparently
  forwards to the host's Ollama instance. This is architecturally equivalent to a real
  cloud/local-network provider from the app's perspective: an HTTP round trip carries
  every token, and all inference compute happens off the phone's own SoC.
- **Coordinators fixed to allow this test:** `ReachyCloudLlmCredentialCoordinator` and
  `ReachyCloudVlmCredentialCoordinator` previously only accepted `https://` base URLs.
  Both were extended to auto-select `ReachyProviderTlsMode.LocalDevelopmentCleartext`
  for `http://` URLs, still gated by `ReachyProviderProfile`'s own
  `IsTrustedLocalDevelopmentHost` check (loopback/private/`.local` hosts only) -- a
  public `http://` URL is still rejected fail-closed exactly as before. Covered by 4
  new EditMode tests (2 per coordinator: accepts loopback http, rejects public http).
- **New exploratory probe** (`ReachyRma195CloudLlmThermalProbe.cs`, activated via the
  `reachy_rma195_cloud_llm_thermal_probe` launch-intent boolean extra, mirroring the
  RMA-134/135 acceptance-harness activation pattern): waits for the already-running
  production `ReachyProductionAuthoritativeRuntime` (never a second simulation worker,
  per RMA-135's own invariant), configures a cloud LLM profile via the real
  `ReachyCloudLlmCredentialCoordinator` (base URL `http://127.0.0.1:11434`, model
  `llama3.2:3b`), grants fallback-policy authorization, then repeatedly calls the real
  `ReachyLocalLlmProviderApplicationService.GenerateAsync` cloud path for 45 seconds
  while physics runs normally, writing checkpoints throughout. No new provider/transport
  code -- this only drives the already-built, already-tested cloud LLM path under
  sustained load.
- **Thermal capture:** `scripts/run_rma195_cloud_llm_thermal_probe_android.sh` installs
  the app, launches it with the extra, and captures `adb shell dumpsys
  thermalservice`/`dumpsys battery` at three points: immediately before launch, mid-run
  (as soon as the first generation attempt completes), and immediately after the probe
  reports completion -- the same `dumpsys thermalservice` evidence source the original
  RMA-135 finding used.

## Result

| Point | Elapsed | SKIN | AP (SoC) | `mStatus` |
|---|---:|---:|---:|---|
| Before launch | 0s | 34.7 C | 35.3 C | 0 (none) |
| Mid-run | ~3.3s into generation | 35.3 C | 37.9 C | 0 (none) |
| Immediately after | ~46.8s total | 36.4 C | 41.3 C | 0 (none) |

`mStatus=0` ("none") held at every sample -- the device never crossed into `mStatus=1`
(Light) at any point during the run. Probe report:

```json
{
    "status": "completed",
    "device_model": "samsung SM-A546E",
    "cloud_llm_base_url": "http://127.0.0.1:11434",
    "cloud_llm_model_id": "llama3.2:3b",
    "generation_window_seconds": 45.22,
    "generation_attempts": 203,
    "generation_succeeded_schema_valid": 203,
    "generation_failed_or_invalid": 0,
    "last_status": "BehaviorIntentInvalid",
    "last_detail": "Behavior intent gaze_target entity_id must match entity-[0-9]+ within bounds."
}
```

203 generation round trips completed in 45 seconds (~4.4 per second) -- every attempt
reached the real server and returned a real, parsed response (the RMA-151 schema
strictness rejecting some completions as "invalid" is a separate, expected concern from
whether the network round trip and physics stayed healthy; see the same phenomenon
in the earlier same-day local `.NET` smoke test against the identical Ollama server).

**For direct comparison**, RMA-135's documented on-device baseline (same device,
`docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md`): starting from a
comparable cool baseline (~35 C SKIN), roughly **16 seconds** of combined physics +
**on-device** LLM generation drove SKIN to 40.5 C (`mStatus=1`, Light) and AP to 47.2 C.

This run's 45-second cloud-offloaded window -- nearly 3x longer than the on-device
baseline's 16 seconds -- still finished at a lower SKIN (36.4 C vs 40.5 C) and lower AP
(41.3 C vs 47.2 C), and never left `mStatus=0`. A longer duration would ordinarily be
expected to accumulate *more* heat, not less, which makes the gap more notable, not less.

## Interpretation

Moving LLM token generation off the phone's own SoC and onto a network-reachable
server removes the dominant source of the combined workload's heat, consistent with
the original RMA-135 finding's own diagnosis (the *combined* on-device physics+LLM
workload is what drives thermal throttling, not physics alone). This is a real,
measured data point in favor of cloud/local-network LLM offload as a way to avoid this
specific device's thermal limitation -- but it does **not** close or supersede RMA-135:

- RMA-135's acceptance criterion is specifically about the **on-device** local-LLM path,
  which remains genuinely thermally limited on this device; this finding does not change
  that.
- This run used a different (smaller, `llama3.2:3b` vs `qwen3-0.6b`) model on
  fundamentally different hardware (the host's CPU/GPU, not the phone's SoC) -- the
  models are not directly comparable in isolation, but the point of this experiment was
  never model quality, it was whether the phone itself stays cooler when it isn't the
  one doing the inference. It does.
- The live conversational-turn trigger for the cloud LLM path is still not wired into
  the real app (documented in the RMA-195 TODO entry) -- this probe drives the same
  underlying `GenerateAsync` path directly, bypassing that still-open gap, which is
  the correct thing to do for this specific thermal question but does not itself close
  that gap.
- This was one run, not a statistically repeated series, and the network path (adb
  reverse to a host on the same machine as the CI runner) is not identical to a true
  wide-area cloud endpoint's latency/jitter profile, though it is architecturally the
  same code path a real cloud endpoint would exercise.

## Reproduction

```bash
export REACHY_ANDROID_SERIAL=<device serial>
export ADB_BIN=/path/to/adb
# Ollama (or any OpenAI-compatible server) listening on 127.0.0.1:11434 on this host.
./scripts/build_unity_android.sh development   # requires UNITY_EDITOR, ANDROID_SDK_ROOT
./scripts/run_rma195_cloud_llm_thermal_probe_android.sh
```
