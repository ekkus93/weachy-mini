# Simulation snapshots and deterministic reset

This document defines the persisted simulation snapshot and named-reset contract
for RMA-033. It complements the native ABI in
`native/reachy_sim/include/reachy_sim.h`, the managed immutable envelope in
`Assets/ReachyMini/Runtime/Interop/ReachySimSnapshot.cs`, and the authoritative
worker in `Assets/ReachyMini/Runtime/Simulation/ReachySimulationWorker.cs`.

## Version boundaries

Two independent versions are carried by the implementation:

- **Native C ABI version:** `2`. This version covers structures and function
  signatures crossing the C boundary.
- **Snapshot serialization format version:** `1`. This version covers the
  persisted envelope and backend payload interpretation.

A snapshot-format change does not automatically require an ABI change when the
public C structures remain unchanged. A public structure layout change does
require an ABI increment. Restore never guesses a version or performs a
best-effort migration.

## Named reset poses

The stable reset identifiers are:

| Identifier | Native value | Meaning |
|---|---:|---|
| `SleepRest` | `0` | A model-defined resting or sleeping state. |
| `NeutralAwake` | `1` | The model's neutral awake state. |

Unknown identifiers fail with `REACHY_SIM_STATUS_INVALID_ARGUMENT`. A successful
reset returns simulation sequence and simulation time to zero, clears accepted
command sequencing and pending wrench state, and creates a new authoritative
continuity epoch.

The implementation does not invent calibrated joint positions. `NeutralAwake`
uses the model's neutral reset state. `SleepRest` resolves only a model keyframe
named `sleep_rest`, `sleep`, or `rest`. The pinned official Reachy model does not
currently provide one, so production returns `REACHY_SIM_STATUS_UNSUPPORTED`
rather than fabricating a sleep pose. The deterministic contract backend includes
a synthetic test pose solely to exercise the stable identifier and sleeping
health flag.

## Common snapshot envelope

Every serialized snapshot starts with `ReachySimSnapshotHeader`:

| Field | Purpose |
|---|---|
| `abi_version` | Native ABI required to interpret the public header. |
| `struct_size` | Exact header size expected by that ABI. |
| `model_hash` | Opaque identity of the exact loaded model bytes. |
| `sequence` | Authoritative simulation step sequence at capture. |
| `simulation_time` | Authoritative simulation time at capture. |
| `payload_size` | Number of backend-specific bytes following the header. |
| `snapshot_version` | Snapshot serialization format version. |
| `calibration_profile_id` | Active calibration identity; zero means explicitly uncalibrated. |

The managed `ReachySimSnapshot` owns an immutable copy of the complete byte
sequence and exposes the envelope metadata for storage policy and diagnostics.
`ToArray()` returns a defensive copy. Callers do not parse or mutate the
backend-private payload.

## Production MuJoCo payload

The production payload contains a versioned `ReachyMujocoSnapshotPayloadHeader`
followed by the complete MuJoCo `mjSTATE_INTEGRATION` vector. The private header
binds the payload to:

- authoritative and last-command sequences;
- the selected reset pose and exact health flags;
- configured timestep and model format;
- pending finite-duration wrench state;
- integration-state count and MuJoCo state signature.

Capture is rejected for a MuJoCo-warning-faulted or non-finite state. The active
production profile is currently explicitly uncalibrated, so its calibration ID
is zero; a future calibrated profile must carry a distinct identity and cannot
silently accept an uncalibrated snapshot.

## Compatibility and transactional restore

Restore returns `REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE` when any required
identity or integrity check fails. Checks include:

- exact total byte length, ABI version, public header size, and payload size;
- snapshot and private-payload format versions;
- exact model hash and calibration-profile ID;
- configured timestep, model format, integration-state count, and state
  signature;
- finite nonnegative simulation time and agreement with restored MuJoCo time;
- matching public/private sequences;
- valid reset pose, health flags, reserved fields, and pending wrench;
- finite restored integration state with no MuJoCo warning after `mj_forward`.

The production backend saves the live integration state and warning statistics
before evaluating a candidate restore. If any post-load validation fails, it
restores the previous live state and warning data. A failed restore therefore
cannot partially alter the simulator. No failure is converted into reset,
model reload, best-effort import, or a cosmetic fallback.

## Authoritative worker ownership

Once `ReachySimulationWorker` owns a session, application code captures and
restores snapshots through worker control requests rather than calling the
session out of band.

- Capture and restore are accepted only while the worker is `Paused`.
- The dedicated simulation thread performs the native operation, preserving the
  single-owner rule and a stable step boundary.
- A successful capture returns an immutable `ReachySimSnapshot` in the typed
  control result.
- An incompatible restore returns its typed error, leaves the worker paused,
  publishes nothing, and preserves the prior live state and command queue.
- A successful restore discards queued future commands visibly, resets scheduler
  lag, publishes the restored immutable state, and remains paused until an
  explicit resume.
- Recreate-handle or fatal snapshot failures become retained worker faults;
  ordinary incompatibility remains a recoverable, nonfatal rejection.

Timed-out snapshot requests remain visible under the same one-request handshake
as pause, resume, reset, and shutdown. A second control request cannot silently
replace one that is still pending or in flight.

## Capture ownership and bounds

Session-level capture uses the ABI two-call bounded-buffer pattern:

1. Query the exact required byte count.
2. Allocate that exact managed buffer.
3. Copy the snapshot and require an unchanged returned size.
4. Validate the complete public envelope before constructing the immutable
   managed object.

Restore validates the managed envelope before invoking native restore and then
passes a private immutable byte copy. All native buffers remain caller-owned and
bounded; no borrowed snapshot pointer crosses the ABI.

## Deterministic replay contract

Replay tests use this sequence:

1. Reset or advance to a known state.
2. Apply a deterministic command or finite-duration wrench stream.
3. Capture a checkpoint.
4. Continue the same stream and capture the expected result.
5. Restore the checkpoint.
6. Replay the identical stream and recapture the result.
7. Compare state and serialized snapshot output.

For the deterministic contract backend and same-process production MuJoCo tests,
the tolerance is **zero**: final state structures and recaptured snapshot bytes
must be byte-identical. Production tests cover actuator commands, finite-duration
wrenches, sequencing, configuration identity, and transactional rejection.
The desktop and Android official-model runners independently require
`snapshot_replay_identical=true` with the pinned MuJoCo runtime and exact model.

Cross-platform model-integrity comparisons remain governed by the numeric
qpos/qvel/body-transform/constraint tolerances documented for RMA-042; those
floating-point tolerances do not weaken the same-runtime snapshot replay rule.

## Acceptance coverage

The permanent native and managed suites verify:

- both stable reset identifiers, including explicit production `UNSUPPORTED`
  when the official model lacks a sleep keyframe;
- explicit snapshot version, nonzero model identity, and calibration identity;
- model, version, calibration, timestep, payload, and state incompatibility;
- failed restore leaving live state byte-identical;
- command and wrench save/restore replay with zero-tolerance recapture;
- immutable managed snapshot bytes and defensive export copies;
- worker rejection while running, paused capture, nonfatal foreign-model
  rejection, visible queue discard on successful restore, restored-state
  publication, paused stability, and explicit resume.
