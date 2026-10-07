# RMA-190 / RMA-194 current-master refresh — 2026-10-07

## Source head

- Repository: `ekkus93/weachy-mini`
- Exact source head: `571cd291c2c5fb2ef9c688d21ef33a60b4822c4b`
- Commit message: `RMA-194: restore release status contract phrases`

## Purpose

This note records a current-master qualification refresh after the RMA-194 status-only reconciliation commits. It is evidence for the health of the current tree, but it is **not** a new RMA-190 scenario closure and it does **not** close any remaining RMA-184 or RMA-194 physical/performance release row.

The authoritative checklist remains `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. Any future checkbox closure must still update that file with the specific scenario evidence required by the checklist.

## Exact-head workflow results

The aggregate Ralph CI outcome for `571cd291c2c5fb2ef9c688d21ef33a60b4822c4b` was `passed`.

| Workflow | Run | Result | Evidence boundary |
| --- | ---: | --- | --- |
| `CI` | `37682068888` | passed | Hosted static, managed, native, Android, and pinned model gates. |
| `Local Unity Android Validation` | `37682068897` | passed | Self-hosted Unity/Android validation on `kawa`. |
| `Local Unity Android Device Validation No Artifacts` | `37682069141` | passed | Physical A54 no-artifacts validation, including RMA-191/192/193 contracts, RMA-125 A54 speech revalidation contract, Unity tests, APK build/verification, device pinning, RMA-190 core, RMA-090/091/092, RMA-111, RMA-154, RMA-022, RMA-195, and authoritative rendering. |
| `RMA Release Review Contracts` | `37682068910` | passed | RMA-191, RMA-192, RMA-193, and RMA-194 release-review/status contracts. |
| `RMA-173 Silent Failure Regressions` | `37682068866` | passed | Permanent fail-closed regressions for ASR/TTS/LLM/camera/solver/calibration/buffer/provider failure paths. |
| `RMA-134 local LLM Android acceptance` | `37685034977` | passed | Published success for this exact source head; physical execution was scope-skipped because the relevant inputs had not changed. |
| `RMA-135 resource governor Android acceptance` | `37685035123` | passed | Published success for this exact source head; physical execution was scope-skipped because the relevant inputs had not changed. |

## Non-overclaiming boundary

The no-artifacts physical A54 validation refresh confirms that the current source head still passes already-established physical paths such as RMA-190 core, camera acquisition/texture, lightweight tracking, visual servo, lifecycle, application composition, and authoritative rendering. It does not exercise every still-open RMA-190 scenario and therefore must not be used to close them.

The following RMA-190 scenarios remain open until dedicated scenario evidence exists:

- on-device ASR -> local LLM -> behavior -> offline TTS;
- rear-camera visual question with optional VLM;
- independent cloud provider combinations;
- network loss, rate limit, malformed response, and cancellation;
- model corruption/OOM;
- solver fault and controlled reset.

The following RMA-194 release rows likewise remain open:

- Android on-device ASR and offline TTS work where installed;
- selected benchmark-backed local LLM works without blocking physics;
- representative-device performance report exists.

RMA-184 also remains open for measured default-profile publication and its acceptance criteria: long-running memory/state-lag behavior, thermal degradation following the documented priority order, and supported-device target compliance.

## Infrastructure classification

No source failure was observed on the exact current head above. Queued, skipped-by-scope, or no-artifact behavior is not itself evidence for a new physical claim. Any future compiler, lint, test, Unity build, Android build, physical acceptance, or managed/native contract failure on a later head remains normal repository work and must be diagnosed rather than treated as an infrastructure blocker.
