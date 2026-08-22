# RMA-135 finding — SM-A546E heats past light thermal throttling under the combined physics+LLM workload within seconds

**Date:** 2026-08-17
**Device:** Samsung SM-A546E (Galaxy A54 5G), Exynos 1380, serial R5CW31AX4FL
**Status of this finding:** governor-cadence bug fixed and confirmed working; this is a
separate, genuine hardware characteristic of the device under this specific combined
workload, not a governor or acceptance-harness defect. Reproduced again on 2026-08-22
(see "Recurrence" below); kept open as a known device limitation by explicit user
decision -- not fixed by widening retry budgets or relaxing governor thresholds.
**Related roadmap items:** RMA-135 (resource/thermal governor, local LLM acceptance
criterion), RMA-184 (representative-device matrix, mid class, `supported_with_limitations`)

## Summary

Three consecutive full RMA-135 physical acceptance runs on this device (commits
`610202e`, `022535f`) exhausted the post-recovery generation retry budget with
`ResourceCancelledDuringGeneration` on every attempt, `governor_reasons` including
`ThermalLight` throughout. Per-attempt telemetry (added this session -- see the
`post_recovery_generation_completed` / `post_recovery_preflight_wait_completed`
checkpoints) shows this is real, sustained thermal-driven physics pressure, not a
governor bug:

- The device's own `dumpsys thermalservice` SKIN zone was confirmed at normal status
  (`mStatus=0`, ~35 C) roughly 15 minutes before the third run.
- That run completed in ~16 seconds. Immediately after, SKIN was back to `mStatus=1`
  (light throttling, 40.5 C) and the SoC (`AP` zone) measured 47.2 C.
- Every one of the 8 post-recovery attempts in that run carried `ThermalLight` in
  `governor_reasons`, and every attempt registered a nonzero
  `worker_deadline_miss_delta` (9 real misses across 5565 physics steps in the retry
  window) -- deadline misses were landing continuously, not just at the start.
- The governor's own hysteresis-recovery preflight (`post_recovery_preflight_wait_completed`)
  converged to `ready=True` in 1-4 samples on every attempt, confirming the earlier
  governor-cadence bug (fixed in `610202e`) is resolved and not a contributing factor
  here.

Conclusion: the combined MuJoCo physics simulation and local-LLM inference workload
alone drives this SoC into light thermal throttling within roughly 15 seconds, even
starting from a measured-cool baseline. RMA-135 refusing to sustain generation under
that condition is the governor's fail-closed design working as intended, not a defect
to fix by widening retry budgets or relaxing thresholds.

## Evidence

- Run `32063555261` (commit `610202e`): 8/8 attempts `ThermalLight`, 11 misses / 7700
  steps in the retry window.
- Run `32071228096` (commit `022535f`): 8/8 attempts `ThermalLight`, 9 misses / 5565
  steps in the retry window; governor mode briefly dropped to `Minimal` at attempt 4
  as pressure increased (`last_real_physics_state=AtRisk`).
- Device thermal readings (`adb shell dumpsys thermalservice`, `adb shell dumpsys
  battery`) taken directly before and after these runs, recorded above.

## What this does not affect

- The governor-cadence fix (`610202e`) is unrelated and confirmed correct: hysteresis
  recovery converges reliably in every attempt across both runs above.
- This is specific to the *combined* physics+LLM workload's thermal profile on this
  SoC, not a general device-support blocker -- RMA-184's separate device probe (see
  `RMA_184_SM_A546E_DEVICE_PROBE_VALIDATION_2026-08-17.md`) found the device otherwise
  functional (camera, TTS) with no reboot or crash under lighter load.

## Recurrence (2026-08-22)

Two further physical runs on the same device (commits `16c70b7`, `14b808f`, runs
`32590147928` and `32592929989`) reproduced the identical signature: 8/8 post-recovery
attempts refused with `ResourceCancelledDuringGeneration`, `governor_reasons` including
`ThermalLight`/`DeviceProfileLimit`/`PhysicsBudgetExceeded` throughout, exhausting the
same `PostRecoveryGenerationAttemptBudget = 8` retry budget. Neither commit touched
RMA-135's governor, acceptance harness, or retry/pacing constants -- both changes were
unrelated feature work (an RMA-195 Phase D cloud-LLM settings-UI slice and the new
cloud-VLM provider stack). This confirms the finding is a durable, repeated hardware
characteristic of this specific device under the combined workload, not an
intermittent flake. Decision (user, 2026-08-22): keep RMA-135's local-LLM acceptance
criterion open as a known device limitation; do not widen retry budgets or relax
governor thresholds to close it. The open question below remains unexplored.

## Related finding: cloud-offloaded LLM generation stays cool (2026-08-22)

`docs/validation/RMA_195_CLOUD_LLM_THERMAL_COMPARISON_2026-08-22.md` records a same-day
follow-up experiment on this same device: physics running normally plus 45 seconds of
**cloud-offloaded** (network) LLM generation via the new RMA-195 Phase D cloud LLM path,
instead of on-device generation. The device never left `mStatus=0` (SKIN 36.4 C / AP
41.3 C after 45s, vs. this finding's 40.5 C / 47.2 C after only ~16s on-device). This is
real evidence that offloading inference off the phone's own SoC avoids this specific
thermal limitation -- but it does not close or supersede this finding: RMA-135's
acceptance criterion is specifically about the on-device local-LLM path, which remains
genuinely thermally limited on this device exactly as described above.

## Open question

Whether a longer inter-run cooldown, a lighter acceptance workload, or active cooling
during the physical run would let RMA-135's local-LLM acceptance criterion close on
this specific device is not yet determined. Not investigated further this session at
the user's direction; recorded here as an open finding rather than continuing to
retry against real thermal pressure.
