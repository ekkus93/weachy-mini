#!/usr/bin/env python3
"""Record RMA-082 implementation progress and the exact validation blocker."""

from pathlib import Path

TODO = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
START = "## RMA-082 — Build settings screens\n"
END = "---\n\n# Phase 10 — Android CameraX bridge\n"
REPLACEMENT = """## RMA-082 — Build settings screens

**Status:** In progress — implementation and hosted contracts complete; Unity and installed-device validation blocked by inactive Unity license on `kawa` (2026-08-03)

- [x] Providers: independent ASR/TTS/LLM/VLM selection.
- [x] Camera: front/rear, preview, calibration, reprojection diagnostics.
- [x] Speech: language, voice, offline/network status.
- [x] Local model: install/import/select/delete and resource settings.
- [x] Simulation: fidelity mode, calibration profile, reset, diagnostic controls.
- [x] Privacy: cloud-bound data indicators, history/retention options.
- [x] Licenses and attribution.

**Acceptance criteria**

- [ ] Every provider or capability unavailable state is visible and actionable on the validated Unity/Android build.
- [ ] Settings do not imply offline operation when a network-backed Android service is selected on the validated Unity/Android build.

**Implementation and blocker evidence**

- `ReachySettingsStateStore` provides immutable revisioned settings for all
  seven sections and independent ASR, TTS, LLM, and VLM selections. Android
  service and cloud selections are structurally required to declare
  `NetworkRequired`; privacy summaries list every off-device selection.
- `ReachySettingsPersistenceApplicationService` writes schema-versioned durable
  JSON, sanitizes unsupported values, uses temporary/backup replacement, and
  quarantines invalid files with visible degraded health.
- `ReachySettingsApplicationCompositionProvider` supplies all eight RMA-080
  boundaries. Preferences never convert unavailable runtime integrations into
  false ready health.
- The settings UI exposes front/rear preference; camera preview, calibration,
  and reprojection entry points; speech language/voice/network status; local
  model package/resource actions; simulation fidelity/reset/diagnostics;
  privacy/history/retention; and licenses/attribution. Unimplemented operations
  remain actionable and publish the reason they are unavailable.
- Hosted run `30847149038`, job `91798117294`, passed managed warnings-as-errors
  and the permanent RMA-082 state, persistence, settings-surface,
  network-truthfulness, privacy, service-boundary, and fixed-camera contracts on
  source commit `fb267f9a459e48e5acd33aa9022f73b399f65479`.
- Self-hosted run `30847148996` did not reach Unity project compilation. Jobs
  `91798553550` and `91799008188` both resolved Unity 6000.5.2f1 and the Android
  toolchain, then exited with status 198 because the Unity Licensing Client had
  no access token or Editor entitlement: `No valid Unity Editor license found`.
- RMA-082 must remain in progress until Unity Hub is signed in and the permanent
  Unity/Android workflow passes Unity tests, ARM64 API-26 build/verification,
  installed LG-phone lifecycle acceptance, and installed authoritative-rendering
  acceptance on one exact current `master` SHA.
- Detailed design and current evidence are in
  `docs/architecture/SETTINGS_ARCHITECTURE.md` and
  `docs/validation/RMA_082_SETTINGS_VALIDATION_2026-08-03.md`.

"""


def main() -> None:
    text = TODO.read_text(encoding="utf-8")
    start = text.index(START)
    end = text.index(END, start)
    if "inactive Unity license on `kawa`" in text[start:end]:
        return
    TODO.write_text(text[:start] + REPLACEMENT + text[end:], encoding="utf-8")


if __name__ == "__main__":
    main()
