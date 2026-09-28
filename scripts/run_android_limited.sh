#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 1 ]]; then
    printf '%s\n' 'usage: run_android_limited.sh command [arguments...]' >&2
    exit 2
fi

ANDROID_GRADLE_WORKERS="${ANDROID_GRADLE_WORKERS:-2}"
ANDROID_NATIVE_JOBS="${ANDROID_NATIVE_JOBS:-2}"
ANDROID_CPU_QUOTA="${ANDROID_CPU_QUOTA:-300%}"
ANDROID_CPU_AFFINITY="${ANDROID_CPU_AFFINITY:-0-3}"
ANDROID_NICE="${ANDROID_NICE:-10}"
WEACHY_ANDROID_CPU_LIMITS="${WEACHY_ANDROID_CPU_LIMITS:-1}"
WEACHY_ANDROID_SYSTEMD_SCOPE="${WEACHY_ANDROID_SYSTEMD_SCOPE:-1}"

export ANDROID_GRADLE_WORKERS
export ANDROID_NATIVE_JOBS
export CMAKE_BUILD_PARALLEL_LEVEL="${ANDROID_NATIVE_JOBS}"
export GRADLE_OPTS="-Dorg.gradle.workers.max=${ANDROID_GRADLE_WORKERS}${GRADLE_OPTS:+ ${GRADLE_OPTS}}"
export JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=${ANDROID_GRADLE_WORKERS}${JAVA_TOOL_OPTIONS:+ ${JAVA_TOOL_OPTIONS}}"
export MAKEFLAGS="-j${ANDROID_NATIVE_JOBS}${MAKEFLAGS:+ ${MAKEFLAGS}}"

printf 'android_cpu_limits=%s\n' "${WEACHY_ANDROID_CPU_LIMITS}"
printf 'android_gradle_workers=%s\n' "${ANDROID_GRADLE_WORKERS}"
printf 'android_native_jobs=%s\n' "${ANDROID_NATIVE_JOBS}"
printf 'android_cpu_quota=%s\n' "${ANDROID_CPU_QUOTA}"
printf 'android_cpu_affinity=%s\n' "${ANDROID_CPU_AFFINITY}"
printf 'android_nice=%s\n' "${ANDROID_NICE}"
printf 'cmake_build_parallel_level=%s\n' "${CMAKE_BUILD_PARALLEL_LEVEL}"
printf 'gradle_workers_property=org.gradle.workers.max=%s\n' "${ANDROID_GRADLE_WORKERS}"
printf 'java_active_processor_count=%s\n' "${ANDROID_GRADLE_WORKERS}"

if [[ "${WEACHY_ANDROID_CPU_LIMITS}" != "1" ]]; then
    exec "$@"
fi

SYSTEMD_PROPERTIES=()
if [[ -n "${ANDROID_CPU_QUOTA}" ]]; then
    SYSTEMD_PROPERTIES+=(-p "CPUQuota=${ANDROID_CPU_QUOTA}")
fi
if [[ -n "${ANDROID_CPU_AFFINITY}" ]]; then
    SYSTEMD_PROPERTIES+=(-p "CPUAffinity=${ANDROID_CPU_AFFINITY}")
fi

can_use_systemd_scope()
{
    if [[ "${WEACHY_ANDROID_SYSTEMD_SCOPE}" != "1" ]]; then
        return 1
    fi
    if ! command -v systemd-run >/dev/null 2>&1; then
        return 1
    fi
    if [[ "${#SYSTEMD_PROPERTIES[@]}" -eq 0 ]]; then
        return 1
    fi

    systemd-run \
        --user \
        --scope \
        --quiet \
        "${SYSTEMD_PROPERTIES[@]}" \
        true >/dev/null 2>&1
}

if can_use_systemd_scope; then
    exec systemd-run \
        --user \
        --scope \
        "${SYSTEMD_PROPERTIES[@]}" \
        nice -n "${ANDROID_NICE}" \
        "$@"
fi

if command -v taskset >/dev/null 2>&1 && [[ -n "${ANDROID_CPU_AFFINITY}" ]]; then
    printf '%s\n' 'systemd-run Android CPU scope unavailable; falling back to taskset affinity plus nice.' >&2
    exec taskset -c "${ANDROID_CPU_AFFINITY}" \
        nice -n "${ANDROID_NICE}" \
        "$@"
fi

printf '%s\n' 'systemd-run/taskset Android CPU limits unavailable; falling back to nice plus Gradle/CMake/JVM worker caps only.' >&2
exec nice -n "${ANDROID_NICE}" "$@"
