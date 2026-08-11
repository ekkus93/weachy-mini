# RMA-135 — Resource and Thermal Governor TODO

**Date:** 2026-08-10  
**Status:** In progress

## Phase 0 — Freeze the safety boundary

- [x] Preserve the RMA-133 selected model and RMA-134 no-cloud/no-repair/no-hidden-queue contract.
- [x] Define physics timing as higher priority than local LLM throughput.
- [x] Prohibit simulation timestep enlargement or arbitrary physics-step skipping as an LLM mitigation.
- [x] Prohibit automatic retry after resource cancellation.

## Phase 1 — Deterministic governor contracts

- [x] Define thermal, physics-budget, governor-mode, device-profile, and reason types.
- [x] Define a bounded resource snapshot.
- [x] Define Conservative, Balanced, and Performance device envelopes.
- [x] Implement Nominal, Reduced, Minimal, and Suspended decisions.
- [x] Preserve non-resource generation behavior parameters.
- [x] Implement immediate escalation and three-sample recovery hysteresis.
- [x] Keep unavailable telemetry explicit in diagnostics.

## Phase 2 — Android resource signals

- [x] Read `ActivityManager.MemoryInfo.totalMem`.
- [x] Read `ActivityManager.MemoryInfo.availMem`.
- [x] Read `ActivityManager.MemoryInfo.threshold`.
- [x] Read `ActivityManager.MemoryInfo.lowMemory`.
- [x] Read `PowerManager.getCurrentThermalStatus()` on API 29+.
- [x] Represent pre-API-29 thermal telemetry as explicitly unavailable.
- [x] Fail visibly when supported Android signal collection throws or returns invalid values.

## Phase 3 — Physics-budget bridge

- [x] Consume existing `ReachySimulationTimingSnapshot` telemetry.
- [x] Treat a new deadline miss as physics-budget exceeded.
- [x] Treat lag growth / over-budget last-step duration as at-risk.
- [x] Reject regressing authoritative counters.
- [x] Avoid introducing a second simulation timing loop.
- [x] Cover baseline/healthy/at-risk/exceeded and counter-regression behavior in managed tests.

## Phase 4 — Production local-LLM integration

- [x] Add a governed generation coordinator around `LocalLlmProvider`.
- [x] Add explicit pre-creation admission that returns the safe execution profile.
- [x] Keep the loaded RMA-134 provider profile immutable; do not introduce ambient/per-generation mutation.
- [x] Sample resource state immediately before inference starts.
- [x] Refuse to start when the governor is suspended or the loaded profile exceeds the current safe envelope.
- [x] Monitor resource state during active generation.
- [x] Cancel active generation through the ordinary provider cancellation token on a stronger resource decision.
- [x] Do not reset the conversation or automatically retry the cancelled request at a smaller profile.
- [x] Require explicit provider recreation before a more restrictive profile can be used.
- [x] Preserve RMA-134 drain/release/fault semantics by leaving cancellation cleanup owned by `LocalLlmProvider`.

## Phase 5 — Diagnostics and user-visible state

- [x] Publish the latest governor decision from the governed coordinator.
- [x] Project current governor mode, device profile, reason flags, and effective profile into a privacy-safe application diagnostic snapshot.
- [x] Add an optional governor diagnostic source to the existing provider/main-screen diagnostics path without creating a parallel UI notification channel.
- [x] Surface suspended local-LLM state as `Unavailable` and throttled local-LLM state as explicitly throttled without overwriting a higher-priority application `Error`.
- [x] Include explicit `thermal telemetry unavailable` labeling for API 28 and below.
- [x] Prove the governor diagnostics implementation and main-screen wiring cannot access prompt/chat/intent/response content.
- [x] Keep `ReachyUnavailableProviderApplicationService` from implementing the governor diagnostic source; normal production composition remains truthfully unavailable until a real local-provider service is integrated.

## Phase 6 — OOM and cancellation cleanup

- [x] Catch local-inference `OutOfMemoryException` as typed `ResourceExhausted` rather than generic runtime failure.
- [x] Cancel/drain/release an active generation after post-start OOM when cleanup can still be proven.
- [x] Latch suspension after OOM and require three consecutive nominal observations before governor recovery.
- [x] Test OOM before a native generation handle exists.
- [x] Test OOM during active generation polling.
- [x] Test post-start OOM cancellation/drain/release success.
- [x] Test cleanup failure leaves the provider faulted/unavailable and does not fabricate release success.
- [x] Prove explicit provider reload/recovery after OOM followed by a successful second generation without app/process restart.

## Phase 7 — Validation and physical evidence

- [x] Add warnings-as-errors managed governor contract tests.
- [x] Add deterministic static Android/physics/coordinator contract tests.
- [x] Add dedicated `RMA-135 Resource Governor` CI status workflow.
- [x] Expose read-only authoritative timing through `IReachySimulationTimingSource` without exposing simulation ownership.
- [x] Make `ReachySimulationWorker` and `ReachyProductionAuthoritativeRuntime` implement the same read-only timing contract used by the governor budget source.
- [x] Make physical acceptance discover and observe the app's live `ReachyProductionAuthoritativeRuntime` instead of creating a second MuJoCo session/worker.
- [x] Require three consecutive admissible startup timing observations before model work while preserving and reporting any startup `Exceeded` observations.
- [x] Keep the physical acceptance non-owning: it must never dispose, stop, or replace the production simulation worker.
- [x] Harvest the complete physical checkpoint set into the workflow evidence artifact rather than retaining only the latest checkpoint.
- [ ] Pass hosted RMA-135 workflow on the exact implementation SHA.
- [ ] Pass permanent repository CI on the exact implementation SHA.
- [ ] Pass Local Unity Android Validation on the exact implementation SHA.
- [ ] Collect representative-phone total/available/threshold memory evidence.
- [ ] Collect thermal status evidence; on API-26 record the explicit lack of API-29 thermal status.
- [ ] Run local LLM concurrently with authoritative MuJoCo stepping.
- [ ] Demonstrate that a physics-budget violation suspends/cancels LLM work rather than degrading physics.
- [ ] Demonstrate recovery without process restart.
- [ ] Record source SHA, APK SHA-256, device/API, model artifact SHA-256, governor decisions, physics timing, and cleanup outcome.

## Phase 8 — Closure

- [ ] Write `docs/validation/RMA_135_RESOURCE_THERMAL_GOVERNOR_VALIDATION_2026-08-10.md`.
- [ ] Reconcile the five RMA-135 roadmap bullets against exact evidence.
- [ ] Mark RMA-135 complete only after hosted + Unity/Android + physical acceptance are green on the intended SHA.
- [ ] Leave any unsupported telemetry or device limitation explicit rather than weakening acceptance.
