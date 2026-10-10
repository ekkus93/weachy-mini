# RMA-194 deferred-feature and fidelity exact-head acceptance — 2026-10-09

## Scope

This record closes only the two RMA-194 final acceptance criteria that are
source/package/fidelity classifications:

- no deferred Level 2, Level 3, AR, movable-observer-camera, or VLA code is
  half-enabled in production; and
- the release-candidate fidelity label matches the strongest accepted evidence.

It does **not** accept the overall release and does not close RMA-002, RMA-125,
RMA-184, RMA-190, or the remaining `All incomplete items...` criterion.

## Exact-head evidence

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `8f0eaec3b6c2146e5968f1b1253567dbef49461a`
- Workflow: `RMA-190 Core End-to-End Android Acceptance`
- Run: `38020009514`
- Result: overall **failure only at physical-device pinning**

The following pre-device steps all passed on that exact SHA:

1. `Validate RMA-190 source contracts`, which also runs
   `scripts/tests/test_rma194_release_acceptance.py`,
   `scripts/tests/test_rma194_deferred_feature_and_fidelity_gate.py`, and
   `scripts/tests/test_rma194_release_acceptance_status.py`.
2. `Resolve local Unity and Android installations`.
3. `Prepare generated Reachy Unity presentation`.
4. `Stage production MuJoCo runtime for Unity Android`.
5. `Build ARM64 API-26 device APK`.
6. `Verify ARM64 API-26 device APK`.

The shared device-feasibility build script removes any stale APK before Unity
runs and then executes `scripts/verify_rma194_release_apk.py` against the newly
built shipping APK. The successful build step therefore proves that the package
audit passed on the exact source SHA. The audit rejects deferred AR/XR payloads,
unsafe/malformed APK structure, missing Unity data, and missing required
production ARM64 native libraries.

The later `Pin one physical Android device` step failed because the configured
Samsung A54 was not attached. That hardware availability condition occurs after
the source/package evidence above and is not a failure of these two RMA-194
criteria.

Hosted workflows at this time were also showing repository-wide zero-step
failures. Those are classified as GitHub Actions infrastructure failures, not
source failures, and are not substituted for the successful exact-head
self-hosted source/package evidence.

## Fidelity classification

`models/reachy-mini/model-parameter-audit.json` records:

- `profile_id=upstream_geometric_baseline`;
- `classification=geometric_baseline`;
- `calibrated=false`;
- `may_be_labeled_calibrated=false`; and
- `diagnostics_status=uncalibrated_upstream_baseline`.

No accepted measured/fitted/calibrated profile exists. The correct
release-candidate fidelity label is therefore:

**geometric baseline (uncalibrated)**

This classification is intentionally weaker than dynamic baseline,
servo-fidelity, or calibrated twin. Assigning it does not mean the release is
accepted or ready to tag.

## Acceptance result

The deferred-feature production boundary and evidence-accurate fidelity label
are accepted for RMA-194. The overall release gate remains open on the
independent-build, spoken-turn, representative-device-performance, and final
incomplete-item reconciliation requirements.
