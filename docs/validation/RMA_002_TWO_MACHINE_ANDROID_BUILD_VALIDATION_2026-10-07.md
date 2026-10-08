# RMA-002 two-machine Unity/Android build validation — 2026-10-07

## Source head

- Repository: `ekkus93/weachy-mini`
- Exact source head: `5ca67f7ac79f7bfadae3b4712dcac78ceaecdcb9`
- Commit message: `RMA-184: format device probe validator for CI`

## Purpose

This note records current exact-head evidence for the RMA-002 acceptance
criterion that two clean developer environments can build the same Android
application from the pinned toolchain. The evidence supersedes the older
`docs/IMPLEMENTATION_STATUS.md` statement that a second independently
provisioned Unity/Android machine remained required.

The authoritative checklist is still
`docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. This note is evidence for a
future direct checklist reconciliation; it is not evidence for any physical
performance, local-LLM thermal, offline-speech, or RMA-190 scenario closure.

## Exact-head build evidence

Two independently scheduled self-hosted validation lanes built and verified the
same ARM64 API-26 Unity Android application on exact source head
`5ca67f7ac79f7bfadae3b4712dcac78ceaecdcb9`.

| Workflow | Run | Result | Relevant build evidence |
| --- | ---: | --- | --- |
| `Local Unity Android Validation` | `37710449442` | passed | Resolved pinned Unity/Android installations, generated the Reachy Unity presentation, staged the production MuJoCo runtime, ran Unity tests, built the ARM64 API-26 device APK, verified the APK, pinned a physical Android device, and completed the device acceptance sequence. |
| `Local Unity Android Device Validation No Artifacts` | `37710449563` | passed | Resolved pinned Unity/Android installations, generated the Reachy Unity presentation, staged the production MuJoCo runtime, ran Unity tests, built the ARM64 API-26 device APK, verified the APK, pinned the A54 device, and completed the no-artifacts physical validation path. |

Both workflows ran after hosted CI run `37710449497`, release-review run
`37710449527`, and RMA-173 run `37710449531` passed on the same source head.
Downstream RMA-134 run `37711532523` and RMA-135 run `37711532388` also
reported success for the same exact source head.

## Interpretation

The current evidence is stronger than the original minimal-application gate:
these lanes build the production Android application, not only a scaffold. They
also enforce the pinned Unity editor, Android build support, Android SDK, and
project-generated runtime assets before building the APK.

RMA-002 should therefore no longer be blocked by the historical
single-Unity-machine limitation. Future checklist reconciliation may close the
RMA-002 acceptance row once the authoritative TODO can be edited safely.

## Non-overclaiming boundary

This evidence does not close any of the remaining release-gated physical rows:

- RMA-125 network-disabled spoken conversation remains open.
- RMA-135 local-LLM physics-timing acceptance remains open.
- RMA-184 measured default profiles and long-running device qualification remain open.
- RMA-190 remaining end-to-end scenarios remain open.
- RMA-194 final release acceptance remains open until its own criteria are met.
