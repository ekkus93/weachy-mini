# RMA-190 consolidation update — 2026-09-24

This update supplements `docs/RMA_190_CONSOLIDATION_STATUS_2026-09-24.md` after the duplicate-lane transplant pass. It does not close any RMA-190 checkbox on `master` by itself and does not replace TODO reconciliation on a mergeable branch.

## Canonical branch heads after transplant pass

- Core offline/MuJoCo/gesture evidence lane: PR #71, `ralph/rma190-core-evidence-reconcile-v2` at `4afbc0e84c34bf1a75246ad88629ce2714c4622c`.
- Front-camera requalification lane: PR #72, `ralph/rma190-camera-requalify` at docs head `3de12462923b2cae062eedd325ec372859034ea2`; physically validated source/test head `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`.
- Independent provider combinations lane: PR #44, `ralph/rma190-independent-provider-combinations` at `218845dded0f758f0d1a5ec8f6cd56b9e575c768`.
- Network/provider failure lane: PR #67, `ralph/rma190-network-failure-modes` at `ef3aba85008ee0a84cd68e2af4168ec4de8f6e87`.
- Model corruption/OOM lane: PR #68, `ralph/rma190-model-corruption-oom` at `6350208c5beaa8566c5240a8a077b5972f4a948a`.
- Solver fault/controlled-reset lane: PR #69, `ralph/rma190-solver-fault-reset` at `80dbb42102dc0ea92c011903106e9baa77df1aa6`.

## Coverage transplanted into canonical lanes

### PR #44 — independent provider combinations

The canonical provider-combinations lane now preserves the Unity/editor contract from `ralph/rma190-independent-provider-combinations` and the managed durable-settings/fallback-policy coverage from `ralph/rma190-provider-combinations`. The duplicate branch should not be advanced independently unless this canonical PR is invalidated.

### PR #67 — network/provider failure modes

The canonical network/provider failure lane now preserves:

- focused network failure-mode contracts from PR #67;
- managed VLM transport-boundary coverage from `ralph/rma190-network-failure-e2e`;
- managed LLM provider failure-injection coverage from `ralph/rma190-provider-failure-e2e`;
- VLM provider no-hidden-retry coverage from `ralph/rma190-provider-failures`, renamed under distinct file/class names so it coexists with the LLM provider harness;
- provider failure-matrix coverage from `ralph/rma190-provider-failure-matrix` / PR #50.

The branch also preserves the provider failure-injection spec text in `docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md` and a branch-local consolidation note at `docs/validation/RMA_190_NETWORK_FAILURE_CONSOLIDATION_2026-09-24.md`.

### PR #68 — model corruption/OOM

The canonical model/OOM lane now preserves the focused model-corruption source contract plus managed coverage from:

- `ralph/rma190-model-failure-e2e`;
- `ralph/rma190-local-model-failures`.

Its branch-local consolidation note is `docs/validation/RMA_190_MODEL_FAILURE_CONSOLIDATION_2026-09-24.md`.

### PR #69 — solver fault and controlled reset

The canonical solver lane now preserves the focused solver-fault source contract plus controlled native numeric-failure injection and managed controlled-reset coverage from `ralph/rma190-solver-fault-reset-e2e`.

`ralph/rma190-solver-recovery` is intentionally **not** folded into PR #69. It modifies shipping production runtime recovery behavior and should remain a separate production-runtime decision/review lane unless a later exact-head transplant is deliberately made.

## Physical A54 validation status

A temporary branch-only validation workflow was started from exact current `master` (`b188ce3b18635d24da79cb762e245c52e361ab5e`) while the A54 was attached:

- branch: `ralph/a54-rma190-e2e-20260924`
- head: `924c0f169f0b05cfe5ad782985bac1370171f137`
- workflow: `A54 RMA-190 Core And Camera E2E`
- run id: `36058331090`

That current-master workflow proved the A54 build/install/pin path and ran the core RMA-190 acceptance step successfully, but it could not close the camera checkbox because the combined workflow used `continue-on-error` and the camera lane still failed.

### Core offline/MuJoCo/gesture scenarios

The safe core evidence lane is PR #71, `ralph/rma190-core-evidence-reconcile-v2`. It records both the original canonical RMA-190 core physical pass and a later clean standalone A54 rerun:

- PR #71 head: `4afbc0e84c34bf1a75246ad88629ce2714c4622c`
- original canonical run: `35768977213`, exact source SHA `b188ce3b18635d24da79cb762e245c52e361ab5e`, result `success`
- standalone A54 rerun branch: `ralph/a54-rma190-core-20260925`
- standalone A54 rerun head: `b69ec73b3b8ad0196447a00bf791dd214dbaf7a9`
- standalone A54 rerun id: `36104854865`
- workflow: `A54 RMA-190 Core E2E`
- job: `RMA-190 core E2E on A54`
- result: `success`

The standalone A54 rerun changed only a branch-scoped workflow file and executed current `master` application source. It passed source-contract validation, Unity/Android resolution, generated presentation preparation, MuJoCo Android runtime staging, ARM64 APK build, APK verification, physical A54 pin, and `scripts/run_rma190_core_end_to_end_android.sh` acceptance. It was configured to print report files into the job log rather than upload artifacts because GitHub Actions artifact storage quota was exhausted.

### Front-camera transformed-frame scenario

The canonical front-camera lane was debugged while the A54 was available. The key diagnostic came from PR #72 head `c6840c799148c52bd5bb7496058f01e651d5186f`, physical run `36066778327`, and compact device report dump `36068367008`:

```text
last perception state=NoCameraFrame; message=Homography reprojection did not produce a frame: The camera texture and phone orientation are not timestamp-corresponding.; revision=2709
```

PR #72 then fixed the source-side timestamp-correspondence defect: the production perception driver now stamps the fail-closed identity phone-orientation sample with the source texture timestamp (`descriptor.TimestampNanoseconds`) instead of driver wall-clock time, with a source-contract guard in `scripts/tests/test_rma190_camera_end_to_end.py`.

The A54 was reconnected and the canonical PR #72 physical workflow passed for the exact source/test head containing that fix and hardened guard:

- physically validated source/test head: `c2d730aafe7ad9ce396f6f431c95563c8ac6bc83`
- workflow: `RMA-190 Front Camera End-to-End Android Acceptance`
- run id: `36104066566`
- job: `RMA-190 front camera E2E on Android`
- result: `success`

All required physical workflow steps passed: source-contract validation, Unity/Android resolution, generated Reachy Unity presentation preparation, production MuJoCo Android runtime staging, ARM64 API-26 APK build, APK verification, physical A54 pin, `scripts/run_rma190_camera_end_to_end_android.sh` front-camera acceptance, and evidence upload. The PR #72 docs head `3de12462923b2cae062eedd325ec372859034ea2` only records that evidence; it does not change app behavior beyond the physically validated source/test head.

## Remaining consolidation/audit work

The following lanes still require audit before they are closed, superseded, or transplanted:

- `ralph/rma190-failure-matrix` / PR #55;
- `ralph/rma190-failure-scenarios` / PR #63;
- `ralph/rma190-failure-modes` / PR #65;
- `ralph/rma190-solver-recovery` / PR #60.

No authoritative TODO checkbox should be marked complete on `master` until the relevant canonical PR is merged and its required exact-head CI/physical/provider/native/model evidence exists.
