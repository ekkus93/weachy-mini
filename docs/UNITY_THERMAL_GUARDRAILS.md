# Unity Thermal Guardrails

This project intentionally limits Unity and Android build/runtime heat. The immediate goal is to keep self-hosted runners and development machines from sitting at unsafe sustained CPU/GPU load while Unity imports assets, runs validation, builds Android artifacts, or renders simple validation scenes.

These guardrails are reusable for future Unity projects.

## Problem

Unity can heat a machine from multiple independent paths:

- Unity Editor and batchmode worker threads can fan out across many CPU cores during import, compilation, tests, and builds.
- Android builds can separately fan out through Gradle, Java, CMake, Ninja, Make, Android SDK tools, and Unity-launched subprocesses.
- Runtime/player validation can render as fast as possible when there is no explicit frame cap or VSync policy.
- CI workflows can hide the source of the heat because Unity, Android, native, and device-validation work all happen in the same job.

The fix is layered. Do not rely on only one knob.

## Implemented guardrails

### 1. Unity CLI and process limits

Implemented in:

- `scripts/run_unity_limited.sh`
- `scripts/build_unity_android.sh`
- `scripts/tests/test_unity_cpu_limits.py`

The Unity wrapper defaults to:

```bash
UNITY_JOB_WORKERS=2
UNITY_CPU_QUOTA=300%
UNITY_CPU_AFFINITY=0-3
UNITY_NICE=10
```

It always injects Unity's worker cap unless the caller already supplied one:

```bash
-job-worker-count "$UNITY_JOB_WORKERS"
```

On Linux self-hosted runners, it prefers a `systemd-run --user --scope` cgroup with:

```bash
CPUQuota=$UNITY_CPU_QUOTA
CPUAffinity=$UNITY_CPU_AFFINITY
```

If user-scoped systemd is unavailable, it falls back to `taskset` plus `nice`. If `taskset` is unavailable too, it still applies `nice` and Unity's `-job-worker-count`.

This layer is aimed at Unity import/build/test CPU spikes.

### 2. Android toolchain limits

Implemented in:

- `scripts/run_android_limited.sh`
- `scripts/build_unity_android.sh`
- `scripts/tests/test_android_cpu_limits.py`

The Android wrapper defaults to:

```bash
ANDROID_GRADLE_WORKERS=2
ANDROID_NATIVE_JOBS=2
ANDROID_CPU_QUOTA=300%
ANDROID_CPU_AFFINITY=0-3
ANDROID_NICE=10
```

It exports worker caps for the common Android/native toolchain stack:

```bash
GRADLE_OPTS="-Dorg.gradle.workers.max=$ANDROID_GRADLE_WORKERS $GRADLE_OPTS"
JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=$ANDROID_GRADLE_WORKERS $JAVA_TOOL_OPTIONS"
CMAKE_BUILD_PARALLEL_LEVEL="$ANDROID_NATIVE_JOBS"
MAKEFLAGS="-j$ANDROID_NATIVE_JOBS $MAKEFLAGS"
```

It also applies the same `systemd-run` / `taskset` / `nice` process containment pattern as the Unity wrapper.

`build_unity_android.sh` now runs both Android SDK provisioning and the Unity Android build tree through this Android wrapper. The Unity invocation remains nested through `run_unity_limited.sh`, so Android and Unity limits both apply.

This layer is aimed at Gradle, Java, CMake, Ninja, Make, Android SDK tools, and subprocesses launched by Android builds.

### 3. Runtime frame pacing

Implemented in:

- `Assets/ReachyMini/Runtime/Application/ReachyRuntimeThermalPolicy.cs`
- `scripts/tests/test_runtime_thermal_policy.py`

The runtime policy applies before scene load and defaults to:

```text
Application.targetFrameRate = 30
```

It clamps configured frame-rate values to a safe range:

```text
minimum: 15 FPS
maximum: 120 FPS
```

Platform behavior:

- Android and iOS: use `Application.targetFrameRate`; set `QualitySettings.vSyncCount = 0` because mobile frame pacing should be controlled by target frame rate.
- Desktop/editor-style visual paths: set `QualitySettings.vSyncCount = 1` to avoid unrestricted rendering when a display-backed Unity path is active.

Runtime tuning:

```bash
WEACHY_TARGET_FRAME_RATE=24
```

Disable only for explicit performance or profiling runs:

```bash
WEACHY_DISABLE_RUNTIME_THERMAL_POLICY=1
```

This layer is aimed at runtime/player/render-loop heat, including generated presentations, PlayMode-style validation, Android acceptance APKs, camera/visual tests, and simple scenes that would otherwise render hundreds or thousands of FPS.

## Recommended presets

### Balanced CI default

Use this when the runner should remain responsive but still make steady progress:

```bash
UNITY_JOB_WORKERS=2
UNITY_CPU_QUOTA=300%
UNITY_CPU_AFFINITY=0-3
UNITY_NICE=10

ANDROID_GRADLE_WORKERS=2
ANDROID_NATIVE_JOBS=2
ANDROID_CPU_QUOTA=300%
ANDROID_CPU_AFFINITY=0-3
ANDROID_NICE=10

WEACHY_TARGET_FRAME_RATE=30
```

### Cooler/slower laptop or hot runner

Use this if temperatures still reach the high 80s or 90 C during Unity work:

```bash
UNITY_JOB_WORKERS=1
UNITY_CPU_QUOTA=200%
UNITY_CPU_AFFINITY=0-1
UNITY_NICE=15

ANDROID_GRADLE_WORKERS=1
ANDROID_NATIVE_JOBS=1
ANDROID_CPU_QUOTA=150%
ANDROID_CPU_AFFINITY=0-1
ANDROID_NICE=15

WEACHY_TARGET_FRAME_RATE=24
```

### Short explicit performance run

Use only when measuring performance and keep it scoped to that job:

```bash
UNITY_JOB_WORKERS=4
UNITY_CPU_QUOTA=500%
UNITY_CPU_AFFINITY=0-5
ANDROID_GRADLE_WORKERS=4
ANDROID_NATIVE_JOBS=4
ANDROID_CPU_QUOTA=500%
ANDROID_CPU_AFFINITY=0-5
WEACHY_TARGET_FRAME_RATE=60
```

For a true uncapped runtime profiling run, set:

```bash
WEACHY_DISABLE_RUNTIME_THERMAL_POLICY=1
```

Do not leave uncapped settings enabled in normal CI.

## How to reuse this in another Unity project

1. Copy `scripts/run_unity_limited.sh` into the new project.
2. Route all Unity CI invocations through it, especially Android/iOS builds and PlayMode tests.
3. Copy `scripts/run_android_limited.sh` if the project builds Android or native plugins.
4. Route Gradle, Android SDK provisioning, CMake, Ninja, Make, and Unity Android builds through the Android wrapper.
5. Add a runtime startup policy equivalent to `ReachyRuntimeThermalPolicy.cs`.
6. Add source-contract tests equivalent to:
   - `scripts/tests/test_unity_cpu_limits.py`
   - `scripts/tests/test_android_cpu_limits.py`
   - `scripts/tests/test_runtime_thermal_policy.py`
7. Treat performance-test opt-outs as explicit exceptions, not the default.

## Rules of thumb

- Start with worker/process caps before lowering frame rate below 30 FPS.
- If the machine still reaches unsafe sustained temperatures, lower `CPUQuota` and CPU affinity first.
- If Unity runtime/player validation is the heat source, lower `WEACHY_TARGET_FRAME_RATE` to 24.
- Do not disable the thermal policy for ordinary validation.
- Do not use unbounded `cmake --build ... --parallel` in shell scripts.
- Do not call Unity directly from CI scripts when `run_unity_limited.sh` can wrap it.
- Do not call Android/Gradle/native build tools directly from CI scripts when `run_android_limited.sh` can wrap them.

## Troubleshooting

### Unity is still hot during import/build

Lower:

```bash
UNITY_JOB_WORKERS=1
UNITY_CPU_QUOTA=200%
UNITY_CPU_AFFINITY=0-1
```

### Android/native builds are still hot

Lower:

```bash
ANDROID_GRADLE_WORKERS=1
ANDROID_NATIVE_JOBS=1
ANDROID_CPU_QUOTA=150%
ANDROID_CPU_AFFINITY=0-1
```

### Runtime/player validation is still hot

Lower:

```bash
WEACHY_TARGET_FRAME_RATE=24
```

### A test needs higher frame rate

Set a scoped override for that one job:

```bash
WEACHY_TARGET_FRAME_RATE=60
```

If the test is explicitly measuring maximum throughput, use:

```bash
WEACHY_DISABLE_RUNTIME_THERMAL_POLICY=1
```

and document why the opt-out is required.

## Current project files

Thermal guardrail implementation files:

- `scripts/run_unity_limited.sh`
- `scripts/run_android_limited.sh`
- `Assets/ReachyMini/Runtime/Application/ReachyRuntimeThermalPolicy.cs`

Thermal guardrail contract tests:

- `scripts/tests/test_unity_cpu_limits.py`
- `scripts/tests/test_android_cpu_limits.py`
- `scripts/tests/test_runtime_thermal_policy.py`

Primary integration point:

- `scripts/build_unity_android.sh`
