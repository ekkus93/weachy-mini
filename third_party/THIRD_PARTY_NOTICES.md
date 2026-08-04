# Third-party notices

This file inventories dependencies planned or used by Weachy Mini. It is not a substitute for the complete license text that must accompany a packaged release.

CameraX is now a packaged Android dependency. No MuJoCo source archive, llama.cpp source, local model binary, or computer-vision SDK is vendored by this repository. Reachy model assets are prepared from their pinned upstream source during validated builds rather than checked in as opaque binaries.

## Dependencies

- **Unity 6.3 LTS** — governed by Unity's software terms and package-specific notices. The Unity editor is not redistributed by this repository.
- **MuJoCo** — Google DeepMind, Apache License 2.0. Version 3.9.0 is pinned at commit `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`. Source and binaries are staged through the validated build pipeline.
- **Reachy Mini software** — Pollen Robotics / Hugging Face, generally Apache License 2.0 for software. The initial source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`; exact imported paths are recorded by the generated provenance report.
- **Reachy Mini hardware/model assets** — Pollen Robotics, CC BY-NC-SA as identified by the upstream project. The source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`. Redistribution remains subject to each imported asset's attribution, noncommercial, ShareAlike, and modification-notice requirements.
- **AndroidX CameraX 1.6.1** — Android Open Source Project / Google, Apache License 2.0. The packaged modules are `camera-core`, `camera-camera2`, and `camera-lifecycle`; they are consumed unmodified from Google's Maven repository.
- **llama.cpp** — MIT License. A source revision will be pinned during RMA-130.
- **Qwen3-0.6B-class model** — candidate only; no model is selected or distributed. License and exact artifact must be verified during RMA-133.
- **On-device computer-vision provider** — not selected. No SDK may be packaged until its license and redistribution terms are recorded.
- **GitHub Actions used by CI** — `actions/checkout`, `actions/setup-python`, `actions/setup-dotnet`, and `actions/setup-java`; CI-only dependencies, not packaged in the Android app.

The application includes an offline licenses and attribution screen. The project is unofficial and is not endorsed by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI.
