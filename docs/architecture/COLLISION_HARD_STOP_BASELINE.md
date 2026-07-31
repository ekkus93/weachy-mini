# RMA-065 collision and hard-stop baseline

## Status and scope

RMA-065 introduces an explicit generated MuJoCo profile rather than modifying the
pinned Reachy Mini source package. The authoritative source remains commit
`a739a6e461eb6d722901f1cfc225265ffc85c28d` with model SHA-256
`efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`.

The generated contract is `rma065_collision_hard_stop_v1`. It is an
**engineering-estimate** dynamics profile. None of its primitive dimensions,
contact thresholds, antenna stops, or solver settings is labeled measured,
fitted, or calibrated.

## Upstream audit

The permanent audit compiles the exact pinned model under MuJoCo 3.9.0 and
records both source and compiled geometry. The accepted upstream audit found:

- 169 compiled geoms, all meshes;
- only 8 active collision geoms;
- active colliders on only `body_down_3dprint` and `xl_330`;
- no active collider on the base, six actuator arms, six Stewart rods, or two
  antenna bodies;
- seven limited hinge joints: body yaw and the six Stewart actuators;
- no encoded antenna hard ranges;
- zero contacts and zero warnings over a 5,000-step neutral run;
- a 48.11 microsecond median and 61.235 microsecond p95 desktop step in the
  audit environment.

The audit tool is `scripts/audit_reachy_collision_model.py`. Its output is a
machine-readable baseline for generated-model coverage and performance
comparison.

## Generated collision geometry

`scripts/generate_reachy_collision_model.py` copies an imported pinned model
package and transforms only the copied XML. The generator validates source hash,
profile schema, evidence, body and actuator bindings, collision masks, and soft
versus hard range ordering before writing anything.

The profile adds named coarse primitives for:

- the fixed base shell;
- six actuator arms;
- six Stewart rods;
- the moving platform;
- a coarse head shell;
- the right and left antennas.

Existing upstream shell colliders are retained. All generated geoms are named
with an `rma065_` prefix and carry explicit contact parameters.

Collision masks intentionally separate three roles:

- shell: category 1, collides with moving and external objects;
- moving: category 2, collides with shell and external objects, but not every
  other moving primitive;
- external: category 4, collides with shell and moving objects.

This models the highest-value internal shell strikes without creating a dense
all-pairs moving-link contact graph. It is a reduced-order Android baseline, not
a claim of complete CAD collision fidelity.

## Mechanical hard stops and soft command limits

Hard stops are joint constraints. Soft command limits are actuator control
ranges and remain strictly inside the corresponding hard ranges.

- Body yaw and all six Stewart hard ranges retain the pinned source ranges.
- Their generated actuator control ranges are inset from those hard ranges.
- Both antenna joints gain explicit estimated hard ranges of `[-3.12, 3.12]`
  radians and soft command ranges of `[-3.05, 3.05]` radians.
- Every actuated hinge has explicit limit margin, `solreflimit`, and
  `solimplimit` values.

The production backend continues to reject commands outside the actuator soft
range with `REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR`. The MuJoCo joint constraint
independently prevents simulated state from crossing the wider hard range.

## Versioned dynamics diagnostics

The existing authoritative state format version 1 is unchanged in size and
layout. Requesting format version 2 returns a
`ReachySimDynamicsStatePayloadHeader` followed by the existing qpos, qvel,
actuator, and body arrays plus:

- one `ReachySimContactObservation` per active contact;
- one `ReachySimHardStopObservation` per limited scalar joint;
- geom and body pairs;
- contact point and normal;
- penetration;
- normal and tangential force;
- a per-step impulse estimate (`normal_force * timestep`);
- joint position, hard range, signed distance to the nearest stop, generalized
  limit force, and per-step impulse;
- aggregate overload and hard-stop event counts and maxima.

Contact overload and hard-stop events set explicit health flags. The generated
model stores its overload thresholds as named MuJoCo custom numerics, so runtime
diagnostics do not silently rely on unrelated hard-coded thresholds. Missing or
invalid numerics disable overload classification rather than inventing a value;
raw forces and impulses remain available.

## Validation strategy

The focused validation performs all of the following on the exact generated
model:

1. byte-deterministic generation and eight schema/failure tests;
2. pinned-source and generated-model collision inventories;
3. a neutral 5,000-step stability and hosted cost comparison;
4. a non-adjacent internal moving-to-shell contact fixture;
5. a world-to-head external contact fixture;
6. force, impulse, penetration, warning, and finite-state checks;
7. outward-velocity trials at the yaw and antenna upper hard stops;
8. fake-backend ABI/layout tests for state formats 1 and 2;
9. strict real-MuJoCo compilation and a production-backend state-v2 runner;
10. a physical Android ARM64 source-versus-enhanced benchmark.

The Android gate requires zero MuJoCo warnings, real-time factor at least 1.0,
penetration within the profile threshold, and p95 step overhead no greater than
35 percent on the representative project device. The threshold is a declared
engineering budget and must be revised from measured evidence rather than hidden
or relaxed after a failure.

## Known limitations

- Primitive dimensions and antenna stop positions require physical validation.
- Moving-to-moving collision pairs are intentionally omitted from the Android
  baseline.
- Contact impulse is a timestep-local estimate, not an integrated event impulse.
- Hard-stop force is generalized constraint force; hinge values are torque and
  slide values would be force.
- Deformable shells, cable drag, skin compliance, mesh wear, and detailed
  fastener contact are absent.
- The generated profile remains explicitly selected; it does not silently
  replace the pinned source model or a lower-fidelity production profile.
