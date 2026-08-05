#!/usr/bin/env python3
from pathlib import Path

path = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
text = path.read_text(encoding="utf-8")
start = text.index("## RMA-110 — Define vision provider contracts")
evidence = text.index("**Completion evidence**", start)
end = text.index("\n## RMA-111 —", evidence)
replacement = """**Completion evidence**

- `ReachyMini.Perception` defines separate frame-source, lightweight-tracker,
  and semantic-VLM boundaries with explicit identities, capabilities, locality,
  bounded requests, and monotonic provider-selection epochs.
- Normal perception consumes owned transformed Reachy-eye frame leases carrying
  color, validity mask, coverage, calibration/model provenance, timestamps,
  continuity, orientation, and mirror metadata. Raw phone frames remain limited
  to `ExplicitRawDebug`.
- Cancellation, timeout, provider failure, invalid frame, unavailable provider,
  contract violation, and supersession are typed and visible. The executor does
  not retry, substitute a fallback provider, reuse stale output, or silently
  cross the raw/transformed privacy boundary.
- The managed RMA-110 suite is awaited from the project's real async `Main`.
  Fake providers, frame leases, and frame resources use deterministic
  `await using` ownership; the permanent gate rejects the former synchronous
  module-initializer bootstrap and tracked repair artifacts.
- Permanent RMA-110 run `31050417256`, job `92456020415`, passed on exact SHA
  `64587bae3b977ff16f6a9d3f7b416af0b1f64a62`.
- Hosted CI run `31050417844` passed static, managed warnings-as-errors,
  native/sanitizer, Android, and pinned Reachy-model jobs on that SHA.
- Self-hosted run `31050417574`, job `92456090409`, passed `125/125` EditMode,
  `1/1` PlayMode, ARM64 API-26 build/verification, RMA-090, RMA-091, RMA-092,
  lifecycle, authoritative rendering, every evidence upload, APK upload, and
  final status publication on that SHA.
- RMA-092 recorded CameraX `CLOSED` before both subsequent starts, physical
  Vulkan output, rear rotations at 0 and 90 degrees, mirrored front output at
  270 degrees, exact timestamp correspondence, and zero stale texture frames.
- Detailed evidence is in `docs/architecture/VISION_PROVIDER_CONTRACTS.md` and
  `docs/validation/RMA_110_VISION_PROVIDER_CONTRACTS_VALIDATION_2026-08-05.md`.
"""
path.write_text(text[:evidence] + replacement.rstrip() + "\n" + text[end:], encoding="utf-8")
Path(__file__).unlink()
