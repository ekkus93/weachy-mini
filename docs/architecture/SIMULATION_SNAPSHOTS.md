# Simulation snapshots and deterministic reset

This document defines the persisted simulation snapshot contract introduced by RMA-033. It complements the native ABI in `native/reachy_sim/include/reachy_sim.h` and the managed API in `Assets/ReachyMini/Runtime/Interop/ReachySimSnapshot.cs`.

## Version boundaries

Two independent versions are carried by the implementation:

- **Native C ABI version:** `2`. This version covers the layout and meaning of structures crossing the C boundary.
- **Snapshot serialization format version:** `1`. This version covers the persisted snapshot envelope and backend payload interpretation.

A snapshot-format change does not automatically require an ABI change when the public C structures remain unchanged. A public structure layout change does require an ABI increment.

## Named reset poses

The stable reset identifiers are:

| Identifier | Native value | Meaning |
|---|---:|---|
| `SleepRest` | `0` | Resting/sleep state. The state health flags include `REACHY_SIM_HEALTH_FLAG_SLEEPING`. |
| `NeutralAwake` | `1` | Neutral awake state with the sleeping flag cleared. |

Unknown reset identifiers fail with `REACHY_SIM_STATUS_INVALID_ARGUMENT`. A reset returns simulation sequence and simulation time to zero and clears backend command sequencing state.

The contract does not invent calibrated joint positions. The production MuJoCo backend must bind these names to model-derived or measured poses during the Reachy model and calibration phases.

## Common snapshot envelope

Every serialized snapshot starts with `ReachySimSnapshotHeader`:

| Field | Purpose |
|---|---|
| `abi_version` | Native ABI required to interpret the public header. |
| `struct_size` | Exact header size expected by the current ABI. |
| `model_hash` | Opaque identity of the loaded model bytes. |
| `sequence` | Authoritative simulation step sequence at capture. |
| `simulation_time` | Authoritative simulation time at capture. |
| `payload_size` | Number of backend-specific bytes following the header. |
| `snapshot_version` | Snapshot serialization format version. |
| `calibration_profile_id` | Identity of the active calibration profile; zero means explicitly uncalibrated. |

The payload after this header is backend-specific. Callers must treat snapshot bytes as opaque and use the snapshot metadata exposed by the managed wrapper for display, storage policy, and diagnostics only.

The test contract backend currently uses a 64-byte payload containing its complete state header, last accepted command sequence, and named reset-pose identifier. Its model hash is a deterministic 64-bit FNV-1a hash of the supplied model bytes. That hash algorithm and payload are test-backend details, not a promised production storage format.

## Compatibility and failure behavior

Restore returns `REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE` without changing active backend state when any required compatibility check fails. The current contract checks include:

- exact total byte length;
- matching native ABI version and header size;
- matching snapshot format version;
- matching payload size;
- matching model hash;
- matching calibration-profile ID;
- finite, nonnegative simulation time;
- payload/header sequence and simulation-time agreement;
- valid named reset pose;
- valid state structure version and size;
- reserved fields equal to zero;
- health flags consistent with the named reset pose.

The managed `ReachySimSession` performs envelope validation before invoking native restore and returns typed error information. `ReachySimSnapshot` owns an immutable copy of the bytes and returns defensive copies through `ToArray()`.

No compatibility failure is converted into a reset, model reload, best-effort import, or silent fallback.

## Capture and restore ownership

Snapshot capture uses a two-call bounded-buffer pattern:

1. Query the required byte count.
2. Allocate the exact managed buffer and copy the snapshot.
3. Validate the returned size and header before constructing `ReachySimSnapshot`.

Restore passes an immutable copy of the snapshot bytes to the native session. Callers must serialize capture/restore with simulation ownership. A future worker-level persistence API must execute these operations at a stopped or paused authoritative step boundary; it must not mutate the simulator concurrently with stepping.

## Deterministic replay evidence

The native contract test and managed native-lifecycle test perform the following sequence:

1. Reset to a named pose.
2. Submit a command sequence and advance to a known state.
3. Capture a snapshot.
4. Advance and mutate command sequencing.
5. Restore the snapshot.
6. Replay the same command/step stream.
7. Compare the resulting state and recaptured snapshot.

For the deterministic test contract backend, the documented tolerance is **zero**: state fields and serialized snapshot bytes must match exactly. Hosted native warnings-as-errors, ASan/UBSan, and managed analyzer/lifecycle jobs passed this replay coverage on commit `28ec16bd5fb3ef7c68dd6f35192deb97c36d4e9b`.

The production MuJoCo backend is not yet linked. RMA-040 and RMA-042 must define the production payload, model identity, calibration identity, and floating-point replay tolerances, then run equivalent desktop/Android reference tests. The test-only backend evidence must not be presented as physical-model fidelity evidence.
