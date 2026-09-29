# Third-party notices

This file inventories dependencies planned or used by Weachy Mini. It is not a substitute for the complete license text that must accompany a packaged release.

CameraX and the bundled ML Kit face-detection and selfie-segmentation artifacts are packaged Android dependencies. No MuJoCo source archive, llama.cpp source checkout, or local language-model binary is vendored by this repository. Reachy model assets and llama.cpp are prepared from exact pinned upstream sources during validated builds rather than checked in as opaque binaries. The selected Qwen3 GGUF remains an external model acquired through the verified model download/import path; it is not bundled by this repository.

## Dependencies

- **Unity 6.3 LTS** — governed by Unity's software terms and package-specific notices. The Unity editor is not redistributed by this repository.
- **MuJoCo** — Google DeepMind, Apache License 2.0. Version 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. Source and binaries are staged through the validated build pipeline.
- **Reachy Mini software** — Pollen Robotics / Hugging Face, generally Apache License 2.0 for software. The imported source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`; exact imported paths and transformations are recorded by the generated provenance report.
- **Reachy Mini hardware/model assets** — Pollen Robotics, CC BY-NC-SA as identified by the upstream project. The imported source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`. Redistribution remains subject to each imported asset's attribution, noncommercial, ShareAlike, and modification-notice requirements, with generated provenance recording the transformed paths.
- **AndroidX CameraX 1.6.1** — Android Open Source Project / Google, Apache License 2.0. The packaged modules are `camera-core`, `camera-camera2`, and `camera-lifecycle`; they are consumed unmodified from Google's Maven repository.
- **llama.cpp b10313** — ggml-org contributors, MIT License, pinned to commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`. The source checkout is fetched and verified during validated builds and is not vendored. RMA-130 statically links the unmodified pinned runtime into generated `libreachy_llama.so`; its first-party versioned ABI hides upstream symbols. The pinned upstream `LICENSE` Git blob is `e7dca554bcb802f98408383a864404e3aa4eacca`.
- **Qwen3 0.6B Q4_K_M** — selected by RMA-133 for the local-LLM path. The model manifest pins source `Qwen/Qwen3-0.6B-GGUF` revision `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`, Apache-2.0 license, and GGUF SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`. The GGUF is not bundled or vendored by this repository; installation uses the verified download/import path.
- **Google ML Kit bundled face detection 16.1.7** — Google. Packaged as `com.google.mlkit:face-detection:16.1.7`; governed by the Google ML Kit Terms of Service and applicable generated dependency notices. The model is included in the APK and is not downloaded through Google Play Services.
- **Google ML Kit bundled selfie segmentation 16.0.0-beta6** — Google. Packaged as `com.google.mlkit:segmentation-selfie:16.0.0-beta6`; governed by the Google ML Kit Terms of Service and applicable generated dependency notices. This beta model is included in the APK and isolated behind the RMA-111 backend contract.
- **RMA-111 face acceptance fixture** — 250-pixel Wikimedia Commons thumbnail of `BarackObamaportrait.jpg`, official United States Senate portrait, public domain under PD-USGov-Congress. Source: `https://commons.wikimedia.org/wiki/File:BarackObamaportrait.jpg`. Exact packaged JPEG SHA-256: `bfbc798f321699c95c708476f744ba52e9faccdb0d131b8ce878f47d7704c8de`.
- **GitHub Actions used by CI** — `actions/checkout`, `actions/setup-python`, `actions/setup-dotnet`, and `actions/setup-java`; CI-only dependencies, not packaged in the Android app.

The application includes an offline licenses and attribution screen. The project is unofficial and is not endorsed by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI. It is intended to be free and noncommercial.
