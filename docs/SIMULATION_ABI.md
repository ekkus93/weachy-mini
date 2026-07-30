# Reachy simulation C ABI

The public native boundary is defined by `native/reachy_sim/include/reachy_sim.h`.
It is the only first-party native interface that Unity or other language bindings
may call directly. C++ types, exceptions, standard-library containers, MuJoCo
types, and borrowed ownership do not cross this boundary.

The production MuJoCo implementation and its model, command, wrench, warning,
and snapshot semantics are specified in
[Production MuJoCo backend](architecture/PRODUCTION_MUJOCO_BACKEND.md).

## Versioning

Every public structure starts with:

```c
uint32_t abi_version;
uint32_t struct_size;
```

Callers must set both fields before passing a structure into the library. The
library rejects mismatched versions and sizes with
`REACHY_SIM_STATUS_ABI_MISMATCH` or
`REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH`. Structure layouts are covered by
compile-time size assertions on supported 64-bit Android and desktop targets.

The public ABI is version `2`. Adding the actuator-command structure and defining
previously reserved configuration-flag meanings are additive changes: no
existing field meaning, offset, or structure size changed. Adding variable-size
state arrays will require an explicitly versioned state payload and corresponding
managed parser; those arrays are not smuggled into the ABI-2 header.

## Handles and ownership

`ReachySimHandle` is an opaque 64-bit token, not a process pointer. Zero is always
invalid. The token contains an internal slot and generation so a handle used
after destruction is rejected as `REACHY_SIM_STATUS_STALE_HANDLE` rather than
dereferenced.

A successful `reachy_sim_create` transfers ownership of the backend instance to
the returned handle. `reachy_sim_destroy` releases it. Failed creation always
leaves the output handle equal to `REACHY_SIM_INVALID_HANDLE`.

Destroying a handle while another operation is active returns
`REACHY_SIM_STATUS_HANDLE_BUSY`. Callers must serialize all operations on the
same handle. The authoritative simulation worker provides that single-owner
contract. General simultaneous operation rejection remains open hardening work.

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
unavailable backend and return:

```text
MuJoCo backend is not linked; simulation startup is unavailable
```

No cosmetic, kinematic, or test backend is selected automatically. The
deterministic fake backend is linked only into contract-test targets and is never
part of the production shared library.

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

Creation errors are copied into caller-provided `ReachySimErrorInfo`.
Handle-scoped failures are copied into internal handle state and retrieved with
`reachy_sim_get_last_error`.

`reachy_sim_get_last_error` copies the full error structure into caller-owned
memory. It does not return a borrowed pointer. Calls on the same handle must be
externally serialized; concurrent calls could otherwise replace the latest error
before it is queried.

A MuJoCo numeric failure or warning is never converted into success, silently
discarded, or replaced by another backend.

## Variable-size data

State and snapshot copying use a two-call bounded-buffer contract:

1. Call with a null buffer or insufficient capacity.
2. Receive `REACHY_SIM_STATUS_BUFFER_TOO_SMALL` and the exact required size.
3. Allocate that size and call again.

The implementation enforces maximum model, command, and snapshot sizes. Command
and snapshot headers include ABI version, structure size, sequence/identity
fields, and declared byte counts. Malformed buffers fail explicitly.

ABI-2 production state is currently `ReachySimStateHeader` only. It reports real
counts and health but not ordered simulation arrays. Snapshot payloads are
backend-private, versioned, model/configuration bound, and transactionally
restored.

## Capabilities

`reachy_sim_get_capabilities` reports limits available without a live handle.
`reachy_sim_get_handle_capabilities` reports operations actually provided by the
selected backend and model. The production MuJoCo backend reports reset, step,
state, and snapshot; it additionally reports commands when actuators exist and
wrench when at least one non-world body exists.

The unavailable scaffold reports no simulation-operation capabilities.

## Contract tests

The native suite verifies:

- fixed C structure layouts and C/C++ compatibility;
- ABI and structure-size mismatch rejection;
- null and undersized buffer handling;
- explicit unavailable-backend failure in non-production builds;
- create/destroy and 1,000-cycle lifecycle stress;
- stale-generation rejection and double-destroy behavior;
- exact command parsing, sequencing, duplicate rejection, and range validation;
- wrench validation and fixed-step duration;
- finite state and retained warning faults;
- state copy and fixed-step progression;
- model/configuration-bound snapshots;
- transactional restore and deterministic replay;
- last-error copying and recoverability classification.

The first-party backend contract runs under strict warnings-as-errors flags and
ASan/UBSan on supported desktop CI. Fake MuJoCo validates the backend contract
only. The pinned real-runtime Android job is the build and physical execution
gate.
