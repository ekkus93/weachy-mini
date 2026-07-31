# RMA-073 Parameter Fitting and Held-Out Validation

**Date:** 2026-07-31  
**Implementation commit:** `6ae929809150732903e8e08a09b2ac244d883716`  
**Permanent workflow commit:** `44078b7a0826eed90beef3ec20a04b8919eff1c3`  
**Permanent workflow run:** `30670804228`  
**Decision:** Accepted as synthetic fitting infrastructure; not a calibrated profile

## Scope

This record covers strict training/held-out dataset separation, supported-only
parameter fitting, confidence and sensitivity reporting, signed and hashed
candidate profile manifests, and exact model/runtime compatibility rejection.

The accepted evidence is deterministic synthetic data. It validates the
infrastructure and recovery of known values only. It does not represent a
physical Reachy Mini measurement, approve the generated profile, or authorize a
calibrated profile label. Physical acquisition and approval remain RMA-074 work.

## Dataset split and integrity

The fit plan contract is `rma073_calibration_fit_plan_v1`. Each input is bound by
its RMA-070 canonical dataset SHA-256 and immutable `fitting` or `heldout` role.
Dataset identifiers, paths, and hashes cannot be reused across roles. Paths must
remain beneath the declared dataset root. All inputs must identify the same
robot, firmware/register configuration, and source clock contract.

The permanent run generated and independently validated:

- fitting dataset `rma073-synthetic-training-v1`: 2,000 samples, canonical
  dataset SHA-256
  `9c6f772a40797420f5d5383d40a366fffb5307eb0075c0e0a119c57551a24d0b`;
- held-out dataset `rma073-synthetic-heldout-v1`: 2,000 samples, canonical
  dataset SHA-256
  `6e7177286e9fe3c1eba4c466c4d9bd998a5e22d6ea95b3236e1027510f6fc3f7`;
- fit-plan canonical SHA-256
  `94f54bb39c01b819518a8148b1874432f7700c3bfe8896d6efb0d02d782e8f13`.

Each dataset contains 400 command, 500 joint, 600 current/load, 100 voltage,
and 400 temperature samples. The fitting stage loads only fitting-role data,
freezes every fitted result, and loads held-out data only afterward.

## Parameter recovery

All seven supported synthetic parameter families were fitted. These values are
known-value test evidence, not physical calibration constants:

| Family | Method | Accepted estimate |
|---|---|---|
| Friction | OLS Coulomb plus viscous | Coulomb `0.04000161310414374` N·m; viscous `0.005988331181272572` N·m·s/rad |
| Backlash | Bidirectional residual median | Half-width `0.0030004717906404535` rad |
| Latency | Matched transition median | `0.03` s |
| Controller | OLS PD controller | Position gain `2.400000768531358` N·m/rad; velocity gain `0.1799978813269117` N·m·s/rad |
| Voltage | OLS shared-bus sag | Open-circuit voltage `5.100000196262965` V; source impedance `0.12000000246279176` ohm |
| Compliance | Median load over deflection | Stiffness `29.99885501701056` N·m/rad |
| Thermal | OLS lumped thermal model | Heating `0.08000000000000375` °C/(A²·s); cooling `0.015000000000032333` 1/s |

Every fitted family reports its observation count, training residual or robust
spread, a dataset-qualified confidence label, and leave-one-out or robust
sensitivity. Missing required streams produce an explicit `unsupported` result
with no fitted value.

## Held-out validation

All seven supported families passed their independently computed held-out
thresholds:

| Family | Held-out metric | Value | Threshold |
|---|---|---:|---:|
| Friction | RMSE, N·m | `1.4129369940704228e-05` | `0.0002` |
| Backlash | Relative parameter error | `0.0001388997669547482` | `0.02` |
| Latency | Relative parameter error | `0.0` | `0.02` |
| Controller | RMSE, N·m | `2.8261915416197905e-05` | `0.0005` |
| Voltage | RMSE, V | `1.4131103436666891e-05` | `0.0002` |
| Compliance | Relative parameter error | `7.169313693174884e-05` | `0.02` |
| Thermal | Maximum relative parameter error | `1.1650402864648697e-12` | `0.02` |

The profile records `supported_family_count=7` and
`all_supported_passed=true`.

## Signed candidate profile

The output contract is `rma073_calibration_profile_manifest_v1`. It binds the
fit plan, fitting and held-out dataset hashes, pinned Reachy model, MuJoCo
version, simulator ABI, and RMA-061 through RMA-064 servo contracts.

The accepted generated candidate has:

- profile ID `rma073-synthetic-fit-candidate-v1`;
- canonical profile SHA-256
  `d4b7f423de8127b48a1374a517d8afda98605a22d2f2a812f7b6fee80b23059b`;
- signature algorithm `ed25519`;
- public-key SHA-256
  `9ba232b4c60858fe77ef79bf14d1392d089de797d8236ec737c46d494bfdc75c`;
- `calibrated=false`;
- approval state `unapproved_fit_candidate`.

The committed key pair is an explicitly non-secret synthetic test fixture.
RMA-073 refuses to sign a calibrated claim. Verification fails on profile
content drift, a wrong public key, canonical hash mismatch, or any exact model,
MuJoCo, ABI, or servo-contract incompatibility.

## Automated validation

Permanent workflow run `30670804228` checked out exact commit
`44078b7a0826eed90beef3ec20a04b8919eff1c3` and passed:

- OpenSSL 3 Ed25519 signing and verification;
- Python bytecode compilation;
- all 54 RMA-070 through RMA-073 calibration regression tests;
- exact pinned schema descriptors;
- complete RMA-070 validation of both isolated datasets;
- deterministic fitting, signing, and independent verification;
- held-out isolation and all seven held-out thresholds;
- dataset-hash, duplicate-key, path-traversal, schema-drift, unsupported-data,
  tampering, wrong-key, calibrated-claim, and compatibility failure paths.

## Artifact and file integrity

Run `30670804228` published artifact `8808687939`, named
`rma073-calibration-fitting-evidence-44078b7a0826eed90beef3ec20a04b8919eff1c3`,
with ZIP SHA-256
`dd20f830e1ac8364ab71e1100a55f07c3e377239c4ea255431a414d02ef89493`.

Important artifact file SHA-256 values are:

- `fit-plan.json`: `c385da805baa83110fc763195f1541bd5cde96408b8c530c31befd95558f06e5`;
- `training.json`: `3daa5284ce07e7493e735cf5f65085028dca7f54296d56579d508fc06f729c80`;
- `heldout.json`: `f7228b9ed9d50c0d8cb7682c83d0b9faffd2564ea2240fd68cedd128ab23da9c`;
- `profile.json`: `d8a38bfd94f6bfd223036e9ee780444edf77a8e692e0e5d9ec09414223846be4`;
- `profile-verification.json` and `independent-verification.json`:
  `99ffa078422a663cc29ff36b43d336822e573a3659a085f2198e6b76cfb65d15`;
- `synthetic-truth.json`:
  `9390fe2025a9dae2517c167d2fc24200220d7c6928e95c3264d8417fb8a52ed4`.

## Acceptance conclusion

All RMA-073 implementation requirements are satisfied. The repository now has a
fail-closed fitting and held-out-validation pipeline capable of producing an
integrity-bound, signed, unapproved candidate when supplied valid evidence.
RMA-074 must provide physical unit data, unit-specific fitting, human approval,
and the first profile that may legitimately be labeled calibrated.
