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

Every public operation on the same live handle participates in an exclusive
per-handle operation lease. If another operation is already active, the second
operation returns `REACHY_SIM_STATUS_HANDLE_BUSY` before it validates operation-
specific arguments, mutates backend state, or writes caller-owned output.
`reachy_sim_destroy` follows the same rule. Operations on independent handles
remain able to make progress concurrently. The authoritative simulation worker
still provides the normal single-owner call pattern, while the ABI enforces a
typed failure instead of permitting an unsafe race when a caller violates that
pattern.

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
memory. It does not return a borrowed pointer. The query participates in the
same per-handle operation lease as every other handle operation. If another
operation is active, it returns `REACHY_SIM_STATUS_HANDLE_BUSY` and leaves the
caller-owned error structure unchanged. A caller should query immediately after
a failed operation because a later sequential operation may replace the retained
last-error value.

A MuJoCo numeric failure or warning is never converted into success, silently
discarded, or replaced by another backend.

## Variable-size data

State and snapshot copying use a two-call bounded-buffer contract:

1. Call with a null buffer or insufficient capacity.
2. Receive `REACHY_SIM_STATUS_BUFFER_TOO_SMALL` and the exact required size.
3. Allocate that size and call again.

The implementation enforces maximum model, command, and snapshot sizes. Command
and snapshot headers include ABI version, structure size, sequence/identity
fields, and declared byte counts. Malformed buffers fail explicitly. Invalid,
undersized, and busy calls do not partially modify caller-owned buffers or size
outputs.

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
- null and undersized buffer handling without partial caller-output mutation;
- explicit unavailable-backend failure in non-production builds;
- create/destroy and 1,000-cycle lifecycle stress;
- stale-generation rejection and double-destroy behavior;
- exact command parsing, sequencing, duplicate rejection, and range validation;
- wrench validation and fixed-step duration;
- finite state and retained warning faults;
- state copy and fixed-step progression;
- model/configuration-bound snapshots;
- transactional restore and deterministic replay;
- last-error copying and recoverability classification;
- same-handle operation exclusion with typed `HANDLE_BUSY` results;
- unchanged caller outputs on busy results and concurrent progress on independent
  handles; and
- eight-thread contention where every attempt has exactly one typed result and
  the final sequence equals the number of successful serialized operations.

The command, state, wrench, snapshot, capability, and error parsers are covered
by a deterministic property-style contract matrix over version and structure
sizes, exact byte counts, null pointers, bounded capacities, sequences,
duplicate identifiers, reserved fields, finite values, ranges, stale handles,
and incompatible identities. A randomized fuzz target is not part of the
default CI because the current parsers are small, stateful, and already exercised
under strict deterministic invariants plus ASan/UBSan. A persistent fuzz corpus
may be added when it provides additional coverage that can be reproduced and
maintained in CI.

The first-party backend contract runs under strict warnings-as-errors flags and
ASan/UBSan on supported desktop CI. Fake MuJoCo validates the backend contract
only. The pinned real-runtime Android job is the build and physical execution
gate.
