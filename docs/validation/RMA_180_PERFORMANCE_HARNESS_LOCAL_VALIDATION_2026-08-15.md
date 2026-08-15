# RMA-180 Performance Harness — Local Validation

**Task:** RMA-180 — Build performance harness
**Date:** 2026-08-15

## Implemented surface

RMA-180 adds a bounded core performance session/telemetry layer, deterministic JSON report formatting, real production timing hooks, a Unity resource/frame probe, a two-profile Android acceptance runner, a host-side ADB acceptance script, managed contract coverage, and permanent Python/static regression coverage.

The production hooks cover native physics, Unity frame cadence, camera acquisition, camera warp, lightweight tracking, local LLM generation, audio interaction, and shared network transport. No substitute provider or synthetic simulation path is used for timing collection.

## Bounded long-run behavior

Timing sample counts and maxima remain exact. Percentiles use a 4,096-entry deterministic reservoir per workload and declare when they are approximate. Resource sampling is capped at 2,048 samples at a 10-second cadence, retaining more than 5.6 hours of resource history before old samples rotate out.

The managed contract exercises exact p50/p95/p99/max behavior below the reservoir bound, bounded long-run behavior above the bound, resource-ring rotation with session-wide aggregate preservation, memory/battery/thermal summaries, explicit 30/60 FPS profiles, nested-session rejection, no-op measurement outside a session, and label/resource validation.

## Local commands

Focused static regression:

```text
python3 -m unittest scripts/tests/test_rma180_performance_harness.py -v
```

Shell syntax:

```text
bash -n scripts/run_rma180_performance_acceptance_android.sh
```

Repository static gate:

```text
TERM=xterm bash scripts/ci.sh --static-only
```

Whitespace validation:

```text
git diff --check
```

Local results on the preserved pre-diagnostics archive:

- focused RMA-180 static contract: 6/6 passed;
- complete `scripts/tests` discovery: 329/329 passed;
- `TERM=xterm bash scripts/ci.sh --static-only`: exit 0, 329/329 tests passed;
- `git diff --check`: clean.

The archive predates the subsequently landed RMA-163/RMA-170/RMA-171/RMA-172 source set, so publication must rerun the repository static gate from current `master` before integration. The sandbox does not provide `dotnet`, Unity, an Android SDK/ADB device, or the Android player toolchain. Therefore managed compilation, Unity compilation, and physical 30/60 FPS evidence are not claimed locally. The permanent managed tests and opt-in Android acceptance runner are committed so those environments can execute the same contract without changing the implementation.

## Physical acceptance command

On the dedicated Android runner/device, after producing the normal device APK:

```text
REACHY_ANDROID_SERIAL=<serial> \
RMA180_PROFILE_SECONDS=300 \
./scripts/run_rma180_performance_acceptance_android.sh
```

Longer runs may raise `RMA180_PROFILE_SECONDS` up to 3600 seconds per profile. The result must contain 30 FPS and 60 FPS profiles with nonzero native-physics, Unity-rendering, and resource samples; workloads not exercised during a profile must remain explicitly unavailable rather than receiving fabricated measurements.
