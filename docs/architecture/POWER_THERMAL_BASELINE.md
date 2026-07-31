# RMA-064 shared power and thermal baseline

## Scope

RMA-064 adds a native C++17 fleet-level `PowerThermalModel` above the RMA-061
`ServoModel` boundary. It can coordinate nine electrical or mechanical servo
models without changing the public C simulation ABI and is not selected in the
production MuJoCo path by this task.

The model owns one shared servo supply, one thermal state per actuator, common
current allocation, voltage-sag diagnostics, temperature derating, latched
shutdown, and explicit recovery rules.

## Shared supply

All nine channels use one bus. The committed noncalibrated baseline uses a 5 V
open-circuit estimate, 0.12 ohm source impedance, a 5 A shared current budget,
and the documented 3.7 V minimum servo voltage. The pinned hardware document
only specifies 6.8-7.6 V at the robot and power-board input; it does not identify
the regulated Dynamixel rail. The internal-bus values therefore remain visible
engineering estimates rather than hardware ratings.

Each frame:

1. all wrapped servo models are evaluated with the same previous-frame bus
   voltage, avoiding actuator-order dependence;
2. requested currents are aggregated across all nine channels;
3. a proportional common scale enforces the shared source-current budget;
4. a bounded fixed-point correction applies algebraic source sag without
   granting a same-frame voltage boost when load falls;
5. one final bus voltage and current total are exposed to every channel.

This operator split makes simultaneous high-load commands compete for one
finite source instead of receiving nine independent peak-current supplies.

## Thermal state

Each actuator has a lumped thermal state. Copper-loss heating is estimated from
`I^2 R`; cooling is linear to ambient through a thermal resistance; thermal
capacitance integrates temperature at the physics timestep. Winding resistance
is manufacturer-derived from the 6 V stall-current points used by RMA-062.
Thermal resistance and capacitance are role-specific engineering estimates.

Torque and current are linearly derated between warning and shutdown
temperature. At shutdown, torque and current become zero and the
over-temperature fault latches. Cooling does not re-enable the actuator.

## Explicit fault clear

`ClearThermalShutdown(index)` succeeds only when:

- the selected channel is actually latched;
- its most recent command has torque disabled;
- its internal temperature is at or below the role-specific recovery threshold;
- the model configuration is valid.

A successful clear performs an explicit safe reset of the wrapped channel,
removes the thermal bit, and preserves other observed fault bits. A hot or
torque-enabled channel must be cleared again later; no pending request silently
activates when conditions eventually become safe.

## Diagnostics

Every step returns:

- requested and delivered current per channel;
- shared bus voltage per channel;
- per-channel temperature, heating power, cooling power, and derating factor;
- thermal-latch and fault state;
- source open-circuit/evaluation/final voltage, voltage drop, aggregate
  requested/delivered current, current-limit scale, voltage scale, and
  undervoltage state.

## Parameter provenance and limitations

`models/reachy-mini/power-thermal-baseline.json` is the authoritative
`rma064_power_thermal_v1` contract. It pins the Reachy source/model identity and
the validated RMA-062 and RMA-063 commits. The deterministic generator rejects
unit drift, missing evidence, calibrated claims, invalid voltage or temperature
ordering, role-unsafe bindings, copied cross-role thermal vectors, winding
resistance drift, and a source current limit that does not actually constrain
aggregate peak demand.

No physical bus-voltage/current/temperature capture exists yet. The model does
not include battery state of charge, regulator efficiency, spatial heat flow,
fan or shell convection, sensor lag, shared electronics load, or calibrated
motor thermal constants. Those values must be replaced through later physical
identification rather than silently promoted.
