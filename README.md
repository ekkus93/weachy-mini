# Weachy Mini

[![CI](https://github.com/ekkus93/weachy-mini/actions/workflows/ci.yml/badge.svg)](https://github.com/ekkus93/weachy-mini/actions/workflows/ci.yml)

Weachy Mini is a free, noncommercial Android application that runs a virtual Reachy Mini robot in Unity. MuJoCo is the authoritative dynamics engine; Unity renders the robot, provides the Android user interface, and integrates camera, speech, local-model, and BYOK cloud-provider paths.

The project is in release-hardening. Several core physical-device gates now exist, but the authoritative TODO remains the source of truth for which release scenarios are fully qualified.

## Platform target

- Unity 6000.5.2f1
- Android ARM64 (`arm64-v8a`)
- IL2CPP for release builds
- Vulkan preferred; OpenGL ES 3 is a later validated fallback
- Physical-device acceptance currently requires one connected ARM64 API-26+ Android device; recent qualification has used a Samsung A54-class device.

The final public release floor must follow the checked release profile and the authoritative TODO evidence, not an undocumented local assumption.

## Authoritative project documents

- [Implementation specification](docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md)
- [Ordered implementation TODO](docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md)
- [Build instructions](docs/BUILDING.md)
- [Android release and user guide](docs/ANDROID_RELEASE_GUIDE.md)
- [GitHub Actions setup](docs/ci/GITHUB_ACTIONS_SETUP.md)
- [Asset policy](docs/ASSET_POLICY.md)
- [Warning policy](docs/WARNING_POLICY.md)

## Build entry points

```bash
# Validate repository structure, documentation links, inventories, and toolchain manifest.
./scripts/ci.sh --static-only

# Build and test first-party native code on the desktop.
./scripts/build_native.sh

# Verify a developer machine against toolchain.lock.json.
python3 scripts/verify_toolchain.py

# Build a Unity development APK or release AAB after Unity is installed.
./scripts/build_unity_android.sh development
./scripts/build_unity_android.sh release
```

The Android Gradle bridge is in `android-plugin/`. Its wrapper JAR is intentionally not hand-authored; generate and verify it with the pinned Gradle distribution before the bridge is first built.

## Repository layout

- `Assets/ReachyMini/`: first-party Unity runtime, editor tooling, and Unity tests
- `Assets/Plugins/Android/`: packaged Android plug-ins copied from verified build outputs
- `android-plugin/`: Android library source used by Unity
- `native/reachy_sim/`: first-party simulation wrapper and desktop tests
- `native/llama_runtime/`: first-party llama.cpp wrapper integration surface
- `models/manifests/`: model metadata only; model binaries are not committed
- `calibration/schemas/`: versioned calibration schemas; generated datasets are not committed
- `third_party/`: dependency inventory and notices; vendored code is kept separate from first-party code

## Warning policy

Warnings in first-party code are treated as bugs. Builds and CI use warnings-as-errors where the tool supports it. Warnings in third-party source are not fixed, hidden, or suppressed by modifying that source; third-party targets must remain isolated from first-party warning policy.

## Licensing

The repository's first-party source is licensed under Apache License 2.0. Reachy-derived hardware/model assets and other dependencies retain their own licenses. See `third_party/THIRD_PARTY_NOTICES.md` and `third_party/inventory.json` before adding or distributing any third-party material.
