# Authoritative simulation worker

RMA-032 uses a **managed-owned dedicated thread** implemented by
`ReachySimulationWorker`. Native MuJoCo remains the sole owner of mutable physics
state behind the C ABI, while managed code owns scheduling, command admission,
publication, lifecycle handshakes, and diagnostics.

## Why the worker is managed-owned

The managed layer already owns the deterministic `SafeHandle` lifetime and the
Android/Unity lifecycle. Keeping the scheduler in that layer avoids native-to-managed
callbacks, keeps Unity APIs off the physics thread, and makes pause, resume, reset,
and shutdown explicit synchronous requests with typed results. Native code never
invokes managed callbacks from the high-frequency path.

The Unity main thread is only a consumer. It may enqueue immutable command bytes and
read published snapshots; it never calls `mj_step`, mutates MuJoCo data, or advances
simulation from `Update`, `FixedUpdate`, or rendering callbacks.

## Fixed-step scheduling

The worker uses `Stopwatch.GetTimestamp()` as its monotonic clock and a 0.002-second
accumulator. Commands are drained only immediately before a one-step native call. A
cycle performs at most eight catch-up steps so a delayed worker cannot monopolize the
process indefinitely.

Pause and resume both reset the accumulator and monotonic baseline. Suspended wall
time is therefore excluded instead of generating a catch-up burst after resume. Reset
clears queued commands visibly, calls the selected native reset pose, republishes the
reset state, and restarts timing from a zero accumulator.

`DeadlineMissCount` records one miss for a scheduler cycle when either an individual
native step exceeds the 2 ms budget or backlog remains after the bounded catch-up
window. `AccumulatedLagSeconds` is the current positive fixed-step backlog. Last and
maximum native-step durations use the same monotonic clock.

## Commands and overflow

The command queue is bounded and preallocated. Enqueue validates the fixed header and
returns `Accepted`, `QueueFull`, `CommandTooLarge`, `InvalidFormat`, or
`WorkerUnavailable`. A full queue never overwrites an older or newer command. Overflow
and reset-discard counts are published in timing diagnostics. A native command failure
faults the worker visibly; it is not ignored or converted into a successful step.

## Immutable publication

The worker copies native state into a private unmanaged buffer and converts it to an
immutable managed value. A versioned triple buffer publishes the complete state and
timing snapshot. Readers retry if they overlap a write and never obtain a reference to
mutable MuJoCo memory. Slow, stalled, 30 Hz, and 60 Hz readers therefore affect only
which immutable snapshots they observe, not the simulation trajectory.

`ReachySimStateSnapshot.HealthFlags` preserves the exact native health bitset.
`SolverWarningCount` counts observed transitions into
`REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING`; the independent sleeping bit is not counted as
a solver warning, and a persistent warning is not recounted on every publication.

## Fault and lifecycle contract

Native step, command, reset, and state-copy failures become retained
`ReachySimulationFault` values and transition the worker to `Faulted`. Managed timing,
layout, and lifecycle failures are converted to typed managed-interoperability faults.
The worker remains available for an explicit shutdown request after a fault so the
native handle is still destroyed deterministically.

Pause, resume, reset, and shutdown each have one in-flight request identifier, a
completion result, and a caller deadline. A timed-out request remains visible and may
still complete; a second request receives a retryable busy result instead of silently
replacing it. Shutdown joins the dedicated thread and closes the native session.

## Acceptance coverage

Managed-native tests cover:

- progress independent of 60 Hz and 30 Hz reader cadence;
- continued valid trajectory after a deliberately stalled reader;
- command admission and visible bounded-queue overflow;
- pause stability and resume without suspended-time catch-up;
- reset queue discard and immutable reset publication;
- boundary-only command application and retained typed failure for a stale command;
- sleeping-health publication without a false solver-warning count;
- a controlled native step blocked beyond the 2 ms budget, proving step-duration and
  deadline-miss diagnostics;
- retained faults and deterministic shutdown through the production lifecycle gates.

Unity EditMode/PlayMode, ARM64 IL2CPP build, installed Android lifecycle acceptance,
and authoritative rendering acceptance remain the production integration gates.
