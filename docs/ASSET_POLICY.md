# Asset and large-file policy

## Commit directly

The repository stores first-party source code, build definitions, small deterministic test fixtures, schemas, manifests, attribution text, and compact validation summaries.

## Fetch or import deterministically

Reachy MJCF/mesh assets, MuJoCo source, llama.cpp source, and other upstream material must be obtained from a pinned source revision through a script that verifies hashes and records provenance. Generated Unity assets belong under `Assets/Generated/` and are not committed unless a later review documents why deterministic regeneration is insufficient.

## Git LFS

Git LFS may be used only for approved redistributable binary fixtures whose source, license, revision, and hash are recorded in `third_party/inventory.json`. LFS tracking does not itself grant redistribution permission.

## Never commit

- API keys, tokens, keystores, signing material, or local `.env` files
- local Android SDK/NDK paths or `local.properties`
- GGUF, safetensors, ONNX, TFLite, or other model binaries
- raw camera frames, microphone recordings, transcripts, or private diagnostics
- raw calibration captures or generated datasets before a privacy and licensing review
- Unity `Library`, Gradle, CMake, Android Studio, or IDE caches

Missing or modified upstream inputs must fail visibly; import tooling must not silently substitute another revision or cached asset.
