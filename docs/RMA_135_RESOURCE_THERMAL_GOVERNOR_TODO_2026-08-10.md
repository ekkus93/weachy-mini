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

- [ ] Add a governed generation entry point around `LocalLlmProvider`.
- [ ] Allow an explicitly supplied per-generation resource profile without changing existing RMA-134 default behavior.
- [ ] Sample resource state immediately before inference starts.
- [ ] Refuse to start when the governor is suspended.
- [ ] Monitor resource state during active generation.
- [ ] Cancel active generation on a stronger resource decision.
- [ ] Do not automatically retry the cancelled request at a smaller profile.
- [ ] Preserve RMA-134 drain/release/fault semantics.

## Phase 5 — Diagnostics and user-visible state

- [ ] Publish current governor mode, device profile, reason flags, and effective profile.
- [ ] Add governor details to the existing diagnostics provider output.
- [ ] Surface suspended/throttled local-LLM state through the main-screen state rather than displaying ordinary success.
- [ ] Include explicit thermal-unavailable labeling on API 28 and below.
- [ ] Ensure diagnostics contain no prompt/response content.

## Phase 6 — OOM and cancellation cleanup

- [ ] Catch local-inference `OutOfMemoryException` at the governed boundary.
- [ ] Cancel/drain/release any active generation before allowing recovery.
- [ ] Latch suspension after OOM and require healthy observations before recovery.
- [ ] Test OOM before native start.
- [ ] Test OOM during generation/monitoring.
- [ ] Test cancellation cleanup success.
- [ ] Test cleanup failure leaves the provider faulted/unavailable.

## Phase 7 — Validation and physical evidence

- [x] Add warnings-as-errors managed governor contract tests.
- [x] Add deterministic static Android/physics contract tests.
- [x] Add dedicated `RMA-135 Resource Governor` CI status workflow.
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
