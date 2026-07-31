# RMA-064 shared power and thermal baseline validation

**Status:** Complete  
**Validated commit:** `a9a8d4b172a8484cc01167051d5779ce892e6355`  
**Workflow:** `RMA-064 Power Thermal Baseline`  
**Successful run:** `30607760504`  
**Commit status:** `RMA-064 Power Thermal Baseline` — success

## Acceptance result

RMA-064 is accepted. The repository now contains a native, Unity-independent
fleet-level `PowerThermalModel` coordinating all nine servo channels through one
shared source and one thermal state per actuator. It can wrap the RMA-062
controller directly or through the RMA-063 mechanical decorator without changing
the public simulation C ABI.

The model is not silently selected in the production MuJoCo actuator path.
Committed source and thermal values remain manufacturer-derived quantities or
explicit engineering estimates, not calibrated Reachy Mini measurements.

## Shared supply and simultaneous load

`rma064_power_thermal_v1` defines one noncalibrated servo bus with:

- 5.0 V estimated open-circuit voltage;
- 0.12 ohm estimated shared source impedance;
- 5.0 A conservative shared current budget;
- 3.7 V manufacturer-derived minimum servo voltage.

The pinned hardware document specifies 6.8-7.6 V at the robot and power-board
input but does not identify the internal Dynamixel rail. The 5 V source,
impedance, and current budget are therefore visibly labeled engineering
estimates rather than inferred hardware ratings.

All channels are evaluated against the same previous-frame bus voltage. Current
requests are aggregated, one proportional scale enforces the source current
budget, and a bounded fixed-point correction applies algebraic voltage sag. No
actuator receives an independent peak-current source. The behavior test drives
nine simultaneous 1 A demands against a 3 A test supply and verifies aggregate
delivered current stays within the shared limit, every torque is reduced, and
all diagnostics report the same sagged voltage.

## Per-servo thermal state

Each channel integrates a lumped thermal model:

- heating: estimated copper loss `I^2 R`;
- cooling: linear heat flow to ambient;
- state: role-specific thermal capacitance;
- derating: linear from warning temperature to zero at shutdown;
- shutdown: latched zero torque/current with visible over-temperature fault.

Winding resistance is derived from the same 6 V stall-current evidence used by
RMA-062. Thermal resistance, capacitance, and recovery thresholds are distinct
body-yaw, Stewart, and antenna engineering estimates. The generator rejects an
identical full thermal vector copied between dissimilar roles.

## Fault-clear rules

Cooling never silently re-enables a channel. `ClearThermalShutdown(index)` is an
explicit operation and succeeds only when the channel is latched, its most
recent command has torque disabled, and its internal temperature is at or below
the role-specific recovery threshold. A successful clear performs a safe reset
of the wrapped channel, removes the thermal bit, and preserves other observed
fault bits. A failed clear does not remain pending for later automatic action.

The behavior suite verifies:

1. shutdown immediately removes torque and exposes `OverTemperature`;
2. clear is rejected while torque is enabled;
3. the channel cools while disabled but stays latched;
4. a later enabled command still produces zero torque without explicit clear;
5. a disabled, below-recovery explicit clear succeeds;
6. torque resumes only after that successful clear.

## Diagnostics

Each frame exposes per-channel requested/delivered current, shared voltage,
temperature, heating/cooling power, derating factor, shutdown latch, and fault
flags. Bus diagnostics expose open-circuit, evaluation, and final voltage,
source drop, aggregate requested/delivered current, current-limit and voltage
scales, and undervoltage state.

## Hosted validation

Run `30607760504` used Ubuntu 24.04, Python 3.11.15, and GNU C/C++ 13.3.0. It
passed:

1. byte-exact generated-header verification for `rma064_power_thermal_v1`;
2. eight Python schema and failure-path tests;
3. Unity-dependency rejection;
4. calibrated-claim rejection;
5. strict first-party warnings;
6. AddressSanitizer and UndefinedBehaviorSanitizer;
7. integrated compilation of the electrical, mechanical, and power/thermal
   servo-model sources;
8. the native suite for registry validation, role-safe lookup, shared-current
   limiting, source sag, common voltage diagnostics, temperature derating,
   latched shutdown, explicit clear, and fail-closed role mismatch.

CTest reported `1/1` passing tests and zero failures. The workflow published a
successful `RMA-064 Power Thermal Baseline` commit status on exact commit
`a9a8d4b172a8484cc01167051d5779ce892e6355`.

## Known limitations

- No physical Reachy Mini bus-voltage/current/temperature capture exists yet.
- Battery state of charge, regulator efficiency, source capacitance, shared
  compute/audio load, spatial heat flow, sensor lag, and enclosure convection
  are absent.
- Same-frame source correction is a deterministic reduced-order operator split;
  the wrapped controller receives the prior frame's common bus voltage.
- Thermal constants must be replaced through later physical identification.
- RMA-065 still owns collision geometry, contacts, overloads, and mechanical
  hard stops.

## Conclusion

RMA-064 satisfies its implementation and acceptance requirements. Simultaneous
high-load commands share one finite source and cannot obtain independent peak
torque, while thermal shutdown remains visible, latched, and explicitly cleared.
RMA-065 is the next ordered task.
