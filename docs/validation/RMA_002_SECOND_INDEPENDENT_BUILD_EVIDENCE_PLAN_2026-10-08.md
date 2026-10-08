# RMA-002 — Second independently provisioned Unity/Android build

**Status: open (2026-10-08).** This is a reproducibility qualification
plan, not proof of two independent builds and not a release acceptance claim.

The current evidence in
`docs/validation/RMA_002_TWO_MACHINE_ANDROID_BUILD_VALIDATION_2026-10-07.md`
documents builds that share one physical host. Repeating the build on that
host, using another checkout or another self-hosted runner label on the same
machine, does not meet the authoritative RMA-002 acceptance criterion.

## Minimum second-machine qualification

1. Provision an independent Linux/Unity/Android environment without
   reusing the first machine's Unity, Android SDK, build cache, or project
   build outputs. Preserve a sanitized provisioning record and runner identity.
2. Check out the **same exact** `master` SHA as the first build. Verify
   `git status --porcelain` is empty before source generation.
3. Run `python3 scripts/verify_toolchain.py` and record the installed
   Unity, Android SDK/NDK/CMake, JDK, Gradle, and quality-tool pins.
4. Generate the Reachy Unity presentation and stage the production MuJoCo
   runtime using the checked-in scripts, then build the ARM64/API-26 device
   APK with `bash scripts/build_unity_android.sh device-feasibility`.
5. Record the exact build SHA, runner/provisioning provenance, exit status,
   Android package identity, minimum/target SDK, native-library inventory,
   APK SHA-256, and CI/build-log reference for **each** independent machine.
   Check that the APK packages production MuJoCo, Reachy simulation, and
   IL2CPP ARM64 libraries; do not substitute a fixture application.
6. Compare both results. Binary hashes may differ due to nondeterministic
   packaging; distinguish structural application compatibility from strict
   byte-for-byte reproducibility, and explain any material differences.
7. Have the operator verify that the environments are genuinely independent.
   Only then reconcile the RMA-002 checkbox with both exact-head build logs,
   evidence records, and the comparison report.

No API key, local model, physical Reachy Mini, or connected Android phone is
required to **build** the APK. Physical execution, offline speech, local LLM
performance, thermal long runs, and final release acceptance remain separate
open gates. A queued self-hosted workflow or a hosted static test pass is not
evidence that the second independently provisioned Unity build succeeded.
