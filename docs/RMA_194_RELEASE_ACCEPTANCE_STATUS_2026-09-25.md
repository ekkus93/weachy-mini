# RMA-194 release acceptance status — 2026-09-25

## Purpose

This document maps the still-open RMA-194 release acceptance checklist items to current evidence, open PRs, and blockers. It is a planning/status artifact only. It does not close any checkbox in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.

Authoritative baseline: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`.

## Current merge/qualification boundary

Most ready evidence PRs are blocked by generic GitHub Actions failures where jobs report no failed steps and Ralph reports `JOB_NOT_FOUND`/unavailable logs. These are treated as CI infrastructure failures, not source failures.

The Samsung A54 is connected to the self-hosted runner again. RMA-190 permission denial/revocation passed on the physical A54 at exact branch heads `7072a5043d4632398d02174b6fdc8525fd8fbeaa` (run `36233165870`) and `80e37b0dbf35a6684f8b6eecc0a5dcde719e62d2` (run `36235582188`). New physical Android validations may proceed while the device remains available; secure keyguard or device disconnection must still be treated as hardware availability conditions rather than fabricated success.

## RMA-194 item map

| RMA-194 item | Current evidence / PR | Status | Blocker before checkbox closure |
| --- | --- | --- | --- |
| App launches and runs offline on supported hardware. | RMA-190 core evidence PR #71 records core physical A54 evidence including offline launch/no-network-related acceptance. RMA-125 network-disabled speech evidence PR #79 records network-disabled app composition evidence. | Evidence exists in unmerged PRs. | Merge-gate CI must recover; TODO reconciliation must reference merged evidence. |
| Android on-device ASR and offline TTS work where installed. | RMA-195 application-composition PR #78 and RMA-125 network-disabled speech PR #79 show explicit on-device ASR readiness and offline TTS completion while networking is disabled. | Strong prerequisite evidence; full conversation remains open. | A real human-utterance or otherwise explicitly accepted non-fabricated ASR conversation path remains required for RMA-125/full spoken-turn closure. |
| Selected benchmark-backed local LLM works without blocking physics. | RMA-134 local LLM evidence PR #76 records A54 run `36140591155` passing local LLM acceptance. RMA-184 profile evidence PR #81 records representative default-profile metrics; RMA-180 performance evidence PR #74 records performance acceptance. | Evidence exists for local LLM and default profiles, but release acceptance needs final reconciliation. | Merge-gate CI and final TODO reconciliation. Any full spoken-turn release claim still needs combined ASR -> local LLM -> behavior -> offline TTS evidence. |
| OpenAI and compatible providers are independently configurable. | RMA-190 independent provider combinations PR #44 and RMA-142 transformed-image input PR #41 cover provider/profile independence and image-capable provider contract behavior. | Implemented in branches; not merged. | Exact-head hosted qualification and merge-gate recovery. Real cloud-provider success should not be claimed unless credentials/endpoints are intentionally supplied and validated. |
| Behavior planner validates all AI output. | RMA-142/RMA-143 LLM adapter output goes through the RMA-151 behavior-intent validation path; RMA-195 app composition validates behavior intent dispatch; RMA-190 failure/provider lanes preserve fail-closed malformed-output coverage. | Evidence distributed across branches. | Exact-head CI/merge and final reconciliation. |
| No silent provider, privacy, calibration, or kinematic fallbacks remain. | RMA-173 silent-failure regressions PR #42, RMA-191 security/privacy PR #64, RMA-190 network/provider/model/solver PRs #55/#63/#67/#68/#69, and rear/front camera evidence all enforce explicit unavailable/faulted paths. | Broad source-contract coverage exists in unmerged branches. | Exact-head hosted qualification and merge-gate recovery. |
| Diagnostics identify active fidelity, providers, and failures. | RMA-173 diagnostics regressions PR #42 and RMA-191 review PR #64 cover diagnostics/redaction/failure visibility. RMA-190 consolidation PR #70 records current evidence status. | Coverage exists in unmerged branches. | Exact-head hosted qualification, merge, and TODO reconciliation. |
| Representative-device performance report exists. | RMA-184 profile evidence PR #81 records pass run `36187273962` and metrics extraction run `36192045766`, including 30 FPS and 60 FPS NativePhysics/UnityRendering rows. | Evidence exists in unmerged PR. | Merge-gate CI recovery and TODO reconciliation. |
| Licenses and attribution are complete. | RMA-192 license/attribution review PR #83 is the current comprehensive review branch; older PR #57 contains earlier partial reconciliation. | Branch ready but unmerged. | Exact-head hosted qualification and merge-gate recovery. |

## Release-gate blockers

1. Generic PR CI is failing with zero-step/`JOB_NOT_FOUND` infrastructure failures across otherwise ready branches.
2. Full RMA-125/RMA-190 spoken-turn evidence is still missing: on-device ASR utterance -> local LLM -> behavior -> offline TTS with networking disabled, or an explicitly accepted non-fabricated ASR acceptance method.
3. RMA-002 still requires second independently provisioned Unity/Android build evidence.
4. TODO checkbox closure must wait for merged evidence and an exact current-master reconciliation pass.

## Non-blocker classifications

- Artifact upload quota failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload.
- Zero-step PR CI failures with unavailable logs are infrastructure failures, not source failures.
- The earlier RMA-190 permission run `36197725278` failed because the phone was behind secure keyguard; that was a hardware/keyguard availability condition, not a source failure. Subsequent A54 runs `36233165870` and `36235582188` passed the permission denial/revocation acceptance path.
- Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.
