# CI infrastructure blocker — 2026-09-28

## Exact-head observation

Current `master` was `1d0e05db4c5e4e094e562d8ca100b7902d01a5ea` when this record was created.

Local Unity Android Validation run `36461067521` reached the self-hosted `kawa` runner and successfully completed repository checkout, pinned Unity/Android discovery, generated Reachy presentation preparation, production MuJoCo Android staging, and the Unity test step.

The run then failed at **Upload Unity test results**. Because that upload step failed, GitHub Actions skipped the downstream APK build and all physical Android acceptance steps in the job. This is an artifact-publication/infrastructure failure boundary, not evidence that the Unity tests, Android build, or physical acceptance source paths failed.

The generic hosted CI and status-publication jobs on the same exact head also continue to report zero-step/unavailable-log failures through Ralph Bridge. Those failures likewise do not provide a source diagnostic.

## Qualification consequence

No TODO checkbox is closed by this record. In particular, the skipped Android acceptance steps must not be represented as device failures or passes. They remain unqualified on this exact head.

The next useful CI repair is to make artifact publication non-blocking for validation execution while preserving an explicit warning/failure record for missing uploaded evidence. The current workflow cannot safely be rewritten through Ralph Bridge in this run because the authoritative workflow read contains secret-redacted text; reconstructing the full file from that redacted representation would risk corrupting the workflow. A future safe edit must start from an unredacted authoritative source or a mutation primitive that can apply a narrow patch without replacing redacted content.

## Evidence

- exact head: `1d0e05db4c5e4e094e562d8ca100b7902d01a5ea`
- Local Unity Android Validation run: `36461067521`
- self-hosted job: `109059595985`
- successful through: `Run Unity tests`
- infrastructure failure: `Upload Unity test results`
- downstream APK/device steps: skipped as a consequence of the upload failure
