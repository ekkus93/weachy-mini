#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"

: "${REACHY_MANAGED_NATIVE_LIBRARY_DIR:?REACHY_MANAGED_NATIVE_LIBRARY_DIR must point to the managed native contract library}"

export LD_LIBRARY_PATH="${REACHY_MANAGED_NATIVE_LIBRARY_DIR}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
export REACHY_MANAGED_NATIVE_TESTS=1

projects=(
  "managed/ReachyMini.AndroidOnDeviceAsr.Tests/ReachyMini.AndroidOnDeviceAsr.Tests.csproj"
  "managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj"
  "managed/ReachyMini.LocalModelPackages.Tests/ReachyMini.LocalModelPackages.Tests.csproj"
  "managed/ReachyMini.LocalLlm.Tests/ReachyMini.LocalLlm.Tests.csproj"
)

for relative_project in "${projects[@]}"; do
  project="${ROOT_DIR}/${relative_project}"
  printf 'RMA-190 failure matrix: %s\n' "${relative_project}"
  dotnet restore "${project}"
  dotnet run --project "${project}" --configuration Release --no-restore
done

printf '%s\n' 'RMA-190 failure matrix managed suites passed.'
