# RMA-073 Parameter Fitting and Held-Out Validation

**Date:** 2026-07-31  
**Decision:** Implementation pending permanent workflow evidence

## Scope

This record covers strict training/held-out dataset separation, supported-only
parameter fitting, confidence/sensitivity reporting, signed and hashed candidate
profile manifests, and exact model/runtime compatibility rejection.

The committed evidence is synthetic. It validates infrastructure and known-value
recovery only. It does not represent physical Reachy Mini measurements and does
not authorize a calibrated profile label.

## Required automated evidence

The permanent workflow must prove:

- both generated datasets pass the complete RMA-070 validator;
- fitting and held-out roles are disjoint and hash-bound;
- held-out data cannot affect fitted values;
- friction, backlash, latency, controller, voltage, compliance, and thermal
  synthetic parameters are recovered within declared thresholds;
- missing evidence yields `unsupported` rather than a fabricated value;
- every fitted result reports confidence and sensitivity;
- canonical plan/profile hashes detect tampering;
- Ed25519 signing and verification work and wrong keys fail;
- calibrated claims are rejected by RMA-073;
- model, MuJoCo, ABI, and servo-contract incompatibilities fail closed;
- the generated candidate remains `calibrated=false` and
  `unapproved_fit_candidate`.

Exact accepted commit, workflow, artifact, and generated hashes are recorded
after the permanent gate passes.
