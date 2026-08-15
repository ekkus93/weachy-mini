# RMA-184 Representative-Device Matrix

**Task:** RMA-184 — Representative-device matrix  
**Date:** 2026-08-15  
**Status:** In progress — repository qualification tooling complete; mid/high physical long-run evidence pending

## Purpose

RMA-184 turns the RMA-180 performance harness, RMA-181 priority-degradation policy, and RMA-135 local-LLM resource profiles into an explicit Android support/qualification matrix. It does not add another resource governor and it does not silently infer that an unmeasured phone is supported.

The authoritative machine-readable registry is `models/reachy-mini/android-device-matrix.json`. Production-neutral policy code lives in `ReachyRepresentativeDeviceMatrix.cs`. The Android-only metadata probe is opt-in through `reachy_rma184_device_probe=true` and writes `rma184-device-probe.json` without recording prompts, transcripts, images, audio, credentials, or provider payloads.

## Performance classes and initial defaults

| Class | Representative baseline | Render target | Local LLM profile | Post-warmup memory-growth ceiling |
|---|---|---:|---|---:|
| Low | LG-H872 boundary device | 30 FPS | `Conservative` | 128 MiB |
| Mid | SM-A546E class | 30 FPS | `Balanced` | 192 MiB |
| High | OnePlus 11 5G / Pixel 7 Pro class | 60 FPS | `Performance` | 256 MiB |

All classes retain the existing RMA-133 minimum local decode gate of 1 token/s. A representative long run is at least 1,800 seconds. Authoritative physics p95 must stay at or below the fixed 2 ms timestep, and accumulated simulation-state lag may grow by no more than one 2 ms timestep over the post-warmup observation window.

The render p95 ceiling is the RMA-181 nominal-to-`RenderReduced` boundary: 1.15 times the nominal frame budget. This yields 38.333 ms for a 30 FPS default and 19.167 ms for a 60 FPS default. Crossing that boundary is visible degradation pressure, not a qualifying nominal result.

## Support classification

Core runtime support requires all of the following:

- Android API 26 or newer;
- at least 3 GiB of runtime-visible physical memory;
- at least four logical processors;
- Vulkan or OpenGL ES 3 graphics;
- an available rear camera.

A device that meets the core floor but lacks the front camera, explicit API-31 on-device ASR, or offline TTS is `SupportedWithLimitations`. This preserves the Android API-26 deployment floor while making the offline-speech limitation explicit. `Supported` means the core floor plus front camera, explicit on-device ASR, and offline TTS are all available. Missing a core requirement is `Unsupported`.

The classification is capability-based. Marketing model names never override a failing runtime probe.

## Representative devices

The registry currently carries four device-family entries so every class has a concrete target and the high class has a second implementation family:

- LGE LG-H872 — low/boundary class, existing physical camera and local-LLM evidence; API 26 necessarily lacks the API-31 explicit on-device ASR path.
- Samsung SM-A546E — mid class; runtime RMA-184 metadata/long-run qualification still required.
- OnePlus 11 5G — high class; runtime RMA-184 metadata/long-run qualification still required.
- Google Pixel 7 Pro — second high-class implementation family; runtime RMA-184 metadata/long-run qualification still required.

Family RAM configurations in the registry are discovery aids, not substitutes for the probe's runtime-visible `SystemInfo.systemMemorySize`. Support and qualification must use the observed device report.

## Android metadata probe

`scripts/run_rma184_device_probe_android.sh` grants the already-declared camera/microphone permissions, launches the opt-in probe, and pulls a JSON report. The report records:

- manufacturer and model;
- Android version/API;
- SoC model (Android `Build.SOC_MODEL` on API 31+, `Build.HARDWARE` fallback on older devices) and Unity processor string;
- logical processor count and runtime-visible RAM;
- graphics API and GPU name;
- camera permission/count plus front/rear availability;
- explicit on-device ASR availability;
- offline TTS availability;
- the resulting core/full-offline support classification.

The probe does not perform speech recognition or synthesize audible content; it checks provider availability only. Positive end-to-end speech acceptance remains owned by the speech tasks and Phase 20 scenarios.

## Long-run qualification

`ReachyRepresentativeDeviceQualificationPolicy` accepts normalized RMA-184 observations and fails closed when any of these are violated:

1. duration is shorter than 1,800 seconds;
2. render p95 exceeds the class default's RMA-181 nominal budget;
3. physics p95 exceeds 2 ms;
4. final Unity allocated memory grows beyond the class ceiling after warm-up;
5. authoritative state lag grows by more than 2 ms;
6. measured local-LLM decode falls below 1 token/s;
7. observed thermal transitions do not follow RMA-181's ordered degradation ladder.

RMA-180 already bounds retained timing/resource samples, so long characterization runs cannot grow the telemetry reservoir without limit. RMA-184 adds the final-vs-initial qualification gates; it does not change RMA-180 storage behavior.

## Thermal contract

RMA-184 does not tune a per-device degradation order. Every supported device must retain the same RMA-181 order:

1. reduce render/effects;
2. reduce camera/tracking work;
3. suspend VLM;
4. reduce local LLM;
5. enter critical mode only after lower-priority work is already shed.

Physics correctness and audio interaction remain protected invariants. A device that needs a different ordering is not qualified by this matrix.

## Measurement status and closure rule

The LG-H872 has reusable physical evidence for camera behavior and RMA-133 local-LLM thermal/throughput behavior, including the documented warm-run degradation. That evidence is sufficient to seed the low-class default, but it is not a substitute for an RMA-184 30-minute integrated post-warmup run.

Mid/high entries are deliberately marked `pending_measurement`. RMA-184 must not be marked complete until the representative physical runs populate runtime RAM/API/speech availability and demonstrate the long-run qualification gates. Repository/unit/static validation proves the policy and tooling; it does not fabricate those physical measurements.
