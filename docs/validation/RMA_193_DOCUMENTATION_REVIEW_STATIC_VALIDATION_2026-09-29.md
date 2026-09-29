# RMA-193 Documentation Review Static Validation — 2026-09-29

## Scope

This document records the first permanent source-level regression gate for RMA-193 documentation review.

The gate does not close the full release documentation checklist by itself. It creates repository contracts that keep the current build, device, model, provider, calibration, fidelity, camera, troubleshooting, privacy, data-flow, asset-policy, and virtual-product-scope documentation from drifting or disappearing silently.

## Added permanent gate

New test file:

- `scripts/tests/test_rma193_documentation_review.py`

The file is intended to run through the existing static validation path:

```bash
python3 -m unittest discover -s scripts/tests -v
```

## Coverage

| RMA-193 area | Permanent source-level coverage |
| --- | --- |
| Build instructions | Requires README links and `docs/BUILDING.md` commands for toolchain verification, static CI, linters, native build, managed `dotnet run`, Unity Android development/release builds, IL2CPP, and ARM64. |
| Android device requirements | Requires the release operator guide's Android device requirements section and supported-device characteristics. |
| Model installation/import | Requires the operator guide's manifest-bound model installation/import section and explicit visible-unavailable semantics. |
| Provider configuration and BYOK warning | Requires cloud-provider BYOK, Android Keystore-backed credential storage, HTTPS/local-dev cleartext boundaries, and cloud LLM/VLM data-disclosure language. |
| Calibration workflow | Requires exact calibration selection, `NoCalibration`, visible unavailable/faulted states, and opt-in calibration experiment language. |
| Fidelity-level explanation | Requires release-label definitions for geometric baseline, dynamic baseline, servo-fidelity, and calibrated twin, plus evidence-matching language. |
| Camera Level 1 and missing pixels | Requires Level 1 camera reprojection, validity-mask, stale-content prevention, and insufficient-coverage rejection language. |
| Troubleshooting and diagnostic bundles | Requires diagnostics panel/export guidance, redacted bundles, raw-media/transcript/conversation-text exclusions, and recoverable-storage guidance. |
| Privacy and data-flow explanation | Requires offline/cloud data-flow distinction and visible-failure/no-silent-fallback language. |
| Virtual product scope | Requires the release guide to state that physical Reachy Mini hardware is not required for the virtual Android digital twin unless a separate calibration experiment says so. |
| Asset/sensitive-data policy | Requires `docs/ASSET_POLICY.md` to document pinned/provenance-controlled upstream imports and prohibit committed keys, model binaries, raw media, and raw calibration captures. |

## Current evidence boundary

This commit adds the review gate and this validation record. It does not by itself mark RMA-193 complete.

RMA-193 should remain open until an exact-head validation result confirms that static unittest discovery passed with this file present, and until the final release docs are reviewed against the selected release label/device matrix immediately before release.
