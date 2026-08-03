#!/usr/bin/env python3
"""Record accepted RMA-082 evidence in the authoritative implementation TODO."""

from pathlib import Path

TODO = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
START = "## RMA-082 — Build settings screens\n"
END = "---\n\n# Phase 10 — Android CameraX bridge\n"
REPLACEMENT = """## RMA-082 — Build settings screens

**Status:** Complete (2026-08-03)

- [x] Providers: independent ASR/TTS/LLM/VLM selection.
- [x] Camera: front/rear, preview, calibration, reprojection diagnostics.
- [x] Speech: language, voice, offline/network status.
- [x] Local model: install/import/select/delete and resource settings.
- [x] Simulation: fidelity mode, calibration profile, reset, diagnostic controls.
- [x] Privacy: cloud-bound data indicators, history/retention options.
- [x] Licenses and attribution.

**Acceptance criteria**

- [x] Every provider or capability unavailable state is visible and actionable.
- [x] Settings do not imply offline operation when a network-backed Android service is selected.

**Completion evidence**

- `ReachySettingsStateStore` publishes immutable, revisioned settings for all
  seven sections and independent ASR, TTS, LLM, and VLM selections. Android
  service and cloud choices are structurally required to declare
  `NetworkRequired`; privacy summaries identify every off-device selection.
- `ReachySettingsPersistenceApplicationService` writes schema-versioned durable
  JSON, sanitizes unsupported values, uses temporary/backup replacement, and
  quarantines invalid files with visible degraded health.
- `ReachySettingsApplicationCompositionProvider` supplies all eight RMA-080
  boundaries. A stored preference never upgrades an unavailable runtime
  integration into false ready health.
- Camera preview/calibration/reprojection and local-model package actions remain
  enabled explanatory entry points and publish explicit unavailable reasons.
  Simulation reset routes through the authoritative runtime, and all settings
  actions preserve the fixed non-navigable presentation camera.
- Hosted run `30851077541`, job `91810969892`, passed managed
  warnings-as-errors and the permanent settings-state, persistence,
  service-boundary, privacy, network-truthfulness, and fixed-camera contracts on
  exact commit `96c7113eccca7eec4afc8fb5d346a56e0782126f`.
- Self-hosted run `30851077505`, job `91811041976`, passed deterministic Unity
  import, production MuJoCo staging, all Unity edit-mode/play-mode tests, ARM64
  API-26 IL2CPP build and verification, installed LG-phone lifecycle acceptance,
  installed authoritative-rendering acceptance, and all evidence uploads on the
  same exact commit.
- The validation artifacts and accepted evidence are recorded in
  `docs/architecture/SETTINGS_ARCHITECTURE.md` and
  `docs/validation/RMA_082_SETTINGS_VALIDATION_2026-08-03.md`.

"""


def main() -> None:
    text = TODO.read_text(encoding="utf-8")
    start = text.index(START)
    end = text.index(END, start)
    if "**Status:** Complete (2026-08-03)" in text[start:end]:
        return
    TODO.write_text(text[:start] + REPLACEMENT + text[end:], encoding="utf-8")


if __name__ == "__main__":
    main()
