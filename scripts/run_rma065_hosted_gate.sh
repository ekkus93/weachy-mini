#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
WORK_DIR="${RMA065_HOSTED_WORK_DIR:-${RUNNER_TEMP:-${ROOT_DIR}/build}/rma065-hosted}"
ARTIFACT_DIR="${RMA065_ARTIFACT_DIR:?RMA065_ARTIFACT_DIR is required}"

rm -rf "${WORK_DIR}" "${ARTIFACT_DIR}"
mkdir -p "${WORK_DIR}" "${ARTIFACT_DIR}"
cd "${ROOT_DIR}"

python3 -m json.tool models/reachy-mini/collision-hard-stop-baseline.json >/dev/null
python3 -m py_compile \
    scripts/audit_reachy_collision_model.py \
    scripts/generate_reachy_collision_model.py \
    scripts/run_reachy_collision_hard_stop_validation.py \
    scripts/verify_rma065_reports.py
bash -n \
    scripts/build_rma065_android.sh \
    scripts/run_rma065_android.sh \
    scripts/run_rma065_hosted_gate.sh
python3 -m unittest discover \
    -s scripts/tests \
    -p 'test_*collision_model*.py' \
    -v
python3 -m unittest discover \
    -s scripts/tests \
    -p 'test_rma065_report_verifier.py' \
    -v

pins_file="${WORK_DIR}/pins.env"
python3 - "${pins_file}" <<'PY'
import json
import shlex
import sys
from pathlib import Path, PurePosixPath
from urllib.parse import urlparse

output = Path(sys.argv[1])
toolchain = json.loads(Path('toolchain.lock.json').read_text(encoding='utf-8'))
mujoco = json.loads(Path('third_party/mujoco-source.lock.json').read_text(encoding='utf-8'))
reachy = json.loads(Path('third_party/reachy-mini-source.lock.json').read_text(encoding='utf-8'))
android = toolchain['android']
repository_path = urlparse(reachy['repository']).path
repository = repository_path.removesuffix('.git').strip('/')
if repository.count('/') != 1:
    raise SystemExit(f'Invalid pinned Reachy repository: {repository!r}')
values = {
    'ANDROID_BUILD_TOOLS': android['build_tools'],
    'ANDROID_CMAKE_VERSION': android['cmake'],
    'ANDROID_COMPILE_SDK_PACKAGE': android['compile_sdk_package'],
    'ANDROID_NDK_VERSION': android['ndk'],
    'ANDROID_PLATFORM': f"android-{android['native_feasibility_min_sdk']}",
    'MUJOCO_COMMIT': mujoco['commit'],
    'MUJOCO_REPOSITORY': mujoco['repository'],
    'MUJOCO_VERSION': mujoco['version'],
    'REACHY_COMMIT': reachy['commit'],
    'REACHY_REPOSITORY': reachy['repository'],
    'REACHY_MODEL_DIRECTORY': str(PurePosixPath(reachy['model_file']).parent),
    'REACHY_LICENSE_FILE': reachy['license_file'],
    'REACHY_OUTPUT_SUBDIRECTORY': reachy['output_subdirectory'],
}
output.write_text(
    ''.join(f'{key}={shlex.quote(str(value))}\n' for key, value in values.items()),
    encoding='utf-8',
)
PY
# shellcheck disable=SC1090
source "${pins_file}"

sdkmanager="${ANDROID_HOME}/cmdline-tools/latest/bin/sdkmanager"
if [[ ! -x "${sdkmanager}" ]]; then
    sdkmanager="$(command -v sdkmanager)"
fi
"${sdkmanager}" --install \
    'platform-tools' \
    "build-tools;${ANDROID_BUILD_TOOLS}" \
    "ndk;${ANDROID_NDK_VERSION}" \
    "cmake;${ANDROID_CMAKE_VERSION}"
"${sdkmanager}" --channel=3 --install \
    "platforms;android-${ANDROID_COMPILE_SDK_PACKAGE}"

ANDROID_NDK_HOME="${ANDROID_HOME}/ndk/${ANDROID_NDK_VERSION}"
ANDROID_NDK_ROOT="${ANDROID_NDK_HOME}"
cmake_bin="${ANDROID_HOME}/cmake/${ANDROID_CMAKE_VERSION}/bin"
test -f "${ANDROID_NDK_HOME}/source.properties"
test -x "${cmake_bin}/cmake"
test -x "${cmake_bin}/ninja"
export ANDROID_NDK_HOME ANDROID_NDK_ROOT
export PATH="${cmake_bin}:${PATH}"

mujoco_source="${WORK_DIR}/mujoco"
git init "${mujoco_source}"
git -C "${mujoco_source}" remote add origin "${MUJOCO_REPOSITORY}"
git -C "${mujoco_source}" fetch --depth=1 origin "${MUJOCO_COMMIT}"
git -C "${mujoco_source}" checkout --detach FETCH_HEAD
python3 scripts/verify_source_checkout.py \
    --source "${mujoco_source}" \
    --lock third_party/mujoco-source.lock.json

reachy_source="${WORK_DIR}/reachy-mini"
git init "${reachy_source}"
git -C "${reachy_source}" remote add origin "${REACHY_REPOSITORY}"
git -C "${reachy_source}" lfs install --local
git -C "${reachy_source}" sparse-checkout init --no-cone
printf '/%s/\n/%s\n' \
    "${REACHY_MODEL_DIRECTORY}" \
    "${REACHY_LICENSE_FILE}" \
    > "${reachy_source}/.git/info/sparse-checkout"
git -C "${reachy_source}" fetch --depth=1 --filter=blob:none origin "${REACHY_COMMIT}"
git -C "${reachy_source}" checkout --detach FETCH_HEAD
git -C "${reachy_source}" lfs pull \
    --include="${REACHY_MODEL_DIRECTORY}/**" \
    --exclude=''
actual_reachy_commit="$(git -C "${reachy_source}" rev-parse HEAD)"
if [[ "${actual_reachy_commit}" != "${REACHY_COMMIT}" ]]; then
    printf 'Reachy source mismatch: expected %s, found %s.\n' \
        "${REACHY_COMMIT}" "${actual_reachy_commit}" >&2
    exit 1
fi
if [[ -n "$(git -C "${reachy_source}" status --porcelain --untracked-files=all)" ]]; then
    printf '%s\n' 'Pinned Reachy source checkout is not clean.' >&2
    git -C "${reachy_source}" status --short >&2
    exit 1
fi

import_root="${WORK_DIR}/reachy-import"
enhanced_root="${WORK_DIR}/rma065-enhanced"
python3 scripts/import_reachy_assets.py \
    --source "${reachy_source}" \
    --output-root "${import_root}"
source_model_dir="${import_root}/${REACHY_OUTPUT_SUBDIRECTORY}"
test -f "${source_model_dir}/reachy_mini.xml"
test -f "${source_model_dir}/MODEL_MAP.json"
test -f "${source_model_dir}/PROVENANCE.json"
python3 scripts/generate_reachy_collision_model.py \
    --profile models/reachy-mini/collision-hard-stop-baseline.json \
    --source-model "${source_model_dir}/reachy_mini.xml" \
    --output-model "${enhanced_root}/reachy_mini.xml" \
    --metadata "${enhanced_root}/RMA065_COLLISION_METADATA.json" \
    --copy-package
python3 scripts/generate_reachy_collision_model.py \
    --profile models/reachy-mini/collision-hard-stop-baseline.json \
    --source-model "${source_model_dir}/reachy_mini.xml" \
    --output-model "${enhanced_root}/reachy_mini.xml" \
    --metadata "${enhanced_root}/RMA065_COLLISION_METADATA.json" \
    --check

python3 -m pip install \
    --disable-pip-version-check \
    "mujoco==${MUJOCO_VERSION}"
report_dir="${WORK_DIR}/desktop-report"
mkdir -p "${report_dir}"
python3 scripts/audit_reachy_collision_model.py \
    --model "${enhanced_root}/reachy_mini.xml" \
    --output "${report_dir}/rma065-enhanced-audit.json" \
    --steps 5000 \
    --contract rma065_enhanced_collision_audit_v1
python3 scripts/run_reachy_collision_hard_stop_validation.py \
    --source-model "${source_model_dir}/reachy_mini.xml" \
    --enhanced-model "${enhanced_root}/reachy_mini.xml" \
    --profile models/reachy-mini/collision-hard-stop-baseline.json \
    --output "${report_dir}/rma065-collision-hard-stop-validation.json" \
    --neutral-steps 5000
python3 scripts/verify_rma065_reports.py \
    --audit "${report_dir}/rma065-enhanced-audit.json" \
    --validation "${report_dir}/rma065-collision-hard-stop-validation.json" \
    --profile models/reachy-mini/collision-hard-stop-baseline.json \
    --neutral-steps 5000 \
    | tee "${report_dir}/rma065-report-verification.json"

fake_build="${WORK_DIR}/build-fake"
cmake -S . -B "${fake_build}" \
    -DBUILD_TESTING=ON \
    -DREACHY_ENABLE_SANITIZERS=ON \
    -DCMAKE_BUILD_TYPE=Debug
cmake --build "${fake_build}" --parallel 2 --target \
    reachy_sim_mujoco_backend_test \
    reachy_sim_contract_test \
    reachy_sim_header_cpp_test
ctest --test-dir "${fake_build}" --output-on-failure \
    -R '^(reachy_sim_mujoco_backend_test|reachy_sim_contract_test|reachy_sim_header_cpp_test)$'

mujoco_package_dir="$(python3 - <<'PY'
import mujoco
from pathlib import Path
print(Path(mujoco.__file__).resolve().parent)
PY
)"
mujoco_library="$(find "${mujoco_package_dir}" -maxdepth 3 -type f -name 'libmujoco.so*' -print -quit)"
if [[ -z "${mujoco_library}" || ! -f "${mujoco_library}" ]]; then
    printf 'Python MuJoCo package does not contain libmujoco.so: %s\n' \
        "${mujoco_package_dir}" >&2
    exit 1
fi

state_dir="${WORK_DIR}/native-state"
fixture_dir="${state_dir}/fixture"
mkdir -p "${fixture_dir}"
python3 - "${enhanced_root}/reachy_mini.xml" "${fixture_dir}" <<'PY'
import sys
from pathlib import Path
import mujoco
from scripts.run_reachy_collision_hard_stop_validation import internal_contact_fixture

enhanced = Path(sys.argv[1]).resolve()
fixture_dir = Path(sys.argv[2]).resolve()
assets = enhanced.parent / 'assets'
if not assets.is_dir():
    raise SystemExit(f'enhanced model assets are missing: {assets}')
asset_link = fixture_dir / 'assets'
if asset_link.exists() or asset_link.is_symlink():
    asset_link.unlink()
asset_link.symlink_to(assets, target_is_directory=True)
internal_contact_fixture(
    mujoco,
    enhanced,
    fixture_dir / 'internal-contact.xml',
)
PY

real_build="${WORK_DIR}/build-real"
cmake -S . -B "${real_build}" \
    -DBUILD_TESTING=ON \
    -DREACHY_BUILD_MUJOCO_BACKEND=ON \
    -DREACHY_BUILD_MUJOCO_PROBE=ON \
    -DREACHY_MUJOCO_INCLUDE_DIR="${mujoco_source}/include" \
    -DREACHY_MUJOCO_LIBRARY="${mujoco_library}" \
    -DREACHY_MUJOCO_EXPECTED_VERSION="${MUJOCO_VERSION}" \
    -DCMAKE_BUILD_TYPE=Release
cmake --build "${real_build}" --parallel 2 --target \
    reachy_mujoco_compile_runner \
    reachy_sim_dynamics_state_runner
mapfile -t compile_runners < <(
    find "${real_build}" -type f -name reachy_mujoco_compile_runner -print
)
mapfile -t state_runners < <(
    find "${real_build}" -type f -name reachy_sim_dynamics_state_runner -print
)
if [[ "${#compile_runners[@]}" -ne 1 || "${#state_runners[@]}" -ne 1 ]]; then
    printf 'Expected one compile runner and one state runner; found %s and %s.\n' \
        "${#compile_runners[@]}" "${#state_runners[@]}" >&2
    exit 1
fi
compile_runner="${compile_runners[0]}"
state_runner="${state_runners[0]}"
export LD_LIBRARY_PATH="$(dirname "${mujoco_library}")${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
"${compile_runner}" \
    "${enhanced_root}/reachy_mini.xml" \
    "${state_dir}/neutral.mjb" \
    | tee "${report_dir}/rma065-native-compile-neutral.json"
"${compile_runner}" \
    "${fixture_dir}/internal-contact.xml" \
    "${state_dir}/internal-contact.mjb" \
    | tee "${report_dir}/rma065-native-compile-contact.json"
"${state_runner}" "${state_dir}/neutral.mjb" 0 \
    | tee "${report_dir}/rma065-native-state-neutral.json"
"${state_runner}" "${state_dir}/internal-contact.mjb" 1 \
    | tee "${report_dir}/rma065-native-state-contact.json"

MUJOCO_ANDROID_PLATFORM="${ANDROID_PLATFORM}" \
MUJOCO_ANDROID_BUILD_DIR="${WORK_DIR}/mujoco-build-android" \
MUJOCO_ANDROID_OUTPUT_DIR="${ARTIFACT_DIR}" \
REACHY_PROBE_ANDROID_BUILD_DIR="${WORK_DIR}/probe-build-android" \
RMA065_SOURCE_MODEL_DIR="${source_model_dir}" \
RMA065_ENHANCED_MODEL_DIR="${enhanced_root}" \
    bash scripts/build_rma065_android.sh
cp -a "${report_dir}" "${ARTIFACT_DIR}/desktop-report"
cp "${enhanced_root}/RMA065_COLLISION_METADATA.json" \
    "${ARTIFACT_DIR}/RMA065_COLLISION_METADATA.json"

prebuilt_root="${ANDROID_NDK_HOME}/toolchains/llvm/prebuilt"
mapfile -t readelf_candidates < <(
    find "${prebuilt_root}" \
        \( -type f -o -type l \) \
        -path '*/bin/llvm-readelf' \
        -print
)
if [[ "${#readelf_candidates[@]}" -ne 1 ]]; then
    printf 'Expected one NDK llvm-readelf, found %s.\n' \
        "${#readelf_candidates[@]}" >&2
    exit 1
fi
"${readelf_candidates[0]}" -h \
    "${ARTIFACT_DIR}/reachy_mujoco_collision_benchmark_runner" \
    | tee "${ARTIFACT_DIR}/collision-benchmark-runner.elf-header.txt"
grep -F 'Machine:' \
    "${ARTIFACT_DIR}/collision-benchmark-runner.elf-header.txt" \
    | grep -F 'AArch64'

for required in \
    libmujoco.so \
    reachy_mujoco_collision_benchmark_runner \
    source-model/reachy_mini.xml \
    enhanced-model/reachy_mini.xml \
    collision-hard-stop-baseline.json \
    RMA065_COLLISION_METADATA.json \
    desktop-report/rma065-enhanced-audit.json \
    desktop-report/rma065-collision-hard-stop-validation.json \
    desktop-report/rma065-report-verification.json \
    desktop-report/rma065-native-compile-neutral.json \
    desktop-report/rma065-native-compile-contact.json \
    desktop-report/rma065-native-state-neutral.json \
    desktop-report/rma065-native-state-contact.json; do
    if [[ ! -s "${ARTIFACT_DIR}/${required}" ]]; then
        printf 'Android benchmark artifact is incomplete: %s\n' \
            "${ARTIFACT_DIR}/${required}" >&2
        exit 1
    fi
done

printf 'RMA-065 hosted gate passed; artifact staged in %s\n' "${ARTIFACT_DIR}"
