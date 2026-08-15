# RMA-180 Performance Harness Specification

**Task:** RMA-180 — Build performance harness
**Date:** 2026-08-15

## Objective

RMA-180 provides one bounded performance-observation path for the Android digital twin. The harness measures real production boundaries instead of running substitute microbenchmarks and keeps simulation timing distinct from Unity frame cadence.

The harness is observational. It does not change provider selection, simulation timestep, camera policy, model quality, or fallback behavior. RMA-181 owns any later degradation decisions.

## Timing domains

`ReachyPerformanceWorkload` defines the required domains:

- `NativePhysics` — exact duration around the authoritative native `StepRaw(1)` call on the simulation worker;
- `UnityRendering` — end-to-end Unity frame interval from `Time.unscaledDeltaTime`, recorded by the runtime performance probe and kept separate from native physics timing;
- `CameraAcquisition` — Android camera platform handoff through `AcquireLatestTextureFrame`;
- `CameraWarp` — complete homography/coverage/GPU-warp pipeline execution;
- `LightweightTracking` — complete on-device lightweight tracking request, including pixel staging and backend detection;
- `LocalLlm` — complete local LLM generation wrapper lifetime;
- `Audio` — complete audio-coordinated ASR or TTS interaction, including focus/lease lifecycle and the selected provider;
- `Network` — complete shared HTTP transport operation, including response consumption, retries, and retry backoff.

Audio and network samples may intentionally overlap for a network-backed speech request. They answer different questions: end-to-end audio interaction cost versus transport cost.

## Session model

Only one performance session may be active at a time. A session requires:

- an identifier-only label of at most 64 characters;
- an explicit 30 FPS or 60 FPS target profile.

Production instrumentation performs a cheap active-session check and does not retain timing data when no harness session is active. Measurement scopes use the monotonic `Stopwatch` clock.

Nested sessions fail closed rather than silently mixing samples from different profiles.

## Timing aggregation

For each workload the harness records:

- total sample count;
- median;
- p95;
- p99;
- exact maximum.

Each workload keeps a deterministic 4,096-entry reservoir. Percentiles are exact until the reservoir capacity is exceeded. On longer runs the total count and maximum remain exact while percentile fields are explicitly marked `percentiles_approximate=true`.

This keeps high-frequency 500 Hz physics measurement bounded during long Android runs.

A workload that is not exercised is emitted as `Unavailable` with an explicit reason rather than a fabricated zero-latency success result.

## Resource sampling

`ReachyPerformanceRuntimeProbe` samples resources every 10 seconds while a session is active:

- Unity allocated memory from `Profiler.GetTotalAllocatedMemoryLong()`;
- Android available memory through the existing RMA-135 resource-signal source when available;
- battery level from `SystemInfo.batteryLevel`;
- Android thermal state through the existing resource-signal source.

The resource ring retains at most 2,048 samples. At a 10-second cadence this covers more than 5.6 hours without unbounded memory growth. When the ring fills, the oldest resource sample is replaced and `dropped_sample_count` increments.

The report summarizes session-wide aggregates independent of ring rotation:

- initial/final/maximum Unity allocated memory;
- minimum Android available memory;
- initial/final battery level and discharge fraction;
- peak observed thermal state/severity.

Those aggregates are updated before ring insertion, so rotating detailed samples does not lose the session's initial battery level, memory extrema, or peak thermal state.

Unavailable resource signals are recorded with bounded implementation-level reasons. Exception messages, credentials, prompts, transcripts, images, audio, and provider payloads are not written into performance reports.

## 30/60 FPS Android acceptance

`ReachyRma180PerformanceAcceptance` is opt-in through Android launch extras:

- `reachy_rma180_performance_acceptance=true`;
- `reachy_rma180_profile_seconds=<10..3600>`.

The default is 300 seconds per profile. After a short warm-up the acceptance runner:

1. sets `Application.targetFrameRate` to 30 and captures one performance report;
2. sets `Application.targetFrameRate` to 60 and captures a second report;
3. restores the prior target frame rate and VSync setting;
4. atomically writes `rma180-performance-acceptance.json` under the app persistent-data directory.

The acceptance runner requires native physics, Unity rendering, and resource samples for both profiles. Other workloads remain explicitly unavailable if the operator did not exercise them during that profile. Their real production hooks are still active and will populate samples when exercised.

`scripts/run_rma180_performance_acceptance_android.sh` installs/launches the APK on the dedicated device, requests both profiles, captures before/after battery/memory/thermal diagnostics, validates the result schema, and retains logcat plus the performance report.

## Privacy and failure behavior

The harness records numeric timings, resource values, enum/identifier labels, and bounded availability reasons only. It does not record request text or media. Session labels reject whitespace and arbitrary text to prevent a caller from treating the label as an ad-hoc private-data channel.

Instrumentation never changes a workload result. If telemetry is inactive, the production operation proceeds unchanged. If a workload is not exercised, the report says so; it does not synthesize measurements.

## Relationship to later tasks

RMA-180 only observes and reports. RMA-181 may consume these metrics when implementing priority-based degradation, but RMA-180 itself makes no quality, provider, camera, audio, or simulation policy decisions.
