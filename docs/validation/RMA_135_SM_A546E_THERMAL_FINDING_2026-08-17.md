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

## Escalated recurrence, admission-time failure (2026-08-22, later same day)

Run `32599450986` (commit `60a3a6c`, the live conversational-turn trigger work) failed
with a **new, worse variant** of this finding: `Suspended` at the admission gate itself
(`RMA-135 admission refused local inference once the mandatory prompt was measured
(488 tokens)`, after only ~4.5s -- before generation even started), with
`reasons=DeviceProfileLimit, ThermalModerate, MemoryPressure, PhysicsBudgetExceeded`.
`ThermalModerate` and `MemoryPressure` had not appeared in this finding before (prior
runs only ever showed `ThermalLight`), and the failure point moved earlier -- admission
refusal instead of exhausting the 8-attempt post-recovery retry budget during
generation. Queried the physical device directly, immediately after the run
(`adb shell dumpsys thermalservice`/`dumpsys battery`): SKIN was at `mValue=40.2,
mStatus=2` (Moderate) and battery temperature had climbed steadily across the
afternoon (35.5 C to 36.8 C, `remain` capacity draining, level 38% to 35%) --
independent, live confirmation that the device really was in the reported
`ThermalModerate` state at the time of failure, not a harness or governor bug.

This is judged **not a regression from the same-commit work** (on-demand VLM frame
capture + the new `ReachyConversationTurnOrchestrator`, neither of which touches the
governor, admission logic, or this acceptance harness) for three reasons: (1) the live
device telemetry above independently corroborates the governor's own reported reason;
(2) RMA-134 (same device, same commit, same new composition-wiring code path since
both acceptance harnesses boot the full app) passed cleanly on this same commit,
immediately before this run; (3) this session ran an unusually long, repeated sequence
of physical-device work today -- the RMA-195 cloud-LLM thermal comparison experiment
(45s of sustained generation), plus several consecutive Local Unity Android Validation
+ RMA-134/135 physical-acceptance cycles in the preceding hour -- with no deliberate
cooldown between runs, which is a plausible, sufficient explanation for the device
running measurably hotter than any prior run in this finding. Recorded as a further
data point strengthening (not superseding) the existing finding; the governor was not
touched, consistent with the user's standing decision to keep RMA-135 open as a known
device limitation.

## Reboot does not clear it; the accumulated-heat hypothesis is wrong (2026-08-22, later still)

Commit `970eeb1`'s own CI run of the same acceptance harness (unrelated code -- an
RMA-163 test-name fix and a `CA2263` analyzer fix, neither touching the governor)
reproduced the identical escalated signature above:
`reasons=DeviceProfileLimit, PhysicsBudgetExceeded` at admission time. Live device
query beforehand: SKIN `mValue=40.3`, `mStatus=1` (Light); total RAM reported
`status low`, only ~191 MB truly free; `uptime` showed **6 days, 6 hours** with no
reboot and a load average of 23.7/21.2/18.7 -- unusually high for a phone. This
looked like a plausible explanation at the time: days of accumulated background
cruft (leaked processes, repeated app installs/launches) on top of the day's real
thermal load.

At the user's request, the device was rebooted (`adb reboot`, confirmed
`sys.boot_completed=1`, ~60s settle time) and RMA-135 was re-run immediately
against the existing local `weachy-mini-device-arm64-api26.apk` (built 2026-08-16;
predates today's changes, but nothing built today touches the governor, admission
logic, or this harness, so this is a valid same-question re-test). **This
hypothesis was wrong.** Even 2 minutes after a cold boot, SKIN was already at
`mValue=43.4`, `mStatus=2` (Moderate) -- higher than the pre-reboot reading -- and
climbed to `44.2` by the time the run finished 4 minutes into uptime. Free-RAM
status did genuinely improve after reboot (`status moderate` vs. `status low`,
more free RAM), so the reboot was not useless -- but the run failed with the exact
same signature anyway: `Suspended` at admission,
`reasons=DeviceProfileLimit, ThermalModerate, MemoryPressure, PhysicsBudgetExceeded`.

A device that reaches Moderate thermal throttling within two minutes of powering on
is not carrying software-accumulated heat -- multi-day uptime is ruled out as the
driver. Two more plausible explanations, neither yet tested: (1) genuinely elevated
ambient temperature wherever the device physically sits, which several hours of
testing today would not have helped; (2) the device has been on USB power the
entire session for `adb` access, and battery-charging current is itself a real,
independent heat source unrelated to anything the app or physics does. Testing (2)
would mean running RMA-135 on battery power, disconnected from USB, after a genuine
cool-down period -- losing the live adb connection mid-run means the harness would
need to be re-triggered without a tether, which is a larger change to how this
acceptance runs than attempted so far. Not investigated further this session;
recorded as the corrected working hypothesis, superseding the accumulated-heat
theory above. The governor was not touched.
