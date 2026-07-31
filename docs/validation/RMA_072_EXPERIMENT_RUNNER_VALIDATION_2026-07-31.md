# RMA-072 Experiment Runner Validation

**Date:** 2026-07-31  
**Validated implementation commit:** `de8b95eee5ffdae90c9409fa49887d3d603d6913`  
**Permanent implementation workflow run:** `30667664533`  
**Decision:** Accepted; synthetic orchestration evidence only

## Scope

This record covers the versioned experiment-plan contract, deterministic
schedule compiler, RMA-070 command-stream bridge, explicit physical adapter
boundary, and fail-closed safety behavior.

The committed smoke plan is synthetic. It does not claim that a physical
Reachy Mini was moved or measured. A production transport adapter and physical
data collection remain RMA-074 work.

## Versioned contract and experiment coverage

The accepted plan contract is
`rma072_calibration_experiment_plan_v1`. It binds the exact schema SHA-256,
canonical plan SHA-256, expected robot identity, hardware and firmware
constraints, monotonic command clock, command rate, actuator soft/profile
limits, duration/action ceilings, concurrency, and live voltage, current, and
temperature limits.

The committed smoke plan covers all eight required experiment families:

- unloaded sweep;
- gravity-loaded static pose;
- step response;
- frequency response;
- backlash reversal;
- free decay;
- multi-actuator command;
- thermal cycle.

It compiled to 347 actions over `23400000000` ns. The plan SHA-256 is
`780e43538fce5c2ad14357c1ea37a3ea4da90d9621dd610c1c19b981a23ddd8b`.
The deterministic schedule SHA-256 is
`96fa8b8131765b0f1d7c3ef61ba95c8038c5ee6cf52fcfd615902df776bfdcfd`.

## Safety and physical execution boundary

Physical motion is not exposed as a generic CLI mode. A caller must provide an
`ExperimentAdapter` plus authorization containing the exact plan hash and
robot identity, the exact acknowledgement
`RMA-072 PHYSICAL MOTION AUTHORIZED`, operator presence, workspace clearance,
emergency-stop verification, and explicit permission for physical motion.

The runner checks finite live bus voltage, maximum temperature, total current,
emergency-stop availability, and robot fault state before every action. Any
safety violation, unsupported action, adapter error, or timing-path exception
invokes emergency stop and records an aborted outcome. Free-decay release
explicitly disables torque, and final safe shutdown disables every actuator
used by the plan.

The operator procedure is committed at
`docs/operations/CALIBRATION_EXPERIMENT_SAFETY.md`.

## Automated validation

Workflow run `30667664533` ran on exact implementation commit
`de8b95eee5ffdae90c9409fa49887d3d603d6913` and passed:

- Python bytecode compilation;
- all 39 RMA-070, RMA-071, and RMA-072 calibration regression tests;
- duplicate-key, schema-drift, integrity, soft-limit, frequency, resource,
  authorization, robot-identity, safety-abort, and adapter-failure paths;
- deterministic compilation of every required experiment family;
- dry-run manifest, schedule, and command JSONL generation;
- import of the generated commands through RMA-071;
- final RMA-070 validation of one synchronized 312-sample command stream.

The generated RMA-070 dataset SHA-256 is
`138652fd99c1ccc54e081c6fca81260cf09681f35d4f144617751aaa5bdc035b`.

## Artifacts and integrity

Run `30667664533` published artifact `8807575014`, named
`rma072-experiment-runner-evidence-de8b95eee5ffdae90c9409fa49887d3d603d6913`,
with ZIP SHA-256
`be4aaea8262b240d10a57f6c75f63666a83eac5202c352657a60c921f2a9bb06`.

Staged-file SHA-256 values are:

- `commands.jsonl`:
  `6700617bf21e9d61abf7dd9b48e5642f06411bc51b170bf9b4b87889b395d490`;
- `run-manifest.json`:
  `bea99a0cb645d3860f4ff5e41696dfcb12dff17acdf6c16b9e0e86beffbc2557`;
- `schedule.json`:
  `96fa8b8131765b0f1d7c3ef61ba95c8038c5ee6cf52fcfd615902df776bfdcfd`;
- `rma070-command-dataset.json`:
  `5ae85473f31314ee11c62e78192a82fa30d5294e6c3e97c61d59fcbc3ec28fff`;
- `rma070-command-dataset-summary.json`:
  `07769af700e2ed845acaf0672e2b71af6cc78b8d2b56da3ba83ea7ba4b4de494`.

## Clean-tree policy

The temporary evidence-finalization workflow and script were removed before
this validation record was committed. The permanent
`RMA-072 Calibration Experiment Runner` workflow includes this record and the
authoritative TODO in its path gate, so the final documentation head is
revalidated with the same 39-test, dry-run, capture-bridge, and artifact checks.

## Acceptance conclusion

All RMA-072 implementation requirements are satisfied. The result is a
versioned and fail-closed experiment runner suitable for a future physical
Reachy adapter. No physical calibration or accuracy claim is made.
