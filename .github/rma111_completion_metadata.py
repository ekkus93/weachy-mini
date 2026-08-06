from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd()
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
HOSTED_WORKFLOW = ROOT / ".github/workflows/rma090-camera-discovery.yml"


def replace_once(path: Path, old: str, new: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected one target in {path}; found {count}.")
    path.write_text(source.replace(old, new), encoding="utf-8")


old_section = '''## RMA-111 — Implement on-device lightweight tracking

- [ ] Select and document ML Kit, MediaPipe, LiteRT, or another mobile-compatible approach.
- [ ] Implement face/person tracking first.
- [ ] Add basic object or motion tracking only if performance supports it.
- [ ] Convert detections to the transformed Reachy-eye coordinate system.
- [ ] Do not report detections centered in invalid pixels.
- [ ] Add stable local IDs and expiry.
'''

new_section = '''## RMA-111 — Implement on-device lightweight tracking

**Status:** Complete (2026-08-05)

- [x] Select and document ML Kit, MediaPipe, LiteRT, or another mobile-compatible approach.
- [x] Implement face/person tracking first.
- [x] Add basic object or motion tracking only if performance supports it.
  - Object and generic motion tracking remain disabled because RMA-111 has no
    measured physical-device evidence that they fit the bounded mobile path.
- [x] Convert detections to the transformed Reachy-eye coordinate system.
- [x] Do not report detections centered in invalid pixels.
- [x] Add stable local IDs and expiry.

**Completion evidence**

- The production provider uses bundled Google ML Kit face detection 16.1.7 and
  selfie segmentation 16.0.0-beta6 behind the RMA-110 `IVisualTracker`
  boundary. It consumes owned transformed Reachy-eye frames and validity
  metadata; it does not invoke a VLM or download a model at runtime.
- Managed contracts cover ownership, bounded concurrency, cancellation,
  provider failure, transformed coordinates, validity filtering, deterministic
  local IDs, expiry, and continuity reset behavior.
- Physical validation run `31078197317`, job `92540794256`, passed the complete
  Unity/Android suite on an LG-H872 running Android 8.0.0/API 26/arm64-v8a.
- RMA-111 artifact `8958559677` reports bundled face/person inference, one face
  and one person on both frames, stable `face-000001` and `person-000001` IDs,
  invalid-center suppression, zero VLM invocations, and no network model
  download.
- The same exact run passed RMA-090, RMA-091, RMA-092, RMA-022 lifecycle, and
  authoritative rendering. The authoritative gate verified the installed APK
  SHA-256 against the candidate before reuse, cleared application data, and
  completed with no hidden kinematic fallback.
- Detailed evidence and the rejected-candidate history are in
  `docs/validation/RMA_111_LIGHTWEIGHT_TRACKING_VALIDATION_2026-08-05.md`.
'''

replace_once(TODO, old_section, new_section)

old_paths = '''      - 'docs/validation/RMA_090_CAMERA_CAPABILITY_DISCOVERY_VALIDATION_2026-08-03.md'
      - 'docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md'
'''
new_paths = '''      - 'docs/validation/RMA_090_CAMERA_CAPABILITY_DISCOVERY_VALIDATION_2026-08-03.md'
      - 'docs/validation/RMA_111_LIGHTWEIGHT_TRACKING_VALIDATION_2026-08-05.md'
      - 'docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md'
'''
replace_once(HOSTED_WORKFLOW, old_paths, new_paths)
print("RMA-111 completion metadata applied.")
