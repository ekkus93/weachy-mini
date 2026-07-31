# Calibration parameter fitting and held-out validation

## Scope

RMA-073 provides deterministic infrastructure for converting validated RMA-070
datasets into an **unapproved fit candidate**. It does not acquire physical data
and it does not promote a candidate to a calibrated profile. Physical collection,
unit-specific acceptance, and the calibrated label remain RMA-074 responsibilities.

The implementation is host-side Python and has no Unity dependency. The main
entry points are:

- `scripts/generate_rma073_synthetic_data.py` — deterministic synthetic training
  and held-out RMA-070 datasets used only to validate the fitting infrastructure;
- `scripts/fit_calibration_profile.py` — fit supported parameter families,
  evaluate frozen parameters on held-out data, and sign the candidate manifest;
- `scripts/verify_calibration_profile.py` — verify canonical integrity, Ed25519
  signature, and exact model/runtime compatibility;
- `scripts/calibration_fitting.py` — strict contracts, estimators, validation,
  signing, and verification.

## Data split and leakage boundary

A versioned `rma073_calibration_fit_plan_v1` document binds every dataset by:

- dataset ID;
- canonical RMA-070 dataset SHA-256;
- safe path relative to an explicit dataset root;
- immutable role: `fitting` or `heldout`.

At least one dataset of each role is mandatory. Dataset IDs, paths, and hashes
must be unique. All datasets must describe the same robot identity, hardware,
firmware, and register-configuration hash. Absolute paths, parent traversal,
and paths resolving outside the supplied root fail closed.

The fitter loads only `fitting` datasets while producing parameters. It deep
copies and freezes those results before any held-out evaluator runs. Held-out
data can change validation metrics, but cannot change fitted values.

## Parameter evidence matrix

The tool fits only when the required streams and a minimum number of usable
observations exist. Otherwise the family is emitted as `unsupported` with an
explicit reason and empty values.

| Family | Required evidence | Estimator |
| --- | --- | --- |
| Friction | nonzero-velocity joint torque | Coulomb-plus-viscous least squares |
| Backlash | bidirectional command/joint reversals | direction-conditioned residual median |
| Latency | command steps and matching joint transitions | median transition delay |
| Controller | aligned target, position, velocity, torque | bounded PD least squares |
| Voltage | aligned bus voltage and current | open-circuit voltage/source-impedance least squares |
| Compliance | aligned command, position, and estimated load | median load/deflection ratio |
| Thermal | current, temperature, ambient, monotonic time | lumped heating/cooling least squares |

Every fitted family includes observation count, training error or robust spread,
a dataset-qualified confidence label, and a sensitivity measure. Confidence is
about the supplied dataset and estimator conditioning; it is not a general
accuracy claim.

## Held-out validation

The plan supplies one threshold per parameter family. Predictive families use
held-out RMSE. Scalar-identification families compare the frozen fitting value
against an independently estimated held-out value using relative error. The
manifest records each metric, threshold, sample count, and pass/fail outcome.
Unsupported families remain unsupported; they are never treated as passing.

A passing synthetic profile proves only that the fitting machinery can recover
known synthetic parameters without training/validation leakage.

## Profile manifest and signature

The output contract is `rma073_calibration_profile_manifest_v1`. It contains:

- exact fit-plan ID and hash;
- exact fitting and held-out dataset hashes and roles;
- compatibility tuple;
- fitted or unsupported results for all seven families;
- held-out metrics and thresholds;
- canonical profile SHA-256;
- Ed25519 signature, public-key ID, and public-key-file SHA-256.

RMA-073 enforces:

```text
profile_kind = fit_candidate_unapproved
calibrated = false
approval_state = unapproved_fit_candidate
```

The signer rejects any attempt to set `calibrated=true`. The committed private
key is an explicitly non-secret test fixture used only for automated synthetic
evidence. Production/private signing keys must not be committed.

## Compatibility gate

A profile is loadable only when all fields match exactly:

- pinned Reachy source commit;
- pinned MJCF SHA-256;
- MuJoCo version;
- simulation ABI version;
- RMA-061 servo contract;
- RMA-062 electrical/controller contract;
- RMA-063 mechanical-effects contract;
- RMA-064 power/thermal contract.

Hash or signature validity never overrides a compatibility mismatch.

## Failure behavior

The implementation rejects duplicate JSON keys, non-finite values, schema drift,
plan/profile integrity drift, duplicate split membership, robot-identity drift,
unsafe paths, missing signatures, wrong public keys, and runtime/model mismatch.
No unsupported parameter is synthesized from a baseline estimate.
