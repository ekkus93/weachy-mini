# RMA-135 — Resource and Thermal Governor Specification

**Date:** 2026-08-10  
**Status:** Implementation specification  
**Roadmap task:** RMA-135

## 1. Purpose

RMA-135 adds a production resource governor around local LLM inference. Its job is to protect the authoritative MuJoCo simulation and the Android process from LLM-induced thermal or memory pressure. It must degrade or suspend local inference before simulation timing is compromised.

The governor is not a quality-tuning system and must not conceal failures. It may reduce explicitly bounded execution resources, cancel/suspend local inference, and expose the reason. It must not change simulation timestep, skip arbitrary physics work, silently switch models/providers, invoke cloud services, retry a cancelled generation automatically, repair invalid model output, or reinterpret unavailable telemetry as healthy telemetry.

## 2. Evidence inherited from RMA-133/RMA-134

RMA-133 selected Qwen3-0.6B Q4_K_M. The accepted cool-start physical run stayed within the benchmark gates, but the warm closure run fell to approximately 0.57 token/s from a 34.9 C start and reached 43.0 C. That warm run is explicit evidence that production thermal governance is required.

RMA-134 defines the local-provider execution profile and preserves a strict no-cloud/no-repair/no-hidden-queue boundary. RMA-135 may derive a smaller execution profile from that baseline at explicit provider admission, but it must preserve all non-resource behavior controls. The loaded `LocalLlmProvider` profile remains immutable. If a later observation requires a smaller envelope, governed generation is cancelled or denied and explicit provider recreation is required; RMA-135 does not mutate or invisibly reload a live provider.

## 3. Priority invariant

The resource priority is:

1. authoritative MuJoCo simulation correctness and timing;
2. process survival and memory safety;
3. local audio/camera work already admitted by their own owners;
4. local LLM inference throughput and latency.

A new physics deadline miss during a governed inference window is an immediate LLM suspension condition. RMA-135 never modifies the physics timestep or claims that reduced simulation fidelity is acceptable compensation for LLM load.

## 4. Input signals

### 4.1 Android memory

Read `ActivityManager.MemoryInfo` where running on Android:

- `totalMem`;
- `availMem`;
- `threshold`;
- `lowMemory`.

Invalid Android results are errors. They are not converted to fabricated safe values.

### 4.2 Android thermal

On Android API 29+, read `PowerManager.getCurrentThermalStatus()` and preserve the platform levels NONE, LIGHT, MODERATE, SEVERE, CRITICAL, EMERGENCY, and SHUTDOWN.

On API 28 and below, thermal status is explicitly `Unavailable`. This is an observability limitation, not evidence of a cool device. A failure to read the API on an API level that should support it is fail-visible.

### 4.3 Physics timing

Use the existing authoritative `ReachySimulationTimingSnapshot` rather than a parallel timing implementation. Relevant monotonic/delta signals are:

- total step count;
- deadline miss count;
- accumulated lag;
- last step duration.

Counter regression is treated as an invalid telemetry condition.

### 4.4 OOM

A caught `OutOfMemoryException` associated with local inference is an immediate suspension signal. The governor must not immediately retry the same generation. Recovery requires normal cleanup plus healthy observations.

## 5. Device profiles

RMA-135 V1 defines deterministic execution ceilings from total RAM and logical processor count. These are conservative resource envelopes, not claims that a device class is fully supported.

- **Conservative:** unknown resources, less than 6 GiB RAM, or 4 or fewer logical processors. Ceiling: 1024 context, 128 batch, 64 micro-batch, 2 generation threads, 2 batch threads.
- **Balanced:** at least 6 GiB but less than 10 GiB RAM, or fewer than 8 logical processors. Ceiling: 1536 context, 192 batch, 64 micro-batch, 3 generation threads, 3 batch threads.
- **Performance:** at least 10 GiB RAM and at least 8 logical processors. Ceiling: 2048 context, 256 batch, 64 micro-batch, 4 generation threads, 4 batch threads.

Unknown device resources select the Conservative envelope and remain visible in diagnostics. An unavailable authoritative physics-budget sample suspends inference until a subsequent sample can establish that physics is healthy; the governor never treats missing physics telemetry as permission to run.

## 6. Governor modes

### Nominal

Use the baseline execution profile capped by the device profile.

### Reduced

Used for LIGHT thermal pressure or early memory pressure. Reduce context to at most 75% of the device-capped value, halve batch size, and reduce generation/batch thread counts by one without dropping below one.

### Minimal

Used for MODERATE thermal pressure, stronger memory pressure, or physics timing that is at risk but has not yet missed a deadline. Reduce context to at most 50%, quarter batch size, and use one generation/batch thread.

### Suspended

No inference may start. Active inference must be cancelled by the production integration layer. Suspension conditions include:

- SEVERE or worse Android thermal status;
- Android `lowMemory`;
- critical available-memory pressure;
- a new physics deadline miss / exceeded physics budget;
- a recent local-inference OOM;
- unavailable authoritative physics-budget telemetry;
- failure of a required resource-signal read in production integration;
- a baseline whose preserved output-token limit cannot fit within the selected device context ceiling.

## 7. Memory thresholds

Given a valid total-memory signal:

- critical: available memory at or below `max(lowMemoryThreshold, total/12)`;
- minimal: at or below `max(2*lowMemoryThreshold, 15% total)`;
- reduced: at or below `max(3*lowMemoryThreshold, 25% total)`.

Android `lowMemory=true` always suspends regardless of ratios.

## 8. Hysteresis

Escalation is immediate. Recovery to a less restrictive mode requires three consecutive observations requesting the less restrictive mode. This prevents oscillation around a threshold. No automatic generation retry occurs when recovery completes.

## 9. Execution-profile integrity

The profile selected at explicit provider admission may change only:

- context tokens;
- batch tokens;
- micro-batch tokens as required to remain <= batch;
- generation threads;
- batch threads.

It must preserve:

- sampling temperature;
- min-p;
- seed;
- output-token limit unless a future explicitly reviewed policy changes it;
- stream queue bound;
- conversation/message limits;
- response byte bound;
- selected model/artifact/grammar/provider.

## 10. Diagnostics and UI contract

Every decision exposes:

- governor mode;
- device profile;
- reason flags;
- effective context/batch/micro-batch/thread values, or `suspended`;
- explicit unavailable-signal flags.

Production integration must surface the current decision in the diagnostics provider and show a user-facing local-LLM unavailable/throttled reason when inference is suspended. It must never present throttled inference as ordinary success.

## 11. Cancellation and cleanup

Before provider creation, the integration layer evaluates admission and passes the reported effective profile to the existing `LocalLlmProvider.CreateAsync` path. Before every generation it samples again and refuses to start if the loaded provider profile exceeds the current safe envelope.

When an in-flight generation receives a stronger resource decision than the loaded profile permits, the integration layer cancels through a linked cancellation token. It does not reset the conversation, retry automatically at the lower profile, or invisibly reload the model. Explicit provider recreation is required before a smaller profile can be used. The underlying RMA-134 cancellation/drain/release contract remains authoritative.

If cleanup fails, the provider must remain faulted/unavailable rather than starting another generation on uncertain native state.

## 12. Validation

RMA-135 closure requires:

- managed deterministic tests for all modes, device profiles, signal-unavailable behavior, hysteresis, OOM, and profile integrity;
- static Android bridge tests proving the intended platform APIs and no hidden fallback path;
- Unity/Android build success;
- physical-device evidence on the representative phone showing thermal/memory observations and physics coexistence;
- an induced pressure/cancellation test that leaves the provider recoverable without app restart;
- exact-SHA hosted and device validation evidence.

## 13. Explicit non-goals

RMA-135 does not implement cloud fallback, model selection, model re-quantization, arbitrary model swapping, dynamic physics degradation, battery policy beyond the available thermal/memory signals, or the full RMA-171 diagnostics screen.
