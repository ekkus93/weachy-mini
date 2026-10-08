# RMA-190 rear-camera VLM physical resource-admission finding — 2026-10-08

## Exact-head physical evidence

- Repository: `ekkus93/weachy-mini`; branch: `master`.
- Exact SHA: `5dcc92f0d415f18a5cdfa7d62c289f3499649d4d`.
- Workflow: `RMA-190 Rear Camera VLM End-to-End Android Acceptance`.
- Run: `37805231788`; job: `113407670411`; result: **failure**.
- Physical device: Samsung SM-A546E, ARM64, Android API 36.
- Production MuJoCo staging, Unity ARM64 APK build, APK verification, and physical device pinning passed.
- The end-to-end scenario failed at `ask-visual-question-through-production-vlm`, because the shipping RMA-181 priority policy correctly refused VLM admission. The failure is **not** proof of a successful rear-camera/VLM scenario.

## Resource samples from the shipping production policy

The run's bounded logcat transition extraction (added on this SHA) records the same samples that the policy evaluated, not independently resampled guesses:

| Transition | Available Android memory | Physics state | RMA-181 decision |
| --- | ---: | --- | --- |
| Initial | 1,524,498,432 bytes | Unavailable | Nominal; VLM allowed |
| First pressure | 1,526,882,304 bytes | Exceeded | Critical; VLM denied |
| Combined pressure | 982,466,560 bytes | Exceeded | Critical; VLM denied |
| Recovering | 994,033,664 bytes | Healthy | Critical with RecoveryHold; VLM denied |

The Android device reported 5,689,581,568 total memory, a 226,492,416-byte low-memory threshold, `android_low_memory=False`, and thermal state `None` in all four samples. Therefore, this run does **not** establish Android low-memory or thermal-throttling as the cause. It establishes an authoritative physics deadline-miss episode, concurrent memory pressure, and a subsequent healthy physics sample that was still held by the intentional three-sample recovery hysteresis.

The earlier physical run `37797577687` on `e7fa25cb83b80bc061557dac7f5250425bc533e7` failed at the same policy gate but did not print bounded raw resource samples. The later run above improves observability without relaxing any policy or substituting a provider.

## Next production-safe experiment

The current RMA-190 acceptance asks the VLM immediately after acquiring a real rear-camera frame and installing an explicitly labeled deterministic calibration fixture. A narrowly scoped follow-up is to wait **before capturing the fresh frame** for the shipping `ReachyPriorityDegradationRuntimeDriver.CurrentDecision` to permit VLM admission across at least three separate 600 ms observations, excluding `PhysicsSignalUnavailable`. Bound this wait to 15 seconds and preserve the overall 130-second acceptance timeout. If admission never stabilizes, fail visibly with the last policy diagnostic. Do not force a governor decision, change its thresholds, inject synthetic camera frames, or treat fixture output as live cloud evidence.

If stable admission occurs, capture a **new** real rear-camera transformed Reachy-eye frame and invoke the unchanged shipping `AnalyzeSceneAsync` path. The production service must still refuse the request if pressure recurs. Only a fully passing exact-head physical report can close the RMA-190 rear-camera checkbox.

## Qualification and tooling notes

The same SHA's hosted `RMA Release Review Contracts` run `37805231701` and `RMA-173 Silent Failure Regressions` run `37805231695` passed. Hosted CI run `37805231759` had four successful jobs (managed, Android, native, Reachy model), but the static job failed during actionlint installation: Go 1.24 automatically requested unavailable `go1.26.9` from the module proxy (HTTP 404). This is an external toolchain-resolution failure, not a source-code compiler or lint failure. The workflow currently requests Go `1.24.x` even though actionlint v1.7.12 requires Go >=1.25; pinning an available compatible Go release is the appropriate CI remediation.

The authoritative TODO remains open. Attempts to publish the larger acceptance-harness edit, the full TODO reconciliation, and the workflow toolchain edit were blocked by tool safety checks; after each ambiguous write result, `master` was re-read and verified unchanged. No unverified acceptance checkbox was closed.
