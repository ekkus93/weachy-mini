#!/usr/bin/env bash
set -euo pipefail

ADB_BIN="${ADB_BIN:-adb}"
ACTION="${1:-}"
DEVICE_SERIAL="${2:-}"
PACKAGE_NAME="${3:-}"
TIMEOUT_SECONDS="${4:-20}"

if [[ -z "${ACTION}" || -z "${DEVICE_SERIAL}" || -z "${PACKAGE_NAME}" ]]; then
    printf 'Usage: %s <prepare|wait-focus|wait-background|restore> <serial> <package> [timeout-seconds]\n' "$0" >&2
    exit 2
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'Timeout must be a positive integer: %s\n' "${TIMEOUT_SECONDS}" >&2
    exit 2
fi

ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

collapse_status_bar()
{
    "${ADB[@]}" shell cmd statusbar collapse >/dev/null 2>&1 \
        || "${ADB[@]}" shell service call statusbar 2 >/dev/null 2>&1 \
        || true
}

dismiss_immersive_confirmation()
{
    "${ADB[@]}" shell settings put secure immersive_mode_confirmations confirmed \
        >/dev/null 2>&1 || true
}

focused_window()
{
    "${ADB[@]}" shell dumpsys window windows 2>/dev/null \
        | tr -d '\r' \
        | awk '/mCurrentFocus=|mFocusedApp=/{print; exit}'
}

case "${ACTION}" in
    prepare)
        "${ADB[@]}" wait-for-device
        "${ADB[@]}" shell input keyevent 224 >/dev/null 2>&1 || true
        "${ADB[@]}" shell wm dismiss-keyguard >/dev/null 2>&1 || true
        "${ADB[@]}" shell input keyevent 82 >/dev/null 2>&1 || true
        collapse_status_bar
        dismiss_immersive_confirmation
        "${ADB[@]}" shell svc power stayon true >/dev/null 2>&1 || true
        ;;
    wait-focus)
        deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
        while true; do
            collapse_status_bar
            focus="$(focused_window || true)"
            if [[ "${focus}" == *"ImmersiveModeConfirmation"* ]]; then
                dismiss_immersive_confirmation
                "${ADB[@]}" shell input keyevent 66 >/dev/null 2>&1 || true
                sleep 1
                continue
            fi
            if [[ "${focus}" == *"${PACKAGE_NAME}"* ]]; then
                printf '%s\n' "${focus}"
                exit 0
            fi
            if (( $(date +%s) >= deadline )); then
                printf 'Timed out waiting for %s to own the focused window. Last focus: %s\n' \
                    "${PACKAGE_NAME}" "${focus}" >&2
                exit 1
            fi
            sleep 1
        done
        ;;
    wait-background)
        deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
        while true; do
            focus="$(focused_window || true)"
            if [[ "${focus}" != *"${PACKAGE_NAME}"* ]]; then
                printf '%s\n' "${focus}"
                exit 0
            fi
            if (( $(date +%s) >= deadline )); then
                printf 'Timed out waiting for %s to leave the focused window. Last focus: %s\n' \
                    "${PACKAGE_NAME}" "${focus}" >&2
                exit 1
            fi
            sleep 1
        done
        ;;
    restore)
        collapse_status_bar
        "${ADB[@]}" shell svc power stayon false >/dev/null 2>&1 || true
        ;;
    *)
        printf 'Unsupported action: %s\n' "${ACTION}" >&2
        exit 2
        ;;
esac
