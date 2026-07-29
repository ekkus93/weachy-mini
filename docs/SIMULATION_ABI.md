# Reachy simulation C ABI

The public native boundary is defined by `native/reachy_sim/include/reachy_sim.h`.
It is the only first-party native interface that Unity or other language bindings may call directly.
C++ types, exceptions, standard-library containers, MuJoCo types, and borrowed ownership do not cross this boundary.

## Versioning

Every public structure starts with:

```c
uint32_t abi_version;
uint32_t struct_size;
```

Callers must set both fields before passing a structure into the library. The library rejects mismatched versions and sizes with `REACHY_SIM_STATUS_ABI_MISMATCH` or `REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH`. Structure layouts are covered by compile-time size assertions on the supported 64-bit Android and desktop targets.

The initial ABI version is `1`. Adding fields requires either a new ABI version or an explicitly documented size-compatible extension. Existing field meaning and ordering must not change within an ABI version.

## Handles and ownership

`ReachySimHandle` is an opaque 64-bit token, not a process pointer. Zero is always invalid. The token contains an internal slot and generation so a handle used after destruction is rejected as `REACHY_SIM_STATUS_STALE_HANDLE` rather than dereferenced.

A successful `reachy_sim_create` transfers ownership of the backend instance to the returned handle. `reachy_sim_destroy` releases it. Failed creation always leaves the output handle equal to `REACHY_SIM_INVALID_HANDLE`.

Destroying a handle while another operation is active returns `REACHY_SIM_STATUS_HANDLE_BUSY`. The owner must stop issuing operations and retry destruction. The current contract requires callers to serialize operations on the same handle. RMA-032 will provide the authoritative single-owner simulation thread that satisfies this requirement.

## Backend availability

The production scaffold currently links an explicit unavailable backend. Calling `reachy_sim_create` before the MuJoCo backend is integrated returns `REACHY_SIM_STATUS_BACKEND_UNAVAILABLE` with the diagnostic:

```text
MuJoCo backend is not linked; simulation startup is unavailable
```

No cosmetic, kinematic, or test backend is selected automatically. The deterministic fake backend is linked only into the native contract-test target and is never part of the production `reachy_sim` target.

## Errors and recoverability

Every API returns a `ReachySimStatus` value. `reachy_sim_status_recoverability` classifies whether a caller may retry, must recreate a handle, must reload a model, or must correct configuration.

Creation errors are copied into the caller-provided `ReachySimErrorInfo`. Handle-scoped failures are copied into internal handle state and retrieved with `reachy_sim_get_last_error`.

`reachy_sim_get_last_error` copies the full error structure into caller-owned memory. It does not return a borrowed pointer, so no message-lifetime dependency crosses the ABI. The copied message remains valid for as long as the caller retains the structure. Calls on the same handle must be externally serialized; concurrent calls may replace the handle's latest error before it is queried.

An error is never converted into success, silently discarded, or replaced with another provider/backend.

## Variable-size data

State and snapshot copying use a two-call bounded-buffer contract:

1. Call with a null buffer or insufficient capacity.
2. Receive `REACHY_SIM_STATUS_BUFFER_TOO_SMALL` and the exact required size.
3. Allocate that size and call again.

The implementation enforces maximum model, command, and snapshot sizes. Command and snapshot headers include their ABI version, structure size, sequence/identity fields, and declared byte counts. Malformed buffers fail explicitly.

## Capabilities

`reachy_sim_get_capabilities` reports limits and capabilities available without a live handle. `reachy_sim_get_handle_capabilities` reports operations provided by the selected backend.

The unavailable production scaffold reports no simulation-operation capabilities. The MuJoCo backend must advertise only operations it actually implements.

## Contract tests

The native suite verifies:

- fixed C structure layouts;
- C and C++ header compatibility;
- ABI and structure-size mismatch rejection;
- null and undersized buffer handling;
- explicit unavailable-backend failure;
- create/destroy and 1,000-cycle lifecycle stress;
- stale-generation rejection and double-destroy behavior;
- command sequencing and size validation;
- wrench validation;
- state copy and fixed-step progression;
- snapshot restore and model-hash mismatch rejection;
- last-error copying and recoverability classification.

The same suite runs under the repository's strict warnings-as-errors flags and under AddressSanitizer/UndefinedBehaviorSanitizer on supported desktop CI. The fake backend validates the ABI contract only; it is not evidence of MuJoCo dynamics or Android performance.
