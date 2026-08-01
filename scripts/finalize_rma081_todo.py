#!/usr/bin/env python3
"""Record the validated RMA-081 completion in the authoritative TODO."""

from pathlib import Path

TODO = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
START = "## RMA-081 — Build the main screen\n"
END = "## RMA-082 — Build settings screens\n"
REPLACEMENT = """## RMA-081 — Build the main screen

**Status:** Complete (2026-07-31)

- [x] Display Reachy using the fixed front/three-quarter camera.
- [x] Show concise state: idle, listening, transcribing, thinking, speaking, interrupted, unavailable, error.
- [x] Show active camera and local/cloud provider indicators.
- [x] Add microphone, camera selector, settings, and diagnostics controls.
- [x] Do not add orbit/pan/free-camera gestures.

**Completion evidence**

- `ReachyMainScreenStateStore` publishes immutable revisioned snapshots for the
  complete interaction-state vocabulary, active camera, provider location,
  capability availability, and mutually exclusive settings/diagnostics panels.
- `ReachyMainScreenBootstrap` installs exactly one production shell only when
  the generated Reachy root, authoritative runtime, tagged main camera, and
  fixed non-navigable presentation metadata are present. It creates no fallback
  camera or alternate scene.
- The production composition supplies all eight RMA-080 boundaries. Missing
  audio, provider, perception, and behavior capabilities are explicitly
  unavailable optional services, so the application accurately reports
  `Degraded` rather than falsely reporting `Ready`.
- Microphone and camera-selector requests surface actionable unavailable
  diagnostics. Settings and diagnostics controls open visible panels; no
  request is silently discarded.
- Static and Unity contracts reject camera-navigation paths and prove that all
  controls leave the presentation camera position, rotation, and
  `AcceptsUserNavigation == false` unchanged.
- Self-hosted run `30679297685` passed 74 Unity edit-mode tests, one play-mode
  test, the ARM64 API-26 IL2CPP build, installed LG-phone native lifecycle
  acceptance, and installed authoritative-rendering acceptance on exact commit
  `61737fe03b370181430f8ecd93a2a240cc9a47b2`.
- Detailed design and evidence are in
  `docs/architecture/MAIN_SCREEN_APPLICATION_SHELL.md` and
  `docs/validation/RMA_081_MAIN_SCREEN_VALIDATION_2026-07-31.md`.

"""


def main() -> None:
    text = TODO.read_text(encoding="utf-8")
    start = text.index(START)
    end = text.index(END, start)
    if "**Status:** Complete (2026-07-31)" in text[start:end]:
        return
    TODO.write_text(text[:start] + REPLACEMENT + text[end:], encoding="utf-8")


if __name__ == "__main__":
    main()
