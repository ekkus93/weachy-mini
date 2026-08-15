# RMA-181 Priority-Based Degradation Policy

**Task:** RMA-181 — Implement priority-based degradation policy  
**Date:** 2026-08-15  
**Status:** Implemented

## Purpose

RMA-181 defines one deterministic degradation policy for resource, thermal, and
frame-time pressure. The policy protects authoritative simulation correctness
first and sheds lower-priority work in a fixed order instead of allowing each
subsystem to make unrelated fallback decisions.

The preservation order is normative:

1. authoritative simulation correctness;
2. audio interaction;
3. camera analysis and lightweight tracking;
4. UI responsiveness;
5. LLM/VLM throughput;
6. visual quality and previews.

The policy never changes the calibrated simulation timestep and never authorizes
arbitrary physics-step skipping. Audio is not degraded by this task.

## Signals

`ReachyPriorityDegradationSignals` consumes the existing RMA-135 resource
signals:

- Android total/available memory and low-memory threshold;
- Android low-memory notification;
- Android thermal state;
- authoritative simulation physics-budget state.

It may additionally consume a recent Unity render p95 in milliseconds. RMA-180
provides the timing vocabulary and measurement boundary; a caller may feed a
recent render p95 into the policy without coupling the policy to a particular
sampling window implementation.

Unknown thermal or memory signals remain explicit reasons. An unavailable
physics signal is also explicit; it does not cause the policy to invent a
physics fallback or alter simulation behavior.

## Degradation ladder

The ordered levels are:

| Level | Render/effects | Tracking analysis | VLM | Local LLM |
|---|---|---|---|---|
| `Nominal` | nominal 30/60 FPS, baseline effects | 640 px max, unthrottled | allowed | nominal |
| `RenderReduced` | 30 FPS from a 60 FPS baseline, or 20 FPS from a 30 FPS baseline; expensive effects disabled | 640 px, unthrottled | allowed | nominal |
| `CameraReduced` | reduced render | 480 px max, 15 Hz max | allowed | nominal |
| `VlmSuspended` | reduced render | 320 px max, 10 Hz max | suspended/cancelled | nominal |
| `LlmReduced` | reduced render | 256 px max, 8 Hz max | suspended | minimum LLM mode `Minimal` |
| `Critical` | 15 FPS, expensive effects disabled | 192 px max, 5 Hz max | suspended | suspended |

Worsening pressure applies immediately. Recovery to a less restrictive level
requires three consecutive lower-pressure observations. This hysteresis keeps
thermal and resource noise from rapidly toggling expensive workloads.

## Classification

The initial engineering policy uses these inputs:

- render p95 greater than 1.15 times the nominal frame budget requests
  `RenderReduced`;
- render p95 greater than 1.50 times the nominal frame budget requests
  `CameraReduced`;
- light thermal pressure requests `RenderReduced`;
- moderate thermal pressure requests `VlmSuspended`;
- severe-or-worse thermal pressure requests `Critical`;
- physics `AtRisk` requests `LlmReduced`, which necessarily includes all
  earlier render/camera/VLM reductions before LLM reduction;
- physics `Exceeded` requests `Critical`;
- Android low-memory or critical available-memory pressure requests `Critical`;
- lower memory-pressure tiers request camera reduction and then LLM reduction.

These thresholds are policy baselines, not representative-device calibration.
RMA-184 owns measured device classes and published default profiles.

## Subsystem integration

### Unity rendering

`ReachyUnityPriorityDegradationTarget` applies the decision to Unity presentation
work. It lowers `Application.targetFrameRate`, disables vSync while governed,
and disables shadows, anti-aliasing, and soft particles when expensive effects
are not allowed. It captures the pre-policy values and can restore them.

It does not alter the simulation worker, MuJoCo timestep, solver settings, or
native step cadence.

### Camera analysis and lightweight tracking

`ReachyOnDeviceLightweightTracker` implements the degradation target. The
existing bounded pixel staging call receives the policy's maximum analysis
dimension. A policy interval additionally throttles analysis by authoritative
source-frame timestamp.

A throttled frame returns an explicit `Unavailable` result. It performs no pixel
staging, performs no detector invocation, and does **not** substitute the last
successful tracking result. This prevents a lower analysis rate from becoming a
silent stale-content fallback.

### VLM

`ReachyVlmScheduler` implements the degradation target. When VLM is disallowed:

- all active leases are marked cancellation-requested;
- cancellation callbacks are dispatched outside the scheduler lock;
- callback failures remain counted in existing diagnostics;
- new requests return the explicit `ResourceSuspended` scheduling status;
- no alternate VLM provider is selected.

Recovery re-enables admission only after the central policy has recovered.

### Local LLM

`LocalLlmResourceGovernor` accepts the policy's minimum governor mode as an
additional floor. The existing RMA-135 resource checks remain authoritative and
may always request a stronger restriction. Existing RMA-135 recovery hysteresis
continues to apply when the RMA-181 floor relaxes.

Thus an RMA-181 `LlmReduced` decision cannot make the local LLM less restrictive
than `Minimal`, and `Critical` cannot make it less restrictive than
`Suspended`.

### Runtime composition seam

`ReachyPriorityDegradationRuntime` bridges the existing
`ILocalLlmResourceSignalSource` and `ILocalLlmPhysicsBudgetSource` into
`ReachyPriorityDegradationCoordinator`. The coordinator applies one immutable
decision to an explicitly supplied bounded set of targets.

The repository's current production composition still has unavailable provider
and perception services in its baseline scene, so RMA-181 does not invent a
parallel provider/perception composition. Instead, the real LLM governor, VLM
scheduler, and lightweight tracker now expose the target contract and are
controlled whenever those already-existing components are composed. Unity's
render target is the presentation-side target for the same decision.

## Safety invariants

Every decision exposes and tests these invariants:

- `PhysicsTimestepSeconds` is exactly
  `ProjectMetadata.InitialPhysicsTimestepSeconds`;
- `PhysicsStepSkippingAllowed` is always `false`;
- `AudioInteractionPreserved` is always `true`;
- no degradation target modifies `ReachySimulationWorker` or native step calls;
- no provider fallback is introduced;
- VLM cancellation and camera throttling are visible states, not fake success.

## Validation strategy

Managed contracts cover:

- degradation ordering and exact action envelopes;
- three-observation recovery hysteresis;
- immutable physics/audio invariants;
- RMA-181 local-LLM floor plus RMA-135 recovery behavior;
- VLM cancellation, blocked admission, and recovery;
- tracking resolution reduction and cadence throttling without stale reuse.

A static contract suite verifies production hooks, runtime signal wiring, and
roadmap closure. Physical thermal characterization and device-specific tuning
remain RMA-184 work.
