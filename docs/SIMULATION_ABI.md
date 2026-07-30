# Reachy simulation C ABI

The public native boundary is defined by
`native/reachy_sim/include/reachy_sim.h`. The independently versioned ordered
state format is defined by `native/reachy_sim/include/reachy_sim_state.h`.
These are the only first-party native interfaces Unity or other language
bindings may call directly. C++ types, exceptions, standard-library containers,
MuJoCo types, and borrowed ownership do not cross this boundary.

The production MuJoCo implementation and its model, command, wrench, warning,
state, and snapshot semantics are specified in
[Production MuJoCo backend](architecture/PRODUCTION_MUJOCO_BACKEND.md). Handle
arbitration and caller-owned output rules are specified in
[Native handle concurrency and output ownership](architecture/NATIVE_HANDLE_CONCURRENCY.md).
The managed P/Invoke, layout, ownership, error, and threading rules are specified
in [Managed simulation interop contract](architecture/MANAGED_INTEROP.md).

## Versioning

Every fixed public structure starts with:

```c
uint32_t abi_version;
uint32_t struct_size;
```

Callers must initialize both fields before passing the structure to the library.
The library rejects mismatched versions and sizes with
`REACHY_SIM_STATUS_ABI_MISMATCH` or
`REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH`. Structure layouts are covered by
compile-time size assertions on supported 64-bit Android and desktop targets.

The public function and fixed-structure ABI is version `2`. The authoritative
state envelope has its own `REACHY_SIM_STATE_FORMAT_VERSION`, currently `1`, so
ordered state fields can evolve without silently changing `ReachySimStateHeader`
or the ABI-2 function signatures.

## Managed preflight

Android IL2CPP startup validates the managed process width, every fixed ABI and
authoritative-state structure size, critical field offsets, native library
availability, and the exact native ABI version before the first scene. A layout
or version mismatch fails startup visibly; simulation and presentation do not
continue through a mock, guessed packing, or cosmetic fallback. Hosted managed
tests inject an incompatible ABI value and require a typed fatal `AbiMismatch`
containing both version numbers.

## Handles and ownership

`ReachySimHandle` is an opaque 64-bit token, not a process pointer. Zero is always
invalid. The token contains an internal slot and generation so a handle used
after destruction is rejected as `REACHY_SIM_STATUS_STALE_HANDLE` rather than
dereferenced.

A successful `reachy_sim_create` transfers ownership of the backend instance to
the returned handle. `reachy_sim_destroy` releases it. Failed creation always
leaves the output handle equal to `REACHY_SIM_INVALID_HANDLE`.

Every handle-scoped public function is nonblocking and exclusive per handle. A
competing operation returns retryable `REACHY_SIM_STATUS_HANDLE_BUSY` before it
touches backend state. Destruction uses the same lease, so it cannot race reset,
step, command, copy, wrench, restore, capability, or error operations. Different
handles remain independent. Invalid and stale handles retain their more specific
statuses rather than being reported as busy.

## Backend selection and availability

Production MuJoCo is selected at build time with:

```text
REACHY_BUILD_MUJOCO_BACKEND=ON
REACHY_MUJOCO_INCLUDE_DIR=/path/to/pinned/mujoco/include
REACHY_MUJOCO_LIBRARY=/path/to/libmujoco.so
REACHY_MUJOCO_EXPECTED_VERSION=3.9.0
```

That build links `src/reachy_sim_backend_mujoco.c` and can emit the Android
`libreachy_sim.so` shared library. Builds without that option retain the explicit
unavailable backend. No cosmetic, kinematic, or test backend is selected
automatically. The deterministic fake backend is linked only into contract-test
targets and is never part of the production shared library.

## Model buffers

`ReachySimConfig.flags` can declare the create buffer as self-contained XML or
compiled MJB. Zero enables conservative format detection. Conflicting or unknown
flags fail.

The complete Reachy Mini package is multi-file, so Android tooling compiles its
pinned MJCF and assets to MJB with the same exact MuJoCo runtime before calling
the production ABI. Missing files or incompatible MJB data fail visibly.

## Commands

`reachy_sim_submit_commands` accepts one exact command batch:

```c
ReachySimCommandBatchHeader header;
ReachySimActuatorCommand commands[header.command_count];
```

The byte count must equal the header plus exactly `command_count` command
structures. Sequences are nonzero and monotonically increasing. The production
backend rejects malformed layouts, duplicates, non-finite controls, invalid
actuator IDs, nonzero reserved fields, and controls outside declared model
ranges. It does not partially apply a rejected batch or silently clamp values.

## Errors and recoverability

Every API returns a `ReachySimStatus` value.
`reachy_sim_status_recoverability` classifies whether a caller may retry, must
recreate a handle, must reload a model, or must correct configuration.

Creation errors are copied into an optional caller-provided
`ReachySimErrorInfo`. When supplied, that output must have initialized ABI and
size fields. Invalid metadata is rejected without mutating the output.
Handle-scoped failures are retained by the handle and copied with
`reachy_sim_get_last_error`; no borrowed error pointer is returned.

A rejected competing call returns `HANDLE_BUSY` directly and does not replace the
active operation's retained diagnostics. A MuJoCo numeric failure or warning is
never converted into success, silently discarded, or replaced by another
backend.

## Variable-size data and output ownership

State and snapshot copying use a bounded two-call contract:

1. Pass a non-null `required_size`.
2. Query with a null byte buffer and zero capacity, or supply an undersized
   buffer.
3. Receive `REACHY_SIM_STATUS_BUFFER_TOO_SMALL` and the exact required size.
4. Allocate that exact size and call again.

A null byte buffer with nonzero capacity is invalid. `required_size` changes only
on success or `BUFFER_TOO_SMALL`; busy, invalid, stale, and backend-error paths
leave it unchanged. Wrapper-side rejection and undersized-buffer paths do not
partially mutate caller output. Backends must report internally consistent sizes;
an impossible success or buffer-too-small result is converted to
`REACHY_SIM_STATUS_BACKEND_ERROR`.

Legacy state callers receive `ReachySimStateHeader`. Authoritative callers place
a valid `ReachySimStateRequest` in their buffer. State format 1 returns a checked
offset envelope containing:

- model hash, sequence, simulation time, and continuity identifier;
- ordered `qpos` and `qvel` arrays;
- canonical actuator observations;
- canonical non-world body positions and WXYZ quaternions;
- calibration identity, warning count, constraint counts, and maximum residuals.

Snapshot payloads remain backend-private, independently versioned,
model/configuration bound, and transactionally restored.

## Capabilities

`reachy_sim_get_capabilities` reports limits available without a live handle.
`reachy_sim_get_handle_capabilities` reports operations actually provided by the
selected backend and model. The production MuJoCo backend reports reset, step,
state, and snapshot; it additionally reports commands when actuators exist and
wrench when at least one non-world body exists.

## Contract tests

The native and managed suites verify:

- fixed C and managed structure layouts and C/C++ compatibility;
- ABI and structure-size mismatch rejection;
- injected managed startup rejection for an incompatible native ABI;
- initialized creation outputs and optional diagnostics;
- null, invalid, and undersized output-buffer behavior;
- exact legacy and authoritative state sizing;
- explicit unavailable-backend failure;
- native and managed create/destroy lifecycle stress, including 1,000 managed
  create/step/dispose cycles;
- stale-generation rejection and double-destroy behavior;
- exact command parsing, sequencing, duplicate rejection, and range validation;
- wrench validation and fixed-step duration;
- finite state and retained warning faults;
- model/configuration-bound snapshots, transactional restore, and replay;
- deterministic same-handle `HANDLE_BUSY` for every public operation;
- independent progress on another handle;
- unchanged outputs on busy and undersized paths;
- eight-thread contention with exact success/busy accounting.

The first-party contract runs under strict warnings-as-errors, ASan, and UBSan on
desktop CI. Pinned Android ARM64 cross-compilation, physical native probing,
Unity lifecycle acceptance, and authoritative rendering are production regression
gates. The Android preflight makes exact ARM64 IL2CPP managed layout validation a
startup requirement rather than an inference from later native calls.
