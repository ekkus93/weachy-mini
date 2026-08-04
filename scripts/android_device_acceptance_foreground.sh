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
        | awk '
            /mCurrentFocus=/ { current = $0 }
            /mFocusedApp=/ { focused_app = $0 }
            END {
                if (current != "" && current !~ /mCurrentFocus=null/) {
                    print current
                } else if (focused_app != "") {
                    print focused_app
                } else if (current != "") {
                    print current
                }
            }
        '
}

power_state()
{
    "${ADB[@]}" shell dumpsys power 2>/dev/null \
        | tr -d '\r' \
        | awk '/mWakefulness=|Display Power: state=/{print}'
}

keyguard_state()
{
    "${ADB[@]}" shell dumpsys activity activities 2>/dev/null \
        | tr -d '\r' \
        | awk '
            /KeyguardController:/ { in_keyguard = 1; next }
            in_keyguard && /mKeyguardShowing=/ {
                sub(/^[[:space:]]+/, "")
                showing = $0
                next
            }
            in_keyguard && /mOccluded=/ {
                sub(/^[[:space:]]+/, "")
                occluded = $0
                exit
            }
            END {
                if (showing != "") {
                    print showing
                }
                if (occluded != "") {
                    print occluded
                }
            }
        '
}

device_is_awake()
{
    local state
    state="$(power_state || true)"
    [[ "${state}" == *"mWakefulness=Awake"* || \
        "${state}" == *"Display Power: state=ON"* ]]
}

keyguard_blocks_focused_app()
{
    local state
    state="$(keyguard_state || true)"
    if [[ "${state}" != *"mKeyguardShowing=true"* ]]; then
        return 1
    fi
    if [[ "${state}" == *"mOccluded=true"* ]]; then
        return 1
    fi
    return 0
}

prepare_device()
{
    "${ADB[@]}" wait-for-device
    "${ADB[@]}" shell svc power stayon true >/dev/null 2>&1 || true
    dismiss_immersive_confirmation

    for _ in 1 2 3; do
        "${ADB[@]}" shell input keyevent 224 >/dev/null 2>&1 || true
        "${ADB[@]}" shell wm dismiss-keyguard >/dev/null 2>&1 || true
        "${ADB[@]}" shell input keyevent 82 >/dev/null 2>&1 || true
        collapse_status_bar
        sleep 1
        if device_is_awake && ! keyguard_blocks_focused_app; then
            return
        fi
    done
}

case "${ACTION}" in
    prepare)
        prepare_device
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
            if [[ "${focus}" == *"${PACKAGE_NAME}"* ]] && \
                device_is_awake && ! keyguard_blocks_focused_app; then
                printf '%s\n' "${focus}"
                exit 0
            fi
            if (( $(date +%s) >= deadline )); then
                printf 'Timed out waiting for %s to own an awake focused window that is not blocked by keyguard.\n' \
                    "${PACKAGE_NAME}" >&2
                printf 'Last focus: %s\n' "${focus}" >&2
                printf 'Power state: %s\n' "$(power_state || true)" >&2
                printf 'Keyguard state: %s\n' "$(keyguard_state || true)" >&2
                printf '%s\n' \
                    'A showing but occluded keyguard is accepted; an unoccluded keyguard must be manually unlocked before rerunning the job.' \
                    >&2
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
