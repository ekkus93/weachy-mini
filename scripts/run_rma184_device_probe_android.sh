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

"${adb_cmd[@]}" get-state >/dev/null
if ! "${adb_cmd[@]}" shell pm grant "${package}" android.permission.CAMERA; then
  printf '%s\n' 'RMA-184 could not pre-grant CAMERA; probe may report incomplete camera capability.' >&2
fi
if ! "${adb_cmd[@]}" shell pm grant "${package}" android.permission.RECORD_AUDIO; then
  printf '%s\n' 'RMA-184 could not pre-grant RECORD_AUDIO; ASR availability may report permission required.' >&2
fi
"${adb_cmd[@]}" shell am force-stop "${package}"
"${adb_cmd[@]}" shell am start -W \
  -n "${package}/${activity}" \
  --ez reachy_rma184_device_probe true >/dev/null

remote="/sdcard/Android/data/${package}/files/rma184-device-probe.json"
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
python3 - "${output}" <<'PY'
from pathlib import Path
import json
import sys
path = Path(sys.argv[1])
data = json.loads(path.read_text(encoding="utf-8"))
if data.get("schema_version") != 1 or data.get("status") != "passed":
    raise SystemExit(f"RMA-184 probe failed: {data.get('error', 'unknown')}")
for key in (
    "model", "soc", "operating_system", "android_api_level",
    "system_memory_mib", "graphics_api", "camera_permission",
    "on_device_asr", "offline_tts", "support_status",
):
    if key not in data:
        raise SystemExit(f"RMA-184 probe missing {key}")
print(
    "RMA-184 device probe passed: "
    f"{data['model']} api={data['android_api_level']} "
    f"ram_mib={data['system_memory_mib']} graphics={data['graphics_api']} "
    f"support={data['support_status']}"
)
PY
