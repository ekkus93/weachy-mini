#!/usr/bin/env bash
set -euo pipefail

adb_bin="${ADB:-adb}"
serial="${ANDROID_SERIAL:-}"
package="${REACHY_ANDROID_PACKAGE:-com.ekkus.weachymini}"
activity="${REACHY_ANDROID_ACTIVITY:-com.unity3d.player.UnityPlayerGameActivity}"
output="${RMA184_OUTPUT:-artifacts/rma184-device-probe.json}"

adb_cmd=("${adb_bin}")
if [[ -n "${serial}" ]]; then
  adb_cmd+=(-s "${serial}")
fi

# A previous invocation must not be mistaken for this invocation's evidence.
# Remove local evidence before any fallible device operation.
rm -f -- "${output}"
"${adb_cmd[@]}" get-state >/dev/null
if ! "${adb_cmd[@]}" shell pm grant "${package}" android.permission.CAMERA; then
  printf '%s\n' 'RMA-184 could not pre-grant CAMERA; probe may report incomplete camera capability.' >&2
fi
if ! "${adb_cmd[@]}" shell pm grant "${package}" android.permission.RECORD_AUDIO; then
  printf '%s\n' 'RMA-184 could not pre-grant RECORD_AUDIO; ASR availability may report permission required.' >&2
fi
"${adb_cmd[@]}" shell am force-stop "${package}"

remote="/sdcard/Android/data/${package}/files/rma184-device-probe.json"
if ! "${adb_cmd[@]}" shell rm -f "${remote}"; then
  printf '%s\n' 'RMA-184 could not remove the prior on-device report; refusing stale evidence.' >&2
  exit 1
fi
if ! "${adb_cmd[@]}" shell test ! -f "${remote}"; then
  printf '%s\n' 'RMA-184 prior on-device report is still present; refusing stale evidence.' >&2
  exit 1
fi
"${adb_cmd[@]}" shell am start -W \
  -n "${package}/${activity}" \
  --ez reachy_rma184_device_probe true >/dev/null

for ((attempt = 0; attempt < 90; ++attempt)); do
  if "${adb_cmd[@]}" shell test -f "${remote}"; then
    break
  fi
  sleep 1
done

if ! "${adb_cmd[@]}" shell test -f "${remote}"; then
  printf '%s\n' 'RMA-184 device probe did not produce its report.' >&2
  exit 1
fi

mkdir -p "$(dirname "${output}")"
"${adb_cmd[@]}" pull "${remote}" "${output}" >/dev/null
python3 "$(dirname "$0")/validate_rma184_device_probe.py" "${output}"
