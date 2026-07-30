# Production MuJoCo backend

This document defines the first production `reachy_sim` backend that owns a real
MuJoCo model and data instance. It replaces the unavailable backend only in
builds configured with `REACHY_BUILD_MUJOCO_BACKEND=ON`. The normal desktop
contract build continues to use the explicit unavailable backend, and tests
continue to use an explicitly linked fake backend. No build selects a fake or
cosmetic implementation automatically.

## Pinned runtime

The backend is built against the pinned MuJoCo 3.9.0 source revision recorded in
`third_party/mujoco-source.lock.json`. Startup requires both of the following:

- `mj_version()` equals the compiled `mjVERSION_HEADER` value;
- `mj_versionString()` equals the configured exact version, currently `3.9.0`.

A mismatch returns `REACHY_SIM_STATUS_BACKEND_UNAVAILABLE`. The backend does not
attempt to load an MJB produced by another MuJoCo release.

## Model input contract

`reachy_sim_create` still accepts one bounded byte buffer. `ReachySimConfig.flags`
selects its format:

- `REACHY_SIM_CONFIG_FLAG_MODEL_XML` means a self-contained XML buffer;
- `REACHY_SIM_CONFIG_FLAG_MODEL_MJB` means a compiled MJB buffer;
- zero asks the backend to detect XML from the first non-whitespace `<` byte and
  otherwise treat the buffer as MJB.

The format flags are mutually exclusive and unknown flags are rejected.

The XML path uses an in-memory MuJoCo VFS containing exactly one XML file. It is
therefore intended for self-contained fixtures. It does not invent a filesystem,
ignore missing meshes, or rewrite unresolved include/asset paths. The official
Reachy Mini MJCF references external mesh assets, so production Android tooling
compiles the imported package to an MJB using the same pinned MuJoCo runtime and
passes those MJB bytes to `reachy_sim_create`.

The Android feasibility artifact contains:

- `libmujoco.so`;
- `libreachy_sim.so`;
- `reachy_mujoco_compile_runner`;
- `reachy_sim_backend_runner`;
- the existing constrained-model and reference-trace runners.

The physical-device script compiles the complete imported Reachy package to MJB,
reloads and verifies its dimensions, then exercises the public production ABI
against that MJB.

## Owned native state

One successful handle owns:

- one `mjModel`;
- one `mjData`;
- the exact model-byte identity hash;
- fixed-step sequence and command sequence state;
- pending finite-duration wrench state;
- warning counters and retained health flags;
- two preallocated MuJoCo integration-state buffers used for snapshots and
  transactional restore.

Destruction releases every owned allocation. MuJoCo pointers never cross the
public C ABI.

The public handle contract still requires operations on one handle to be
externally serialized. The managed authoritative simulation worker is the
single owner. Per-handle concurrent-operation rejection remains separate ABI
hardening work and must not be inferred from this backend implementation.

## Reset behavior

`REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE` calls `mj_resetData` and `mj_forward`.

`REACHY_SIM_RESET_POSE_SLEEP_REST` searches for a model keyframe named, in
order, `sleep_rest`, `sleep`, or `rest`. If none exists, it returns
`REACHY_SIM_STATUS_UNSUPPORTED`. It never fabricates a sleep pose from guessed
joint values.

A successful reset clears simulation sequence, command sequence, pending wrench,
and warning health state. A MuJoCo warning raised during reset is returned as a
backend error and retained in health flags.

## Commands

A command buffer is exactly:

```text
ReachySimCommandBatchHeader
ReachySimActuatorCommand[command_count]
```

Every batch and command carries ABI version and structure size. The backend
validates the entire batch before mutating `mjData.ctrl`, including:

- exact byte count;
- nonzero monotonically increasing batch sequence;
- configured command-count limit;
- actuator index bounds;
- finite control value;
- zero reserved fields;
- no duplicate actuator IDs;
- declared MuJoCo actuator control ranges.

Out-of-range values fail. They are not silently clamped. Controls remain active
until replaced by a later accepted batch, matching MuJoCo control semantics.

## Wrenches

A wrench must target a non-world body and contain finite force, torque,
application-point, and duration values. Duration zero means exactly the next
simulation step. A positive duration is rounded upward to a whole number of
fixed simulation steps. Submitting another wrench replaces the pending wrench
explicitly.

Before each step, the backend clears its owned `qfrc_applied` vector and applies
the pending wrench through `mj_applyFT`. It does not retain unrelated external
force values from another authority.

## Stepping and faults

The backend performs exactly the requested number of `mj_step` calls. After each
step it validates:

- finite simulation time;
- finite `qpos`, `qvel`, `qacc`, actuator state, and controls;
- finite world body positions and quaternions;
- no newly raised MuJoCo warning.

A numeric failure returns `REACHY_SIM_STATUS_NUMERIC_FAILURE`. A MuJoCo warning
returns `REACHY_SIM_STATUS_BACKEND_ERROR`, records the warning category/count,
and retains `REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING`. A warning-faulted state
cannot be snapshotted as a healthy continuation point.

## State ABI boundary

ABI version 2 still returns only `ReachySimStateHeader`. The production backend
fills real simulation time, sequence, non-world body count, joint count,
actuator count, contact count, and health flags.

Ordered `qpos`, `qvel`, actuator observations, and body-pose arrays are not added
silently to ABI version 2. Publishing those arrays requires the separately
versioned state-payload ABI and managed parser. Until that work is complete, the
Unity authoritative renderer must remain visibly unbound in production.

## Snapshots

The public snapshot envelope remains `ReachySimSnapshotHeader` format version 1.
Its backend-private payload records:

- private payload version and size;
- sequence and last accepted command sequence;
- reset identity and health flags;
- exact timestep and detected model format;
- pending wrench and remaining step count;
- MuJoCo integration-state signature and element count;
- the `mjSTATE_INTEGRATION` values.

Restore rejects mismatched model identity, calibration identity, timestep,
format, state size/signature, reset identity, warning-fault state, malformed
wrench state, non-finite values, or inconsistent simulation time.

Restore is transactional. The backend saves the current integration state before
applying a candidate snapshot. If candidate forward evaluation fails, warns, or
has inconsistent time, the previous state and warning statistics are restored.
A failed restore therefore does not become a partially applied live state.

## Validation boundary

The default native test suite compiles the backend against an expanded fake
MuJoCo contract and exercises failure paths under warnings-as-errors and
ASan/UBSan. This proves first-party parsing, ownership, sequencing, snapshot, and
fault behavior; it is not dynamics evidence.

The Android feasibility workflow provides the real-runtime gate by building the
backend against pinned MuJoCo, compiling the complete Reachy package to MJB on an
ARM64 device, and exercising create, capabilities, state, commands, wrench,
step, snapshot/restore/replay, reset, and destroy through the public ABI.
