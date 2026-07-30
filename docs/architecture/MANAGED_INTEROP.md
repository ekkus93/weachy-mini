# Managed simulation interop contract

## Scope

The sole managed-to-native entry point is
`Assets/ReachyMini/Runtime/Interop/NativeReachySim.cs`, compiled into the
`ReachyMini.Runtime` assembly. Application code does not declare additional
`DllImport` methods or call MuJoCo directly. The boundary uses C calling
conventions and mirrors only the versioned structures exported by
`native/reachy_sim/include/reachy_sim.h` and
`native/reachy_sim/include/reachy_sim_state.h`.

## Startup preflight

Android player startup runs `ReachySimAndroidInteropPreflight` before the first
scene. It performs two fail-closed checks:

1. `ReachySimManagedAbiContract.ValidateCurrentProcessLayout` verifies a 64-bit
   process, every fixed managed structure size, and critical field offsets for
   the ABI and authoritative-state envelope.
2. The preflight loads `libreachy_sim`, queries `reachy_sim_abi_version`, and
   rejects any value other than `ProjectMetadata.NativeAbiVersion`.

A failure throws before simulation or presentation startup. The error names the
managed and native ABI versions or the exact structure/field that differs. The
runtime does not continue with guessed packing, a mock backend, or cosmetic
animation.

`ReachySimSession.Create` independently repeats the process-width and native ABI
checks at session creation and returns a typed `AbiMismatch` or
`ManagedInteropFailure` result instead of starting a session.

## Layout contract

All fixed records use `StructLayout(LayoutKind.Sequential)`. The managed
preflight verifies these ABI sizes:

| Managed structure | Required bytes |
| --- | ---: |
| `NativeReachySimConfig` | 24 |
| `NativeReachySimCapabilities` | 40 |
| `NativeReachySimStateHeader` | 48 |
| `NativeReachySimCommandBatchHeader` | 24 |
| `NativeReachySimWrenchCommand` | 96 |
| `NativeReachySimSnapshotHeader` | 48 |
| `NativeReachySimErrorInfo` | 272 |
| `NativeReachySimStateRequest` | 24 |
| `NativeReachySimStatePayloadHeader` | 136 |
| `NativeReachySimActuatorObservation` | 40 |
| `NativeReachySimBodyPose` | 64 |

Critical offsets are checked for the configuration timestep, state simulation
time, snapshot calibration identifier, fixed error message, authoritative qpos
offset, and authoritative constraint residual. Desktop managed tests and Unity
EditMode tests retain independent size/offset assertions.

The Android preflight executes these checks inside the ARM64 IL2CPP player.
Physical lifecycle and authoritative-rendering acceptance therefore cannot reach
native session creation or pose publication unless the Android managed layouts
match the production ABI.

## Ownership and lifetime

The native handle is an opaque 64-bit generation-bearing token. Managed code
wraps it in `ReachySimSafeHandle`, which owns exactly one native handle and calls
`reachy_sim_destroy` from explicit close or final release. A successful explicit
close invalidates the safe handle, and subsequent session operations throw
`ObjectDisposedException` before crossing the native boundary.

`ReachySimSession` serializes handle operations with a private gate. This matches
the native per-handle exclusive lease and prevents managed disposal from racing
step, command, state, wrench, snapshot, reset, capability, or error operations.

## Error preservation

Native status, recoverability, and diagnostic text are converted to
`ReachySimError`. Creation uses caller-owned `ReachySimErrorInfo`; handle-scoped
operations copy the retained native error immediately after a failed call. If
the last-error query itself fails, the managed result preserves both the
original status and the query failure instead of replacing the operation with a
false success.

Interop loading failures (`DllNotFoundException`, `EntryPointNotFoundException`,
`BadImageFormatException`, and `MarshalDirectiveException`) become typed
`ManagedInteropFailure` results. No exception text, native status, or
recoverability classification is silently discarded.

## Threading

The P/Invoke assembly declares no managed callback or delegate entry point. The
authoritative worker calls the native API synchronously from its dedicated
simulation thread and publishes immutable managed snapshots. Native physics does
not invoke managed code from the high-frequency step path.

## Acceptance evidence

- The managed contract test injects an incompatible ABI value and requires a
  typed fatal `AbiMismatch` containing both version numbers.
- The same test validates all fixed sizes and critical offsets under the hosted
  managed warnings-as-errors gate.
- Managed native-lifecycle validation performs 1,000 create/step/dispose cycles
  through the real P/Invoke surface and test native library.
- Unity EditMode tests independently validate managed sizes and offsets.
- The physical Android lifecycle gate resolves the production ABI, observes a
  structured native initialization failure, creates/steps/destroys a valid
  session, and rejects operation after close.
- The physical authoritative-rendering gate parses and publishes the ordered
  production state envelope on ARM64 IL2CPP, providing end-to-end validation of
  the state request, payload header, actuator observation, and body-pose layouts.
