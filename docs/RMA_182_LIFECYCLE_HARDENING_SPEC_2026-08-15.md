# RMA-182 lifecycle hardening

## Scope

RMA-182 makes Android background/foreground transitions an explicit application lifecycle operation. It preserves the authoritative simulation state, releases or cancels transient resources, rejects new background work, and resumes into a defined idle interaction state without replaying elapsed wall-clock time.

## Invariants

1. **Simulation time is authoritative and discontinuous across an application pause.** `ReachySimulationWorker` clears its fixed-step accumulator and refreshes its monotonic timestamp at the pause boundary, while paused, and at resume. Background wall-clock time therefore cannot become physics catch-up work.
2. **One Unity application lifecycle ingress owns production pause/resume.** `ReachyApplicationHostBehaviour.OnApplicationPause` coordinates the application interruption state and CameraX acquisition. The production simulation runtime and CameraX acquisition expose explicit lifecycle methods instead of independently consuming Unity pause callbacks.
3. **Dependents pause before dependencies; dependencies resume before dependents.** `ReachyApplicationInterruptionCoordinator` uses a fixed service-kind order and reverses it for pause.
4. **Pause and resume are idempotent.** Repeated identical callbacks do not re-enter participant transitions. A completed cycle is counted only after a successful Paused -> Active transition.
5. **Cancellation generations are not resumable jobs.** `ReachyApplicationInterruptionGate` cancels the current generation on pause and allocates a fresh generation on resume. HTTP and local-LLM requests link to the generation token; cancelled requests are never replayed automatically.
6. **VLM leases are cancelled, not parked.** Lifecycle pause cancels active VLM leases and rejects new scheduling until resume. RMA-181 thermal/resource suspension remains independent and can continue blocking admission after lifecycle resume.
7. **Speech focus is released through the existing lease-finally contract.** Lifecycle pause cancels focus acquisition and interrupts an active speech session with `ApplicationBackgrounded`. Coordinated ASR/TTS observes that interruption, terminates, and releases its focus lease exactly once.
8. **Camera resources obey lifecycle and permission state.** Active CameraX acquisition is paused before application services; resume rechecks permission and either resumes the desired stream or stops and reports permission revocation.
9. **Conversation/UI never silently continue a turn.** Active conversation work is cancelled into a lifecycle-owned unavailable state. Resume converts only that lifecycle-owned state to Idle. Existing Error or unrelated Unavailable states are preserved. The main screen hides transient panels on pause and returns lifecycle-owned interaction state to Idle on resume.
10. **Failures fail closed.** A participant failure faults the interruption coordinator after all remaining pause participants have still received a quiesce attempt. A faulted coordinator does not resume partially quiesced work.

## Repeated-cycle behavior

Managed contracts execute five pause/resume cycles, including duplicate pause and duplicate resume calls within every cycle. They assert deterministic ordering, idempotence, cancellation-generation replacement, preserved error states, and monotonically increasing completed-cycle count.

## Out of scope

RMA-182 does not change the 500 Hz native physics timestep, skip physics steps, preserve or restart partially completed inference, or add provider fallback. It also does not claim physical Android lifecycle evidence in the local sandbox; representative-device validation remains a separate execution concern.
