# RMA-072 Experiment Runner Validation

**Date:** 2026-07-31  
**Decision:** Implementation pending permanent workflow evidence

## Scope

This record covers the versioned experiment-plan contract, deterministic
schedule compiler, RMA-070 command-stream bridge, explicit physical adapter
boundary, and fail-closed safety behavior.

The committed smoke plan is synthetic. It does not claim that a physical
Reachy Mini was moved or measured.

## Required automated evidence

The permanent workflow must prove:

- strict plan parsing and canonical integrity verification;
- deterministic compilation;
- coverage of all eight required experiment families;
- explicit free-decay torque disable and final all-actuator torque shutdown;
- soft-limit, schema-drift, sampling, duration, and action-count rejection;
- exact robot, plan-hash, operator, workspace, and emergency-stop
  authorization gates;
- live voltage, current, temperature, emergency-stop, and fault checks before
  every physical-adapter action;
- emergency-stop and aborted-run retention on safety or adapter failure;
- dry-run CLI output;
- successful import of generated command JSONL through the RMA-071 capture
  tool and final RMA-070 dataset validation.

Physical unit execution remains deferred to RMA-074.
