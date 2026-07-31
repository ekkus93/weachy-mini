# RMA-074 Physical Calibration Validation

**Date:** 2026-07-31  
**Status:** In progress — blocked by physical Reachy Mini connectivity

## Completion boundary

RMA-074 cannot be accepted from synthetic data or a simulator. Completion
requires a real Reachy Mini unit, separate physical fitting and held-out
datasets, a unit-specific fitted profile, the complete required held-out metric
report, accurate calibrated/uncalibrated labeling, and a redistributable
repository report.

No RMA-074 completion or calibrated-profile claim is made by this record.

## Read-only physical-unit gate

The permanent preflight gate targets the existing `kawa` self-hosted runner and
uses only HTTP GET requests. It checks daemon status, a physical hardware
identifier, current joints, body yaw, antenna positions, and head pose. It
rejects simulation, mock, unready, faulted, malformed, or non-finite responses.
The raw unit identifier and network host are never retained. The probe issues
zero motion commands and zero torque commands.

Workflow run `30672584330` executed on exact commit
`d937416d3a5b353142a60ee99714ef30ea8c9f71`:

- the hosted contract job passed all eight preflight tests;
- self-hosted runner `kawa` accepted and executed the physical job;
- no daemon was listening on `127.0.0.1:8000` (`connection refused`);
- `reachy-mini.local` did not resolve;
- repository variable `REACHY_MINI_HOST` was empty;
- the result was correctly recorded as `physical_unit_unavailable`;
- report SHA-256 was
  `3a3abcccf060c659e6b557b792367287e3963838ee716359f27511cba19cc72c`;
- artifact `8809421904`,
  `rma074-physical-preflight-d937416d3a5b353142a60ee99714ef30ea8c9f71`,
  has ZIP SHA-256
  `534cba39dc896a4411dcd56b1d51b4ef26aa2029047d11bb784f6f5e3a8e74f7`.

This is a connectivity blocker, not a failed calibration result: no physical
measurements were acquired and no robot was moved.

## Calibrated-profile approval contract

The repository now contains a separate RMA-074 approval layer rather than
weakening the RMA-073 fit-candidate contract. It requires:

- a verified RMA-073 candidate that remains unapproved before promotion;
- a successful read-only preflight for the same hashed unit identity;
- two separately captured physical fitting and held-out datasets;
- exact dataset hashes and distinct physical capture-run identifiers;
- joint-position, head-position, head-orientation, settling, overshoot,
  current, free-decay, and contact metric entries;
- passing core position/orientation/settling/overshoot/decay thresholds;
- explicit `unsupported` outcomes for unavailable instruments rather than
  fabricated measurements or mature claims;
- exact model, MuJoCo, simulator ABI, and servo-contract compatibility;
- canonical SHA-256 and Ed25519 approval signing with a non-fixture key;
- exact connected-unit identity before resolving the UI label to
  `Calibrated for this unit`.

Missing, invalid, incompatible, or unit-mismatched approval evidence resolves to
`Uncalibrated` with a diagnostic reason.

Hosted approval workflow run `30673242557` passed on exact commit
`e2e8c40a72620456c8bf8aab1cd4ee90a5203c08`:

- OpenSSL Ed25519 signing and verification passed;
- all eight approval and label tests passed;
- synthetic dataset IDs were rejected;
- reused fitting/held-out capture runs were rejected;
- failed core held-out metrics were rejected;
- the committed RMA-073 fixture signing key was rejected for approval;
- content tampering, compatibility mismatch, and unit mismatch failed closed;
- an absent approval resolved to `Uncalibrated` with process status 2;
- schema SHA-256 was
  `1bbdac52b91131b40678832e9b6714cd7e9cc20c1a25b05142bd7fcb63afa5b2`.

## Required remaining physical evidence

- make a real Reachy Mini daemon reachable from `kawa`, either locally or by
  configuring `REACHY_MINI_HOST`;
- obtain a successful read-only physical-unit artifact;
- review and install a production `ExperimentAdapter` with real current,
  voltage, temperature, fault, and emergency-stop safety sources;
- receive exact operator presence, workspace-clearance, emergency-stop, plan
  hash, unit identity, and physical-motion authorization inputs;
- capture separate physical fitting and held-out datasets;
- fit the unit-specific RMA-073 candidate;
- produce the complete physical held-out report;
- sign the RMA-074 approval with a non-fixture key;
- publish the approved unit profile and final redistributable report;
- complete application-level profile selection and labeling integration.

RMA-074 remains open until the physical evidence above exists and passes its
thresholds.
