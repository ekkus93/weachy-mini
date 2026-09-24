# RMA-190 consolidation status — 2026-09-24

## Purpose

RMA-190 has accumulated many parallel Ralph branches and pull requests. This document
reduces the work back to one canonical lane per roadmap scenario, identifies duplicate
or stale lanes, and prevents additional branch churn while GitHub Actions is failing
jobs before any steps or logs are available.

Authoritative checklist: `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.
Current `master` baseline at the time of this consolidation:
`b188ce3b18635d24da79cb762e245c52e361ab5e`.

This document does **not** close any RMA-190 checkbox. It is a triage/control document.
Scenario checkboxes remain open until the required exact-head CI, physical Android,
provider, native, model-package, and artifact evidence exists and is merged.

## Current global blocker

Hosted CI and PR workflows are repeatedly failing before any job step starts. Ralph
Bridge reports zero failed steps, no log excerpts, and `JOB_NOT_FOUND`/unavailable job
logs for default CI and dedicated RMA-190 workflows. Those failures are treated as CI
infrastructure failures, not as repository source failures.

While that infrastructure issue exists:

- do not merge RMA-190 PRs merely because local source contracts pass;
- do not mark TODO checkboxes complete unless the relevant safe reconciliation PR is merged;
- keep exact branch heads ready for requalification;
- continue only source-level repair, branch consolidation, and local/static validation;
- avoid creating new branches for scenarios that already have a canonical branch below.

## PR #39 audit result and replacement

PR #39 (`ralph/rma190-core-evidence-reconcile`) is **unsafe as currently written** and
must not be merged. Its only changed file is the authoritative TODO, and the diff is
`+20 / -3362`; the branch copy of `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` is
only 76 lines long. That is a truncated-file overwrite, not a valid TODO
reconciliation.

The underlying physical evidence cited by PR #39 is real enough to keep: Ralph Bridge
report `35768977213` shows workflow `RMA-190 Core End-to-End Android Acceptance` passed
on exact SHA `b188ce3b18635d24da79cb762e245c52e361ab5e` and produced a non-expired
artifact named `rma190-core-end-to-end-report-b188ce3b18635d24da79cb762e245c52e361ab5e`.

PR #71 (`ralph/rma190-core-evidence-reconcile-v2`) is the safe replacement evidence
lane. It starts from current `master` and adds only
`docs/validation/RMA_190_CORE_E2E_PHYSICAL_VALIDATION_2026-09-22.md`. It intentionally
does **not** edit the authoritative TODO; a later safe TODO reconciliation must preserve
the complete current TODO and apply only the intended RMA-190 core checkbox/evidence
delta.

## Canonical scenario matrix

| RMA-190 scenario | Canonical lane | Current evidence | Missing before closure | Duplicate/superseded lanes |
| --- | --- | --- | --- | --- |
| Offline launch with no network | PR #71, `ralph/rma190-core-evidence-reconcile-v2` at `bebea19d3102531114b5fd3188324f5bd5f8ac6c`. | Physical Android core E2E run `35768977213` passed on exact SHA `b188ce3b18635d24da79cb762e245c52e361ab5e`; PR #71 records the evidence without truncating the TODO. | Exact-head CI/merge gate after CI infrastructure recovers; optional later safe TODO reconciliation to mark the checkbox complete. | PR #39 is unsafe and superseded; PR #22 `ralph/rma-190-core-e2e`; `ralph/rma190-core-e2e`; older `ralph/rma190-camera-e2e` should not be used for this core checkbox. |
| MuJoCo full model load and neutral reset | PR #71, `ralph/rma190-core-evidence-reconcile-v2` at `bebea19d3102531114b5fd3188324f5bd5f8ac6c`. | Same passed core E2E run `35768977213`; PR #71 is the safe evidence record. | Exact-head CI/merge gate after CI infrastructure recovers; verify artifact details before any TODO checkbox closure. | PR #39 is unsafe and superseded; PR #22 and older core branches are supporting/superseded unless diff audit proves otherwise. |
| Deterministic gesture replay | PR #71, `ralph/rma190-core-evidence-reconcile-v2` at `bebea19d3102531114b5fd3188324f5bd5f8ac6c`. | Same passed core E2E run `35768977213`; PR #71 is the safe evidence record. | Exact-head CI/merge gate after CI infrastructure recovers; verify replay evidence is physical Android/shipping host, not fixture-only lower-level proof. | PR #39 is unsafe and superseded; PR #22 and older core branches are supporting/superseded unless diff audit proves otherwise. |
| Front camera -> head rotation -> transformed Reachy-eye frame | No canonical open PR yet. Candidate branch: `ralph/rma190-camera-requalify` at `f3a49a7b968d4916aeba63faf3cc9cfc4f5a301b`. | Branch exists but no open PR in current branch inventory. | Diff audit, PR creation if still relevant, exact-head CI, physical Android camera/head-motion artifact, and TODO reconciliation. | `ralph/rma190-camera-e2e`, `ralph/rma190-front-camera-e2e` are older lanes; do not advance until compared with `camera-requalify`. |
| On-device ASR -> local LLM -> behavior -> offline TTS | No canonical RMA-190 PR yet. Supporting branch: PR #40 `ralph/rma125-visible-speech-setup-guidance` at `eaa5e4d910942663f2aa361760031ce6f8c35745`. | RMA-125 setup guidance source contract passed locally; this is supporting guidance only. | A real physical spoken-turn harness/run using explicit on-device ASR, selected local LLM, behavior path, and exact offline TTS voice. Device/service/language availability must be explicit. | `ralph/rma190-asr-jni-hardening` may be a supporting branch, but it is not scenario closure. |
| Rear-camera visual question with optional VLM | PR #43, `ralph/rma190-rear-visual-e2e` at `bdfea61e8638c45d8cf5d74bac228839b2cd20fa` | Focused Python contract passed locally after test repair. | Exact-head CI and physical Android rear-camera artifact; optional VLM must report unavailable truthfully when credentials/provider are absent. | None currently better. |
| Independent cloud provider combinations | PR #44, `ralph/rma190-independent-provider-combinations` at `bf492ded248d3e565adab9dfb80bfa9673f4b8e9` | Focused source contract passed locally after spec wording repair. | Exact-head CI / Unity evidence; no real cloud-response claim unless credentials/endpoints are intentionally supplied. | PR #46 `ralph/rma190-provider-combinations` is superseded by PR #44 unless diff audit shows unique work. |
| Permission denial/revocation | PR #53, `ralph/rma190-permission-end-to-end` at `888b5570fe75a22115f262cc61a96201055a2afa` | Focused Python contract passed locally; local validation note committed. | Exact-head CI and physical Android permission-denial/revocation artifact. | `ralph/rma190-permission-denial-revocation-e2e` has no open PR; keep parked unless PR #53 is invalidated. |
| Network loss, rate limit, malformed response, cancellation | PR #67, `ralph/rma190-network-failure-modes` at `b9d976e2616286f814d458099bf1035f180f82f5` | Focused Python contract passed locally; local validation note committed. | Exact-head CI and any required provider/transport artifact evidence. | PR #48, #45, #47, #50, #55, #63, and #65 overlap parts of provider/failure handling. Treat as supporting/superseded unless a diff audit finds unique required coverage. |
| Model corruption/OOM | PR #68, `ralph/rma190-model-corruption-oom` at `786427d20ada7fad6a4446ac181939b7bad02b1e` | Focused Python contract passed locally; local validation note committed. | Exact-head CI and model-package/local-provider failure artifact evidence. | PR #49 and #51 are superseded by PR #68 unless diff audit shows unique required coverage. |
| Solver fault and controlled reset | PR #69, `ralph/rma190-solver-fault-reset` at `f9fd79c20f20093dc91be727169f24da28204713` | Focused Python contract passed locally; local validation note committed. | Exact-head CI and native/runtime recovery evidence. | PR #59 and #60 are superseded by PR #69 unless diff audit shows unique required coverage. |

## Supporting/non-scenario hardening lanes

These branches may support the scenario lanes but should not be counted as separate
RMA-190 scenario closure lanes:

- PR #38 `ralph/rma190-main-thread-metadata` — main-thread metadata safety.
- PR #32 `ralph/rma190-plan-hash-shadowing` and PR #34
  `ralph/rma190-plan-hash-shadowing-v2` — SHA-256 type shadowing; choose at most one
  after diff audit.
- PR #36 `ralph/rma190-restore-truncated-android-helper` — helper restoration.
- `ralph/rma190-report-main-thread`, `ralph/rma190-sha256-namespace-fix`, and
  `ralph/rma190-system-sha256` — no open PR in the branch inventory; treat as parked
  unless they contain unique fixes not present in the PR lanes above.

## Operational plan

1. Do not create more RMA-190 implementation branches until this matrix is updated.
2. For each scenario, finish or repair the canonical lane first.
3. For duplicate open PRs, compare diffs against the canonical lane and either:
   - transplant unique required changes into the canonical branch; or
   - close/supersede the duplicate once GitHub-side PR comments/closure are available.
4. Re-run exact-head CI only after the zero-step Actions failure is fixed.
5. Merge only after the relevant canonical branch has exact-head CI and required
   physical/provider/native/model evidence.
6. Reload the authoritative TODO after every successful merge and close only the
   checkbox whose required evidence is actually present.

## Current next actions

1. Treat PR #71 as the safe replacement for PR #39 and leave PR #39 unmerged.
2. Fix the GitHub Actions/self-hosted runner zero-step failure, because it blocks all
   exact-head qualification and merge gates.
3. Decide whether `ralph/rma190-camera-requalify` should become the canonical
   front-camera PR; if yes, open one PR and mark older camera branches superseded.
4. Create a real spoken-turn RMA-190 lane only after the physical ASR/offline-TTS
   prerequisites are clear; PR #40 is supporting setup guidance, not the spoken-turn
   acceptance itself.
