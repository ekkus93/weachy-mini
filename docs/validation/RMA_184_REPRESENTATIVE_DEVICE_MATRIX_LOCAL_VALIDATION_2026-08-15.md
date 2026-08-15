# RMA-184 representative-device matrix local validation — 2026-08-15

**Status:** Repository validation passed; physical qualification pending

## Implemented

- Added the authoritative `rma184_representative_device_matrix_v1` JSON registry with low/mid/high classes, representative devices, explicit support criteria, and initial class defaults.
- Added a pure managed support policy distinguishing unsupported devices, core-supported devices with limitations, and devices eligible for full offline interaction.
- Added deterministic class defaults tied to the existing `Conservative`, `Balanced`, and `Performance` local-LLM profiles.
- Added a 30-minute long-run qualification policy covering render p95, fixed-step physics p95, bounded post-warmup memory growth, bounded state-lag growth, the existing 1 tok/s local-LLM floor, and the RMA-181 thermal degradation order.
- Added an opt-in Android device probe that records runtime-visible RAM, Android/API, SoC/hardware identity, graphics API/GPU, camera capability, and speech-service availability.
- Added `scripts/run_rma184_device_probe_android.sh` to launch and pull that sanitized probe evidence.
- Added managed and Python/static contract coverage.

## Local checks

The local sandbox has no Unity Editor or .NET SDK, so Unity/Roslyn compilation cannot be executed here. Validation therefore uses repository static contracts plus the existing Python regression suite. The new C# is included automatically by `managed/ReachyMini.Core/ReachyMini.Core.csproj` and the Unity runtime assembly through their existing recursive source inclusion.

Physical-device characterization is intentionally not claimed by this record. The representative device entries that have not run the RMA-184 probe remain `pending_measurement`, and the authoritative roadmap acceptance bullets remain open until the long-running device evidence exists.
