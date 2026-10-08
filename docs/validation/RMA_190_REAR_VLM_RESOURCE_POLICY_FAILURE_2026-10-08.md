# RMA-190 rear-camera/VLM physical acceptance: resource-policy blocker (2026-10-08)

Source head: `381c9548edbbc392e5880f1c7e244ca8a6a52250`.
Physical run: `37788548371` (job `113349517756`), Samsung SM-A546E, Android API 36.
Outcome: **failed**, not accepted.

The runner built and verified the shipping ARM64 API-26 APK, pinned the real
device, and launched the shipping application. At the
`ask-visual-question-through-production-vlm` stage, the production
perception service returned `Unavailable` because RMA-181 priority
degradation reached `Critical` with `MemoryPressure` and
`PhysicsBudgetExceeded`. Its policy correctly suspended VLM admission;
`vlm_allowed=False`, render FPS 15, and the 0.002-second authoritative
physics timestep was preserved without step skipping.

The acceptance result was not a pass. It did not establish a successful
real rear-camera capture, transformed frame, or VLM response. The external
HTTP VLM fixture is not live-cloud evidence and the deterministic
calibration fixture is not measured calibration.

Next investigation: inspect actual resource/physics signal samples and
whether the sustained Critical decision reflects device headroom, stale
sampling, or a source defect. Preserve the fail-closed VLM gate; do not
bypass the policy or mark the RMA-190 scenario complete. Retry on an
exact-head physical device run after a justified fix or measured recovery.

Artifact: `11556333842`, retained by GitHub Actions for only three days.
