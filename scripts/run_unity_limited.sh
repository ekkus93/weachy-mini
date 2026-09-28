#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 1 ]]; then
    printf '%s\n' 'usage: run_unity_limited.sh /path/to/Unity [unity arguments...]' >&2
    exit 2
fi

UNITY_COMMAND="$1"
shift

if [[ ! -x "${UNITY_COMMAND}" ]]; then
    printf 'Unity executable is not executable: %s\n' "${UNITY_COMMAND}" >&2
    exit 1
fi

UNITY_JOB_WORKERS="${UNITY_JOB_WORKERS:-2}"
UNITY_CPU_QUOTA="${UNITY_CPU_QUOTA:-300%}"
UNITY_CPU_AFFINITY="${UNITY_CPU_AFFINITY:-0-3}"
UNITY_NICE="${UNITY_NICE:-10}"
WEACHY_UNITY_CPU_LIMITS="${WEACHY_UNITY_CPU_LIMITS:-1}"
WEACHY_UNITY_SYSTEMD_SCOPE="${WEACHY_UNITY_SYSTEMD_SCOPE:-1}"

UNITY_ARGS=("$@")

has_unity_job_worker_count()
{
    local arg
    for arg in "${UNITY_ARGS[@]}"; do
        if [[ "${arg}" == "-job-worker-count" ]]; then
            return 0
        fi
    done
    return 1
}

if ! has_unity_job_worker_count; then
    UNITY_ARGS=(-job-worker-count "${UNITY_JOB_WORKERS}" "${UNITY_ARGS[@]}")
fi

printf 'unity_cpu_limits=%s\n' "${WEACHY_UNITY_CPU_LIMITS}"
printf 'unity_job_worker_count=%s\n' "${UNITY_JOB_WORKERS}"
printf 'unity_cpu_quota=%s\n' "${UNITY_CPU_QUOTA}"
printf 'unity_cpu_affinity=%s\n' "${UNITY_CPU_AFFINITY}"
printf 'unity_nice=%s\n' "${UNITY_NICE}"

if [[ "${WEACHY_UNITY_CPU_LIMITS}" != "1" ]]; then
    exec "${UNITY_COMMAND}" "${UNITY_ARGS[@]}"
fi

SYSTEMD_PROPERTIES=()
if [[ -n "${UNITY_CPU_QUOTA}" ]]; then
    SYSTEMD_PROPERTIES+=(-p "CPUQuota=${UNITY_CPU_QUOTA}")
fi
if [[ -n "${UNITY_CPU_AFFINITY}" ]]; then
    SYSTEMD_PROPERTIES+=(-p "CPUAffinity=${UNITY_CPU_AFFINITY}")
fi

can_use_systemd_scope()
{
    if [[ "${WEACHY_UNITY_SYSTEMD_SCOPE}" != "1" ]]; then
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
        nice -n "${UNITY_NICE}" \
        "${UNITY_COMMAND}" \
        "${UNITY_ARGS[@]}"
fi

if command -v taskset >/dev/null 2>&1 && [[ -n "${UNITY_CPU_AFFINITY}" ]]; then
    printf '%s\n' 'systemd-run CPU scope unavailable; falling back to taskset affinity plus nice.' >&2
    exec taskset -c "${UNITY_CPU_AFFINITY}" \
        nice -n "${UNITY_NICE}" \
        "${UNITY_COMMAND}" \
        "${UNITY_ARGS[@]}"
fi

printf '%s\n' 'systemd-run/taskset CPU limits unavailable; falling back to nice plus Unity job-worker-count only.' >&2
exec nice -n "${UNITY_NICE}" \
    "${UNITY_COMMAND}" \
    "${UNITY_ARGS[@]}"
