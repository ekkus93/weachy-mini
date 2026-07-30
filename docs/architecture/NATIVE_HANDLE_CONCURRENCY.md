# Native handle concurrency and output ownership

This document defines the RMA-030 concurrency contract for the public
`reachy_sim` C ABI. It supplements [Reachy simulation C ABI](../SIMULATION_ABI.md).
The public ABI remains version 2; this hardening changes operation arbitration,
not structure layouts or function signatures.

## One operation per handle

Every handle-scoped public function acquires a nonblocking operation lease keyed
by the complete generation-bearing `ReachySimHandle` token. Exactly one lease may
be active for a handle at a time. A competing call returns
`REACHY_SIM_STATUS_HANDLE_BUSY`, whose recoverability is `RETRY`, before it
accesses backend state.

The lease covers:

- capability and last-error copies;
- reset and fixed stepping;
- actuator command submission;
- state and snapshot copies;
- wrench application;
- snapshot restoration;
- destruction.

The library does not wait for a handle to become available. Callers choose their
own retry, backoff, cancellation, or fail-visible policy. Operations on different
handles remain independent. The short global operation-table lock protects only
lease bookkeeping; no backend or MuJoCo call runs while that lock is held.

Syntactically invalid handles return `INVALID_HANDLE`, and destroyed or replaced
generations return `STALE_HANDLE`. Those specific statuses take precedence over
contention. The full token, including its generation, is the lease key, so a new
handle that reuses an internal slot is not blocked by an older generation that is
finishing destruction.

## Destruction arbitration

`reachy_sim_destroy` uses the same exclusive lease as every other handle-scoped
operation. If an operation owns the lease, destruction returns `HANDLE_BUSY` and
does not partially tear down the backend. If destruction owns the lease, later
calls return `HANDLE_BUSY` until the slot is invalidated. After invalidation they return `STALE_HANDLE`, including
while the already-owned backend destructor finishes; they cannot acquire or
access that backend instance.

The original slot lease and active-call count remain in place as a second lifetime
barrier. Public operation serialization prevents simultaneous mutable backend
access; the slot barrier prevents a backend instance from being freed while a
validated internal call is active.

## Error ownership

A competing `HANDLE_BUSY` result is returned directly and is not written into the
handle's retained last-error structure. This prevents a rejected observer from
replacing diagnostics owned by the operation that actually reached the backend.
`reachy_sim_get_last_error` is itself serialized and copies caller-owned data; it
never returns a borrowed pointer.

Creation has no published handle to lease. When a caller supplies
`ReachySimErrorInfo`, it must initialize `abi_version` and `struct_size` before
calling `reachy_sim_create`. A null error output is permitted. Invalid output
metadata is rejected without mutating that output, and every failed create leaves
`out_handle` equal to `REACHY_SIM_INVALID_HANDLE`.

## Output-buffer contract

`reachy_sim_copy_state` and `reachy_sim_copy_snapshot` use a bounded two-call
contract:

1. `required_size` must be non-null.
2. A null byte buffer is valid only when capacity is zero.
3. A size query or undersized buffer returns `BUFFER_TOO_SMALL` and the exact
   required size.
4. A successful copy reports the exact number of bytes written.
5. `required_size` is not changed for `HANDLE_BUSY`, invalid arguments, invalid or
   stale handles, or backend failures.
6. Wrapper-side rejection and `BUFFER_TOO_SMALL` paths do not partially mutate the
   caller's byte buffer.

Backends must report a nonzero required size on success or `BUFFER_TOO_SMALL` and
must not claim success when capacity is insufficient. The public wrapper converts
an internally inconsistent copy result to `BACKEND_ERROR`.

Versioned fixed-size output structures, including capabilities and error
information, must be initialized by the caller. A rejected or contended call does
not modify them.

## Tests

The native contract target retains the complete pre-hardening suite and adds a
deterministically blocking fake-backend step plus adversarial RMA-030 tests. The
suite verifies:

- every same-handle operation returns `HANDLE_BUSY` while a step owns the lease;
- destruction cannot race an active operation;
- another handle continues to make progress;
- busy state, snapshot, capability, and error copies leave outputs unchanged;
- null/nonzero and missing-size output combinations fail without mutation;
- state and snapshot two-call sizes are exact and backend-defined;
- uninitialized create-error outputs are rejected without mutation;
- eight threads performing 16,000 step attempts receive only `OK` or
  `HANDLE_BUSY`;
- final sequence equals the exact number of successful serialized steps;
- stale generations and 1,000-cycle lifecycle behavior remain intact.

The target runs under first-party warnings-as-errors, AddressSanitizer, and
UndefinedBehaviorSanitizer. Production ARM64 cross-compilation, Unity lifecycle,
physical-device native probing, and authoritative rendering remain regression
gates for the same exact head.
