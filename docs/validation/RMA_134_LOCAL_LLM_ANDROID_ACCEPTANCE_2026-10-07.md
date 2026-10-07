# RMA-134 Local LLM Android Acceptance — 2026-10-07

## Scope

This note records the exact-head physical Android acceptance that closed the stale local-LLM offline-load evidence gap without weakening the RMA-135 physics-timing requirement.

## Exact source head

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Source SHA: `d5b09d9cd6ea04a354eed48a904228f27a5a7c19`
- Source commit: `RMA-134: apply static contract formatting`

## Physical local-LLM acceptance

- Workflow: `RMA-134 local LLM Android acceptance`
- Run: `37661645863`
- Job: `112930395217`
- Result: `success`
- Job name: `Validate RMA-134 on physical Android`

The job completed the following shipping acceptance steps on the exact source SHA:

1. Published the pending RMA-134 commit status.
2. Checked out the exact validated source SHA.
3. Determined that RMA-134 inputs required acceptance.
4. Pinned an approved representative physical Android device.
5. Downloaded the exact-SHA Unity Android artifacts from the successful Local Unity run.
6. Ran the managed-provider physical acceptance.
7. Recorded exact workflow provenance.
8. Uploaded RMA-134 physical evidence.
9. Published the final success status.

## Related exact-head qualification

The same `d5b09d9cd6ea04a354eed48a904228f27a5a7c19` head also passed:

- `CI` run `37658559986`
- `Local Unity Android Validation` run `37658559850`
- `Local Unity Android Device Validation No Artifacts` run `37658559844`
- `RMA-134 local LLM provider` run `37658559787`
- `RMA Release Review Contracts` run `37658559798`
- `RMA-173 Silent Failure Regressions` run `37658559972`

## Regression hardening

The exact-head static contract now requires `scripts/run_rma134_local_llm_acceptance_android.sh` to install the exact downloaded APK itself before resolving `pm path` and verifying the installed APK SHA-256. This prevents the previous physical-run race where RMA-134 depended on a different workflow leaving the expected APK installed on the shared Android device.

## RMA-135 reconciliation boundary

This evidence is sufficient for the local-LLM acceptance statement that the selected model loads offline and produces a validated behavior intent on representative Android hardware.

It is not used as evidence for the separate RMA-135 criterion `Physics timing remains within the defined budget during generation`. That criterion remains open unless a physical governor/timing acceptance run exercises and passes that specific requirement.
