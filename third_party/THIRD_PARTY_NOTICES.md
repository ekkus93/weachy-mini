# Third-party notices

This file inventories dependencies planned or used by Weachy Mini. It is not a substitute for the complete license text that must accompany a packaged release.

At the current scaffold stage, no MuJoCo, Reachy model asset, llama.cpp source, local model binary, CameraX library, or computer-vision SDK is vendored or packaged in the application.

## Planned dependencies

- **Unity 6.3 LTS** — governed by Unity's software terms and package-specific notices. The Unity editor is not redistributed by this repository.
- **MuJoCo** — Google DeepMind, Apache License 2.0. A release and source commit will be pinned during RMA-020 before source or binaries are imported.
- **Reachy Mini software** — Pollen Robotics / Hugging Face, generally Apache License 2.0 for software. The initial source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`; exact imported paths are recorded by the generated provenance report.
- **Reachy Mini hardware/model assets** — Pollen Robotics, CC BY-NC-SA as identified by the upstream project. The source revision is pinned to `a739a6e461eb6d722901f1cfc225265ffc85c28d`. Redistribution remains blocked until each imported asset's notice requirements are verified for release packaging.
- **llama.cpp** — MIT License. A source revision will be pinned during RMA-130.
- **Qwen3-0.6B-class model** — candidate only; no model is selected or distributed. License and exact artifact must be verified during RMA-133.
- **AndroidX CameraX** — Android Open Source Project / Google, Apache License 2.0 for the relevant libraries. Versions will be pinned during RMA-090.
- **On-device computer-vision provider** — not selected. No SDK may be packaged until its license and redistribution terms are recorded.
- **GitHub Actions used by CI** — `actions/checkout`, `actions/setup-python`, `actions/setup-dotnet`, and `actions/setup-java`; CI-only dependencies, not packaged in the Android app.

The application must include an offline licenses and attribution screen before any release gate is passed. The project is unofficial and is not endorsed by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI.
